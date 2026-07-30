using Xunit;

namespace TypeyTypey.Tests;

public sealed class UnexpectedNodeParsingTests
{
    private const string Xml = """
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <Settings>
            <Enabled>true</Enabled>
            <DisallowStartOnRemoteAppSession>false</DisallowStartOnRemoteAppSession>
          </Settings>
        </Task>
        """;

    [Fact]
    public void NameIsTakenFromTheMessageWhenItIsThere()
    {
        // The exact text reported from the affected system.
        const string output = "ERROR: The task XML contains an unexpected node: (26,7) DisallowStartOnRemoteAppSession";

        Assert.Equal("DisallowStartOnRemoteAppSession", TaskXmlCompatibility.UnexpectedNodeName(output, Xml));
    }

    [Fact]
    public void NameIsReadFromTheSubmittedXmlWhenOnlyAPositionIsReported()
    {
        // Line 5 of Xml above is the DisallowStartOnRemoteAppSession element.
        const string output = "ERROR: The task XML contains an unexpected node: (5,5)";

        Assert.Equal("DisallowStartOnRemoteAppSession", TaskXmlCompatibility.UnexpectedNodeName(output, Xml));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ERROR: Access is denied.")]
    [InlineData("ERROR: The system cannot find the file specified.")]
    public void UnrelatedFailuresAreNotTreatedAsAParserRejection(string output)
    {
        Assert.Null(TaskXmlCompatibility.UnexpectedNodeName(output, Xml));
    }

    [Fact]
    public void APositionOutsideTheDocumentYieldsNothing()
    {
        Assert.Null(TaskXmlCompatibility.UnexpectedNodeName("unexpected node: (900,4)", Xml));
    }
}

public sealed class RemovableNodeAllowlistTests
{
    [Fact]
    public void TheReportedNodeIsAllowlisted()
    {
        Assert.True(TaskXmlCompatibility.IsRemovable("DisallowStartOnRemoteAppSession"));
    }

    [Theory]
    [InlineData("UserId")]
    [InlineData("RunLevel")]
    [InlineData("LogonType")]
    [InlineData("Principal")]
    [InlineData("LogonTrigger")]
    [InlineData("BootTrigger")]
    [InlineData("Exec")]
    [InlineData("Command")]
    [InlineData("Actions")]
    [InlineData("Triggers")]
    public void NothingThatDecidesWhatTheTaskRunsOrRunsAsMayBeRemoved(string node)
    {
        // Dropping any of these would register a task that differs from the one asked for, which is
        // worse than failing.
        Assert.False(TaskXmlCompatibility.IsRemovable(node));
    }

    [Fact]
    public void UnknownAndMissingNamesAreRefused()
    {
        Assert.False(TaskXmlCompatibility.IsRemovable("SomeFutureElement"));
        Assert.False(TaskXmlCompatibility.IsRemovable(null));
    }

    [Fact]
    public void TheAllowlistStaysSmall()
    {
        // A guard on the list itself: growing it is a decision that needs the argument in the
        // doc comment, not a quiet addition.
        Assert.All(TaskXmlCompatibility.RemovableNodes,
            node => Assert.Contains(node, new[] { "DisallowStartOnRemoteAppSession", "UseUnifiedSchedulingEngine" }));
    }
}

public sealed class TaskXmlNodeRemovalTests
{
    private static string Definition() => ScheduledTaskManager.BuildTaskXml(
        AdminTaskMode.Logon, @"C:\Apps\TypeyTypey.exe", "S-1-5-21-1-2-3-1001");

    private const string WithNode = """
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <URI>\TypeyTypey</URI>
          </RegistrationInfo>
          <Settings>
            <Enabled>true</Enabled>
            <DisallowStartOnRemoteAppSession>false</DisallowStartOnRemoteAppSession>
            <Priority>7</Priority>
          </Settings>
        </Task>
        """;

    [Fact]
    public void TheNamespacedElementIsFoundAndRemoved()
    {
        // Task XML puts everything in the Task Scheduler namespace by default while the error names
        // the element without one, so matching has to be on the local name.
        Assert.True(TaskXmlCompatibility.TryRemoveNode(WithNode, "DisallowStartOnRemoteAppSession", out string reduced));

        Assert.DoesNotContain("DisallowStartOnRemoteAppSession", reduced);
    }

    [Fact]
    public void EverythingElseSurvivesRemoval()
    {
        TaskXmlCompatibility.TryRemoveNode(WithNode, "DisallowStartOnRemoteAppSession", out string reduced);

        Assert.Contains("<Priority>7</Priority>", reduced);
        Assert.Contains("<Enabled>true</Enabled>", reduced);
        Assert.Contains(@"<URI>\TypeyTypey</URI>", reduced);
        Assert.Contains("http://schemas.microsoft.com/windows/2004/02/mit/task", reduced);
        Assert.StartsWith("<?xml", reduced);
    }

    [Fact]
    public void TheOriginalIsNotModified()
    {
        string before = WithNode;

        TaskXmlCompatibility.TryRemoveNode(WithNode, "DisallowStartOnRemoteAppSession", out _);

        Assert.Equal(before, WithNode);
    }

    [Fact]
    public void ANodeThatIsNotPresentIsNotAFalseSuccess()
    {
        // Reporting success here would let the retry loop resubmit an identical document.
        Assert.False(TaskXmlCompatibility.TryRemoveNode(Definition(), "UseUnifiedSchedulingEngine", out _));
    }

    [Fact]
    public void ANonAllowlistedNodeIsNeverRemovedEvenWhenPresent()
    {
        Assert.False(TaskXmlCompatibility.TryRemoveNode(WithNode, "URI", out string reduced));
        Assert.Equal(WithNode, reduced);
    }

    [Fact]
    public void MalformedXmlIsRefusedRatherThanThrowing()
    {
        Assert.False(TaskXmlCompatibility.TryRemoveNode("<Task><Settings>", "DisallowStartOnRemoteAppSession", out _));
    }

    [Fact]
    public void TheGeneratedDefinitionDeclaresNoElementNewerThanItsSchema()
    {
        // The root cause of the reported failure: the document said version 1.2 while using two
        // elements introduced in 1.3, so Task Scheduler rejected the whole thing.
        string xml = Definition();

        Assert.Contains(@"version=""1.2""", xml);
        Assert.DoesNotContain("DisallowStartOnRemoteAppSession", xml);
        Assert.DoesNotContain("UseUnifiedSchedulingEngine", xml);
    }
}

public sealed class RegistrationRetryTests
{
    private const string Rejection = "ERROR: The task XML contains an unexpected node: (26,7) DisallowStartOnRemoteAppSession";

    private const string WithNode = """
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <Settings>
            <DisallowStartOnRemoteAppSession>false</DisallowStartOnRemoteAppSession>
            <UseUnifiedSchedulingEngine>true</UseUnifiedSchedulingEngine>
          </Settings>
        </Task>
        """;

    /// <summary>Records what was submitted so the test can assert on the sequence, not just the result.</summary>
    private sealed class FakeScheduler(params (int ExitCode, string Output)[] replies)
    {
        private int _calls;

        public List<string> Submitted { get; } = [];

        public (int ExitCode, string Output) Submit(string xml)
        {
            Submitted.Add(xml);
            return _calls < replies.Length ? replies[_calls++] : (0, string.Empty);
        }
    }

    [Fact]
    public void SuccessOnTheFirstAttemptSubmitsOnceAndRemovesNothing()
    {
        var scheduler = new FakeScheduler((0, string.Empty));

        ScheduledTaskManager.RegistrationOutcome outcome = ScheduledTaskManager.Register(WithNode, scheduler.Submit);

        Assert.True(outcome.Succeeded);
        Assert.Empty(outcome.RemovedNodes);
        Assert.Single(scheduler.Submitted);
        Assert.Equal(WithNode, scheduler.Submitted[0]);
    }

    [Fact]
    public void ARejectedOptionalNodeIsRemovedAndTheRetrySucceeds()
    {
        var scheduler = new FakeScheduler((1, Rejection), (0, string.Empty));

        ScheduledTaskManager.RegistrationOutcome outcome = ScheduledTaskManager.Register(WithNode, scheduler.Submit);

        Assert.True(outcome.Succeeded);
        Assert.Equal(["DisallowStartOnRemoteAppSession"], outcome.RemovedNodes);
        Assert.Equal(2, scheduler.Submitted.Count);
        Assert.Contains("DisallowStartOnRemoteAppSession", scheduler.Submitted[0]);
        Assert.DoesNotContain("DisallowStartOnRemoteAppSession", scheduler.Submitted[1]);
        Assert.Contains("UseUnifiedSchedulingEngine", scheduler.Submitted[1]);
    }

    [Fact]
    public void TwoRejectionsInSequenceAreBothRecovered()
    {
        var scheduler = new FakeScheduler(
            (1, Rejection),
            (1, "ERROR: The task XML contains an unexpected node: (5,5) UseUnifiedSchedulingEngine"),
            (0, string.Empty));

        ScheduledTaskManager.RegistrationOutcome outcome = ScheduledTaskManager.Register(WithNode, scheduler.Submit);

        Assert.True(outcome.Succeeded);
        Assert.Equal(["DisallowStartOnRemoteAppSession", "UseUnifiedSchedulingEngine"], outcome.RemovedNodes);
        Assert.Equal(3, scheduler.Submitted.Count);
    }

    [Fact]
    public void RetriesAreBoundedAndTheLastErrorSurvives()
    {
        // Rejects every document it is given, naming a different removable node each time so the
        // loop keeps finding something to strip. It still stops.
        var scheduler = new FakeScheduler(
            (1, Rejection),
            (1, "ERROR: The task XML contains an unexpected node: (5,5) UseUnifiedSchedulingEngine"),
            (1, "ERROR: The task XML contains an unexpected node: (4,5) DisallowStartOnRemoteAppSession"),
            (1, Rejection),
            (1, Rejection));

        ScheduledTaskManager.RegistrationOutcome outcome = ScheduledTaskManager.Register(WithNode, scheduler.Submit);

        Assert.False(outcome.Succeeded);
        Assert.Equal(3, scheduler.Submitted.Count);
        Assert.Contains("unexpected node", outcome.Output);
    }

    [Fact]
    public void TheAttemptCapIsSmallAndDeliberate()
    {
        // The literal, not the constant compared to itself. The allowlist already bounds the loop —
        // it can only remove nodes that are on it, and only once each — so this cap is a backstop.
        // A backstop that quietly grew would be no backstop, and nothing else would notice.
        Assert.Equal(3, TaskXmlCompatibility.MaximumAttempts);
    }

    [Fact]
    public void ARejectionNamingANonAllowlistedNodeFailsClosed()
    {
        var scheduler = new FakeScheduler((1, "ERROR: The task XML contains an unexpected node: (9,5) RunLevel"));

        ScheduledTaskManager.RegistrationOutcome outcome = ScheduledTaskManager.Register(WithNode, scheduler.Submit);

        Assert.False(outcome.Succeeded);
        Assert.Empty(outcome.RemovedNodes);
        Assert.Single(scheduler.Submitted);
        Assert.Contains("RunLevel", outcome.Output);
    }

    [Fact]
    public void AFailureThatIsNotAParserRejectionIsNotRetried()
    {
        var scheduler = new FakeScheduler((1, "ERROR: Access is denied."));

        ScheduledTaskManager.RegistrationOutcome outcome = ScheduledTaskManager.Register(WithNode, scheduler.Submit);

        Assert.False(outcome.Succeeded);
        Assert.Single(scheduler.Submitted);
        Assert.Equal("ERROR: Access is denied.", outcome.Output);
    }

    [Fact]
    public void TheSameNodeIsNeverRemovedTwice()
    {
        // If removal ever reported success without changing the document, this would loop until the
        // attempt cap instead of stopping. It stops at two submissions.
        var scheduler = new FakeScheduler((1, Rejection), (1, Rejection));

        ScheduledTaskManager.RegistrationOutcome outcome = ScheduledTaskManager.Register(WithNode, scheduler.Submit);

        Assert.False(outcome.Succeeded);
        Assert.Equal(2, scheduler.Submitted.Count);
        Assert.Equal(["DisallowStartOnRemoteAppSession"], outcome.RemovedNodes);
    }
}

public sealed class HelpContentTests
{
    private static string AllText =>
        string.Join("\n", HelpTopics.Operational.Select(topic => topic.Title + "\n" + string.Join("\n", topic.Paragraphs)));

    [Theory]
    [InlineData("Automatic")]
    [InlineData("Physical")]
    [InlineData("Unicode")]
    [InlineData("iDRAC")]
    [InlineData("lowercase")]
    [InlineData("Chrome")]
    [InlineData("shortcut")]
    [InlineData("clipboard history")]
    [InlineData("elevated")]
    [InlineData("integrity")]
    [InlineData("Windows Terminal")]
    [InlineData("conhost.exe")]
    public void EveryRequiredTopicIsCovered(string required)
    {
        Assert.Contains(required, AllText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheRecommendedModeIsNamed()
    {
        Assert.Contains("Automatic is the recommended mode", AllText);
    }

    [Fact]
    public void WindowsTerminalIsDescribedAsAnInteractionNotADefect()
    {
        Assert.Contains("not a fault found in TypeyTypey", AllText);
    }

    [Fact]
    public void StartupClipboardBehaviourIsDocumentedAsWorkingButAbsentFromHistory()
    {
        Assert.Contains("can still be typed", AllText);
        Assert.Contains("does not appear in the clipboard history picker", AllText);
    }

    [Fact]
    public void EveryTopicHasAHeadingAndContent()
    {
        Assert.NotEmpty(HelpTopics.Operational);
        Assert.All(HelpTopics.Operational, topic =>
        {
            Assert.False(string.IsNullOrWhiteSpace(topic.Title));
            Assert.NotEmpty(topic.Paragraphs);
            Assert.All(topic.Paragraphs, paragraph => Assert.False(string.IsNullOrWhiteSpace(paragraph)));
        });
    }
}
