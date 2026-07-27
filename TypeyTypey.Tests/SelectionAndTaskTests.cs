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
