using Xunit;

namespace TypeyTypey.Tests;

public sealed class ClipboardHistoryTests
{
    [Fact]
    public void Add_PutsNewestEntryFirst()
    {
        var history = new ClipboardHistory(50);
        history.Add("first");
        history.Add("second");

        Assert.Equal(["second", "first"], history.Snapshot());
    }

    [Fact]
    public void Add_RepeatedEntryMovesItToTopWithoutDuplicates()
    {
        var history = new ClipboardHistory(50);
        history.Add("first");
        history.Add("second");
        history.Add("first");

        Assert.Equal(["first", "second"], history.Snapshot());
    }

    [Fact]
    public void Add_IgnoresEmptyAndWhitespaceOnlyText()
    {
        var history = new ClipboardHistory(50);
        history.Add("");
        history.Add(" \t\r\n ");

        Assert.Empty(history.Snapshot());
    }

    [Fact]
    public void SetMaximumEntries_TrimsOldestEntries()
    {
        var history = new ClipboardHistory(3);
        history.Add("one");
        history.Add("two");
        history.Add("three");
        history.SetMaximumEntries(2);

        Assert.Equal(["three", "two"], history.Snapshot());
    }

    [Fact]
    public void RemoveAndClear_ModifyOnlyRequestedHistoryItems()
    {
        var history = new ClipboardHistory(50);
        history.Add("one");
        history.Add("two");

        Assert.True(history.Remove("one"));
        Assert.Equal(["two"], history.Snapshot());
        history.Clear();
        Assert.Empty(history.Snapshot());
    }
}

public sealed class CommandLineTests
{
    [Theory]
    [InlineData("--type", AppCommand.Type)]
    [InlineData("--history", AppCommand.History)]
    [InlineData("--settings", AppCommand.Settings)]
    [InlineData("--pause", AppCommand.Pause)]
    [InlineData("--resume", AppCommand.Resume)]
    [InlineData("--clear-history", AppCommand.ClearHistory)]
    [InlineData("--exit", AppCommand.Exit)]
    public void Parse_RecognizesSupportedCommands(string argument, AppCommand expected)
    {
        Assert.True(CommandLine.TryParse([argument], out AppCommand command));
        Assert.Equal(expected, command);
    }

    [Fact]
    public void Parse_RejectsUnsupportedOrMultipleCommands()
    {
        Assert.False(CommandLine.TryParse(["--nope"], out _));
        Assert.False(CommandLine.TryParse(["--type", "--history"], out _));
    }
}
