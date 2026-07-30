using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TypeyTypey;

/// <summary>
/// Recovers from a Task Scheduler parser rejection by dropping an optional element the local
/// Windows build does not understand, and retrying.
///
/// This exists because a task definition is validated against the schema version it declares, and a
/// build that predates an element rejects the whole document rather than ignoring it:
///
///     ERROR: The task XML contains an unexpected node: (26,7) DisallowStartOnRemoteAppSession
///
/// The generated XML no longer contains such elements, so this should not normally fire. It stays
/// as a guard: the failure it recovers from is total — no task is created at all — and the error
/// names the offending element precisely enough to act on safely.
///
/// It is deliberately conservative. Only elements on <see cref="RemovableNodes"/> are dropped, and
/// everything that decides what the task *does* or *runs as* is excluded, so a rejection there
/// fails closed rather than quietly registering a task that differs from the one asked for.
/// </summary>
internal static class TaskXmlCompatibility
{
    /// <summary>
    /// Attempts, including the first. Two removals is already more schema drift than any supported
    /// Windows build should show; beyond that the disagreement is not the kind this can fix.
    /// </summary>
    public const int MaximumAttempts = 3;

    /// <summary>
    /// Optional elements that may be dropped, with the default that applies once they are gone.
    ///
    /// Both are <c>Settings</c> children introduced in task schema 1.3, both are booleans, and both
    /// default to the value this task wants anyway — so removing either changes nothing about how
    /// the task behaves. Nothing that names an account, a trigger, an action, a run level or a
    /// credential belongs here, and nothing may be added without the same argument.
    /// </summary>
    public static IReadOnlySet<string> RemovableNodes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "DisallowStartOnRemoteAppSession",
        "UseUnifiedSchedulingEngine"
    };

    private static readonly Regex UnexpectedNode = new(
        @"unexpected node[:\s]*(?:\(\s*(?<line>\d+)\s*,\s*(?<column>\d+)\s*\))?\s*(?<name>[A-Za-z_][\w.\-]*)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// The element name Task Scheduler objected to, or null when the output is not that failure.
    ///
    /// The message usually names the element. When it reports only a position, the name is read
    /// from that line of the XML that was submitted — which is why the caller passes it in.
    /// </summary>
    public static string? UnexpectedNodeName(string schedulerOutput, string submittedXml)
    {
        if (string.IsNullOrWhiteSpace(schedulerOutput))
            return null;

        Match match = UnexpectedNode.Match(schedulerOutput);
        if (!match.Success)
            return null;

        if (match.Groups["name"].Success && match.Groups["name"].Value.Length > 0)
            return match.Groups["name"].Value;

        if (!match.Groups["line"].Success || !int.TryParse(match.Groups["line"].Value, out int line))
            return null;

        return ElementNameAtLine(submittedXml, line);
    }

    /// <summary>The element opened on <paramref name="line"/> (1-based), or null.</summary>
    private static string? ElementNameAtLine(string xml, int line)
    {
        string[] lines = xml.Replace("\r\n", "\n").Split('\n');
        if (line < 1 || line > lines.Length)
            return null;

        Match element = Regex.Match(lines[line - 1], @"<\s*(?<name>[A-Za-z_][\w.\-]*)");
        return element.Success ? element.Groups["name"].Value : null;
    }

    /// <summary>Whether <paramref name="nodeName"/> may be dropped. Anything unknown is a no.</summary>
    public static bool IsRemovable(string? nodeName) =>
        nodeName is not null && RemovableNodes.Contains(nodeName);

    /// <summary>
    /// Returns <paramref name="xml"/> without every element named <paramref name="nodeName"/>,
    /// leaving the input untouched. False means the element was not there, the name is not
    /// removable, or the document did not parse — in each case the caller must not retry.
    ///
    /// Matching ignores the namespace. Task XML declares the Task Scheduler namespace as its
    /// default, so every element carries it, while the error message names the element without one;
    /// comparing the local name is what makes the two line up.
    /// </summary>
    public static bool TryRemoveNode(string xml, string? nodeName, out string reducedXml)
    {
        reducedXml = xml;
        if (!IsRemovable(nodeName))
            return false;

        XDocument document;
        try
        {
            document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }

        List<XElement> matches = [.. document.Descendants().Where(element => element.Name.LocalName == nodeName)];
        if (matches.Count == 0)
            return false;

        foreach (XElement element in matches)
        {
            // Whitespace was preserved so the document stays readable; drop the text node that
            // indented this element too, or the file keeps a blank line where it used to be.
            if (element.PreviousNode is XText whitespace && whitespace.Value.Trim().Length == 0)
                whitespace.Remove();
            element.Remove();
        }

        reducedXml = document.Declaration is null
            ? document.ToString(SaveOptions.DisableFormatting)
            : document.Declaration + Environment.NewLine + document.ToString(SaveOptions.DisableFormatting);
        return true;
    }
}
