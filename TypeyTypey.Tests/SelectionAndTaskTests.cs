using Xunit;

namespace TypeyTypey.Tests;

public sealed class AdminTaskCommandTests
{
    [Fact]
    public void BareFlag_RequestsTheSignInTask()
    {
        Assert.True(CommandLine.TryParse(["--admintask"], out AppCommand command, out _, out AdminTaskMode mode));
        Assert.Equal(AdminTaskMode.Logon, mode);
        // The task is administered by this process; nothing is relayed to a running instance.
        Assert.Equal(AppCommand.None, command);
    }

    // AdminTaskMode is internal, so the expected value stays in the body rather than the theory data.
    [Theory]
    [InlineData("system")]
    [InlineData("SYSTEM")]
    public void SystemQualifier_RequestsTheBootTask(string qualifier)
    {
        Assert.True(CommandLine.TryParse(["--admintask", qualifier], out _, out _, out AdminTaskMode mode));
        Assert.Equal(AdminTaskMode.System, mode);
    }

    [Theory]
    [InlineData("off")]
    [InlineData("remove")]
    public void RemovalQualifier_RequestsRemoval(string qualifier)
    {
        Assert.True(CommandLine.TryParse(["--admintask", qualifier], out _, out _, out AdminTaskMode mode));
        Assert.Equal(AdminTaskMode.Remove, mode);
    }

    [Fact]
    public void UnknownQualifierOrExtraArgument_IsRejected()
    {
        Assert.False(CommandLine.TryParse(["--admintask", "sometimes"], out _, out _, out _));
        Assert.False(CommandLine.TryParse(["--admintask", "system", "off"], out _, out _, out _));
    }

    [Fact]
    public void ElevatedRelaunchReproducesTheSameMode()
    {
        foreach (AdminTaskMode mode in new[] { AdminTaskMode.Logon, AdminTaskMode.System, AdminTaskMode.Remove })
        {
            Assert.True(CommandLine.TryParse(CommandLine.Arguments(mode), out _, out _, out AdminTaskMode parsed));
            Assert.Equal(mode, parsed);
        }
    }

    [Fact]
    public void ExistingCommandsAreUnaffected()
    {
        Assert.True(CommandLine.TryParse(["--history"], out AppCommand command, out _, out AdminTaskMode mode));
        Assert.Equal(AppCommand.History, command);
        Assert.Equal(AdminTaskMode.None, mode);
    }
}

public sealed class ScheduledTaskXmlTests
{
    private const string Sid = "S-1-5-21-1111111111-2222222222-3333333333-1001";

    [Fact]
    public void SignInTask_RunsAsTheUserWithTheHighestAvailableRunLevel()
    {
        string xml = ScheduledTaskManager.BuildTaskXml(AdminTaskMode.Logon, @"C:\Apps\TypeyTypey.exe", Sid);

        Assert.Contains("<LogonTrigger>", xml);
        Assert.DoesNotContain("<BootTrigger>", xml);
        Assert.Contains("<LogonType>InteractiveToken</LogonType>", xml);
        Assert.Contains("<RunLevel>HighestAvailable</RunLevel>", xml);
        Assert.Contains(Sid, xml);
        Assert.Contains(@"<Command>C:\Apps\TypeyTypey.exe</Command>", xml);
    }

    [Fact]
    public void SystemTask_RunsAtBootAsLocalSystem()
    {
        string xml = ScheduledTaskManager.BuildTaskXml(AdminTaskMode.System, @"C:\Apps\TypeyTypey.exe", Sid);

        Assert.Contains("<BootTrigger>", xml);
        Assert.DoesNotContain("<LogonTrigger>", xml);
        Assert.Contains("<UserId>S-1-5-18</UserId>", xml);
        Assert.Contains("<LogonType>ServiceAccount</LogonType>", xml);
        Assert.DoesNotContain(Sid, xml);
    }

    [Theory]
    [InlineData("<RegistrationInfo>", "<URI>", "<Description>")]
    [InlineData("<Enabled>true</Enabled>", "<UserId>", "<Delay>")]
    [InlineData("<Triggers>", "<Principals>", "<Actions ")]
    public void ElementsFollowTheOrderTheSchemaRequires(string first, string second, string third)
    {
        // Task Scheduler validates against its XSD and rejects a definition whose sequence is out of
        // order, with no indication of which element is at fault. registrationInfoType puts URI
        // before Description, and logonTriggerType extends the base trigger with UserId then Delay.
        string xml = ScheduledTaskManager.BuildTaskXml(AdminTaskMode.Logon, @"C:\Apps\TypeyTypey.exe", Sid);

        Assert.True(xml.IndexOf(first, StringComparison.Ordinal) < xml.IndexOf(second, StringComparison.Ordinal)
            && xml.IndexOf(second, StringComparison.Ordinal) < xml.IndexOf(third, StringComparison.Ordinal),
            $"expected {first} before {second} before {third}");
    }

    [Fact]
    public void ExecutablePathIsXmlEscaped()
    {
        string xml = ScheduledTaskManager.BuildTaskXml(AdminTaskMode.Logon, @"C:\A & B\<Typey>.exe", Sid);

        Assert.Contains(@"C:\A &amp; B\&lt;Typey&gt;.exe", xml);
        Assert.DoesNotContain("<Typey>", xml);
    }

    [Fact]
    public void DeclaredEncodingMatchesTheFileSchtasksWillBeGiven()
    {
        // schtasks rejects a task definition that is not UTF-16, so the declaration and the
        // Encoding.Unicode write in Install must agree.
        Assert.Contains(@"encoding=""UTF-16""", ScheduledTaskManager.BuildTaskXml(AdminTaskMode.Logon, "TypeyTypey.exe", Sid));
    }
}

public sealed class PendingSelectionTests
{
    private const string Clipboard = "clipboard";

    [Fact]
    public void NothingArmed_TypesTheLiveClipboard()
    {
        var pending = new PendingSelection();

        Assert.False(pending.IsArmed);
        Assert.Equal(Clipboard, pending.Resolve(clipboardRead: true, Clipboard));
    }

    [Fact]
    public void ArmedEntry_ReplacesTheClipboardItWasArmedAgainst()
    {
        var pending = new PendingSelection();

        Assert.True(pending.Set("picked", clipboardRead: true, Clipboard));
        Assert.Equal("picked", pending.Resolve(clipboardRead: true, Clipboard));
    }

    [Fact]
    public void ArmedEntry_SurvivesRepeatedTyping()
    {
        var pending = new PendingSelection();
        pending.Set("picked", clipboardRead: true, Clipboard);

        Assert.Equal("picked", pending.Resolve(clipboardRead: true, Clipboard));
        Assert.Equal("picked", pending.Resolve(clipboardRead: true, Clipboard));
    }

    [Fact]
    public void CopyingSinceArming_SupersedesTheEntry()
    {
        // The case that matters with clipboard monitoring paused: nothing tells the application a
        // copy happened, so the comparison against the clipboard at arming time has to catch it.
        var pending = new PendingSelection();
        pending.Set("picked", clipboardRead: true, Clipboard);

        Assert.Equal("copied in the browser", pending.Resolve(clipboardRead: true, "copied in the browser"));
        Assert.False(pending.IsArmed);
        // And stays superseded: the entry is not resurrected by the clipboard changing again.
        Assert.Equal(Clipboard, pending.Resolve(clipboardRead: true, Clipboard));
    }

    [Fact]
    public void ClearingTheClipboardSinceArming_AlsoSupersedesTheEntry()
    {
        var pending = new PendingSelection();
        pending.Set("picked", clipboardRead: true, Clipboard);

        Assert.Null(pending.Resolve(clipboardRead: true, null));
        Assert.False(pending.IsArmed);
    }

    [Fact]
    public void UnreadableClipboard_KeepsTheArmedEntry()
    {
        // A clipboard another application is holding open proves nothing about what the user copied.
        var pending = new PendingSelection();
        pending.Set("picked", clipboardRead: true, Clipboard);

        Assert.Equal("picked", pending.Resolve(clipboardRead: false, null));
        Assert.True(pending.IsArmed);
    }

    [Fact]
    public void EntryArmedAgainstAnUnreadableClipboard_IsNeverSuperseded()
    {
        // No snapshot was taken, so there is nothing to compare against; keeping the entry is the
        // conservative choice, and the history-change and explicit paths still clear it.
        var pending = new PendingSelection();
        pending.Set("picked", clipboardRead: false, null);

        Assert.Equal("picked", pending.Resolve(clipboardRead: true, "copied in the browser"));
        Assert.True(pending.IsArmed);
    }

    [Fact]
    public void EmptyOrWhitespaceSelection_ArmsNothingAndClearsWhatWasArmed()
    {
        var pending = new PendingSelection();
        pending.Set("picked", clipboardRead: true, Clipboard);

        Assert.False(pending.Set("  \r\n ", clipboardRead: true, Clipboard));
        Assert.False(pending.IsArmed);
        Assert.Equal(Clipboard, pending.Resolve(clipboardRead: true, Clipboard));
    }

    [Fact]
    public void ClearedSelection_FallsBackToTheClipboard()
    {
        var pending = new PendingSelection();
        pending.Set("picked", clipboardRead: true, Clipboard);
        pending.Clear();

        Assert.Equal(Clipboard, pending.Resolve(clipboardRead: true, Clipboard));
    }

    [Fact]
    public void SelectionIsDroppedOnceItLeavesTheHistory()
    {
        var pending = new PendingSelection();
        pending.Set("picked", clipboardRead: true, Clipboard);

        Assert.False(pending.ClearIfMissingFrom(["other", "picked"]));
        Assert.True(pending.IsArmed);

        Assert.True(pending.ClearIfMissingFrom(["other"]));
        Assert.False(pending.IsArmed);
        Assert.False(pending.ClearIfMissingFrom([]));
    }

    [Fact]
    public void Confirmation_ReportsTheLengthAndHotkeyButNeverTheText()
    {
        var pending = new PendingSelection();
        pending.Set("hunter2", clipboardRead: true, Clipboard);

        string message = PendingSelection.Describe(pending.Length, "Ctrl + Alt + V");

        Assert.Equal(7, pending.Length);
        Assert.Contains("7 characters", message);
        Assert.Contains("Ctrl + Alt + V", message);
        Assert.DoesNotContain("hunter2", message);
    }

    [Fact]
    public void Confirmation_UsesTheSingularForOneCharacter()
    {
        Assert.Contains("1 character.", PendingSelection.Describe(1, "Ctrl + Alt + V"));
    }
}

public sealed class HelpCommandTests
{
    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("/?")]
    public void EveryHelpFlag_Parses(string flag)
    {
        Assert.True(CommandLine.TryParse([flag], out AppCommand command, out _, out AdminTaskMode mode));
        Assert.Equal(AppCommand.Help, command);
        Assert.Equal(AdminTaskMode.None, mode);
    }

    [Fact]
    public void UsageIsDerivedFromTheDocumentedOptions()
    {
        foreach (CommandLineOption option in CommandLine.Options)
            Assert.Contains(option.Flag, CommandLine.Usage);
    }

    [Fact]
    public void EveryDocumentedOption_HasASummary()
    {
        foreach (CommandLineOption option in CommandLine.Options)
        {
            Assert.StartsWith("--", option.Flag);
            Assert.False(string.IsNullOrWhiteSpace(option.Summary), $"{option.Flag} has no summary");
        }
    }

    /// <summary>
    /// Ties the help window's list to the parser. Without this an option could be documented and
    /// still be rejected on the command line, which is the failure a user would report as a bug.
    /// </summary>
    [Fact]
    public void EveryDocumentedOption_IsAcceptedByTheParser()
    {
        foreach (CommandLineOption option in CommandLine.Options)
        {
            string[] arguments = option.Flag.Split(' ');

            Assert.True(CommandLine.TryParse(arguments, out AppCommand command, out _, out AdminTaskMode mode),
                $"{option.Flag} is listed in help but does not parse");
            Assert.True(command != AppCommand.None || mode != AdminTaskMode.None,
                $"{option.Flag} parses but resolves to no action");
        }
    }
}
