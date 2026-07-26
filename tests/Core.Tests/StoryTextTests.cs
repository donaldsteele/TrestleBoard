using TrestleBoard.Core.Commands;
using TrestleBoard.Core.Model;
using Xunit;

namespace TrestleBoard.Core.Tests;

public sealed class StoryTextTests
{
    private static StoryParagraph Para(params (string Text, string? Style)[] runs) => new()
    {
        ParagraphStyleRef = "body",
        Runs = [.. runs.Select(r => new StoryRun { Text = r.Text, CharacterStyleRef = r.Style })],
    };

    [Fact]
    public void InsertIntoEmptyParagraphCreatesDefaultRun()
    {
        StoryParagraph para = Para();
        StoryText.Insert(para, 0, "Hello");
        StoryRun run = Assert.Single(para.Runs);
        Assert.Equal("Hello", run.Text);
        Assert.Null(run.CharacterStyleRef);
    }

    [Fact]
    public void InsertAtRunBoundaryAdoptsLeftStyle()
    {
        StoryParagraph para = Para(("Bold. ", "bold"), ("Plain.", null));
        StoryText.Insert(para, 6, "X");
        Assert.Equal(["Bold. X", "Plain."], para.Runs.Select(r => r.Text));
    }

    [Fact]
    public void InsertMidRunKeepsRunCount()
    {
        StoryParagraph para = Para(("Hello world", null));
        StoryText.Insert(para, 5, ",");
        Assert.Equal("Hello, world", StoryText.GetText(para));
        Assert.Single(para.Runs);
    }

    [Fact]
    public void DeleteMidRunShrinksRun()
    {
        StoryParagraph para = Para(("Hello cruel world", null));
        StoryText.Delete(para, 5, 6);
        Assert.Equal("Hello world", StoryText.GetText(para));
    }

    [Fact]
    public void DeleteAcrossRunsRemovesEmptyAndMergesSameStyle()
    {
        StoryParagraph para = Para(("AAA", null), ("BBB", "bold"), ("CCC", null));
        StoryText.Delete(para, 2, 5); // "AA|A BBB C|CC" → "AA" + "CC", same style → one run
        Assert.Equal("AACC", StoryText.GetText(para));
        Assert.Single(para.Runs);
        Assert.Null(para.Runs[0].CharacterStyleRef);
    }

    [Fact]
    public void DeleteWholeRunPreservesDifferentStyledNeighbors()
    {
        StoryParagraph para = Para(("AAA", "a"), ("BBB", "b"), ("CCC", "c"));
        StoryText.Delete(para, 3, 3);
        Assert.Equal(["AAA", "CCC"], para.Runs.Select(r => r.Text));
        Assert.Equal(["a", "c"], para.Runs.Select(r => r.CharacterStyleRef));
    }

    [Fact]
    public void DeleteEverythingLeavesEmptyRunList()
    {
        StoryParagraph para = Para(("AAA", null), ("BBB", "bold"));
        StoryText.Delete(para, 0, 6);
        Assert.Empty(para.Runs);
        Assert.Equal(0, para.Length);
    }

    [Fact]
    public void InsertOffsetPastEndThrows()
    {
        StoryParagraph para = Para(("AB", null));
        Assert.Throws<ArgumentOutOfRangeException>(() => StoryText.Insert(para, 3, "X"));
    }

    [Fact]
    public void DeleteRangePastEndThrows()
    {
        StoryParagraph para = Para(("AB", null));
        Assert.Throws<ArgumentOutOfRangeException>(() => StoryText.Delete(para, 1, 5));
    }

    [Fact]
    public void LocateBoundaryPrefersLeftUnlessPreferRight()
    {
        StoryParagraph para = Para(("AAA", "a"), ("BBB", "b"));
        Assert.Equal((0, 3), StoryText.Locate(para, 3));
        Assert.Equal((1, 0), StoryText.Locate(para, 3, preferRight: true));
    }
}
