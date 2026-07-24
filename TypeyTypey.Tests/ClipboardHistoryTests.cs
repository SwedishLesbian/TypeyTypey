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
    [InlineData("--type", "Type")]
    [InlineData("--history", "History")]
    [InlineData("--settings", "Settings")]
    [InlineData("--pause", "Pause")]
    [InlineData("--resume", "Resume")]
    [InlineData("--clear-history", "ClearHistory")]
    [InlineData("--exit", "Exit")]
    public void Parse_RecognizesSupportedCommands(string argument, string expected)
    {
        Assert.True(CommandLine.TryParse([argument], out AppCommand command));
        Assert.Equal(Enum.Parse<AppCommand>(expected), command);
    }

    [Fact]
    public void Parse_RejectsUnsupportedOrMultipleCommands()
    {
        Assert.False(CommandLine.TryParse(["--nope"], out _));
        Assert.False(CommandLine.TryParse(["--type", "--history"], out _));
    }
}
