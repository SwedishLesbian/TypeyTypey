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
    [Fact]
    public void NothingArmed_TypesTheLiveClipboard()
    {
        var pending = new PendingSelection();

        Assert.False(pending.IsArmed);
        Assert.Equal("clipboard", pending.Resolve(() => "clipboard"));
    }

    [Fact]
    public void ArmedEntry_ReplacesTheClipboardWithoutReadingIt()
    {
        var pending = new PendingSelection();
        bool clipboardRead = false;

        Assert.True(pending.Set("picked"));
        Assert.Equal("picked", pending.Resolve(() => { clipboardRead = true; return "clipboard"; }));
        Assert.False(clipboardRead);
    }

    [Fact]
    public void ArmedEntry_SurvivesRepeatedTyping()
    {
        var pending = new PendingSelection();
        pending.Set("picked");

        Assert.Equal("picked", pending.Resolve(() => "clipboard"));
        Assert.Equal("picked", pending.Resolve(() => "clipboard"));
    }

    [Fact]
    public void EmptyOrWhitespaceSelection_ArmsNothingAndClearsWhatWasArmed()
    {
        var pending = new PendingSelection();
        pending.Set("picked");

        Assert.False(pending.Set("  \r\n "));
        Assert.False(pending.IsArmed);
        Assert.Equal("clipboard", pending.Resolve(() => "clipboard"));
    }

    [Fact]
    public void ClearedSelection_FallsBackToTheClipboard()
    {
        var pending = new PendingSelection();
        pending.Set("picked");
        pending.Clear();

        Assert.Equal("clipboard", pending.Resolve(() => "clipboard"));
    }

    [Fact]
    public void SelectionIsDroppedOnceItLeavesTheHistory()
    {
        var pending = new PendingSelection();
        pending.Set("picked");

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
        pending.Set("hunter2");

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
