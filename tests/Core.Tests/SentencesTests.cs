using System;
using System.Collections.Generic;
using System.Linq;
using TrestleBoard.Core.Model;
using TrestleBoard.Core.Text;
using Xunit;

namespace TrestleBoard.Core.Tests;

/// <summary>
/// M58's sentence segmentation (PLAN.md §11 M58).
///
/// <para>PLAN.md's acceptance says no test may depend on audio and that the tests cover sentence
/// segmentation and the silent walk-through. This is the first half: the unit both the reading and
/// the walk move by, checked against the sentences a trestle board actually contains.</para>
/// </summary>
public sealed class SentencesTests
{
    private static string[] Split(string text) => [.. Sentences.Split(text).Select(s => s.Text)];

    [Fact]
    public void APlainParagraphSplitsAtTheFullStops() =>
        Assert.Equal(
            ["The lodge meets on Tuesday.", "Supper is at six.", "All are welcome."],
            Split("The lodge meets on Tuesday. Supper is at six. All are welcome."));

    [Fact]
    public void QuestionsAndExclamationsEndSentencesToo() =>
        Assert.Equal(
            ["Will you be there?", "We hope so!"],
            Split("Will you be there? We hope so!"));

    [Fact]
    public void ARunOfPunctuationEndsOneSentenceRatherThanSeveral() =>
        Assert.Equal(["Really?!", "Yes."], Split("Really?! Yes."));

    [Fact]
    public void AParagraphWithNoFullStopIsStillSomethingToRead() =>
        Assert.Equal(["Notices for September"], Split("Notices for September"));

    [Fact]
    public void TrailingSpaceIsNotASentence() =>
        Assert.Equal(["All are welcome."], Split("All are welcome.   "));

    [Fact]
    public void AnEmptyParagraphIsNoSentences() => Assert.Empty(Split("   "));

    /// <summary>
    /// The one that matters in a trestle board: "Bro. Smith gave the charge" read as two sentences
    /// is a stumble in the middle of every issue.
    /// </summary>
    [Theory]
    [InlineData("Bro. Placeholder gave the charge.")]
    [InlineData("Dr. Placeholder spoke to the lodge.")]
    [InlineData("We met at St. John's hall.")]
    public void AnAbbreviationDoesNotEndASentence(string text) =>
        Assert.Single(Split(text));

    [Fact]
    public void AnInitialDoesNotEndASentence() =>
        Assert.Equal(["A. Placeholder gave the charge."], Split("A. Placeholder gave the charge."));

    [Fact]
    public void AClosingQuoteBelongsToTheSentenceItCloses() =>
        Assert.Equal(
            ["He said “welcome.”", "Then he sat down."],
            Split("He said “welcome.” Then he sat down."));

    [Fact]
    public void EverySentenceKnowsWhereItStarts()
    {
        (int Offset, string Text)[] found = [.. Sentences.Split("One. Two. Three.")];

        Assert.Equal([0, 5, 10], found.Select(f => f.Offset));
        Assert.All(found, f => Assert.Equal(
            f.Text, "One. Two. Three.".Substring(f.Offset, f.Text.Length)));
    }

    // ---- over a whole newsletter -----------------------------------------------------------------

    [Fact]
    public void TheWholeNewsletterComesInReadingOrder()
    {
        Document document = TwoParagraphs("The lodge meets on Tuesday. Supper is at six.", "All are welcome.");

        IReadOnlyList<Sentence> sentences = Sentences.In(document);

        Assert.Equal(
            ["The lodge meets on Tuesday.", "Supper is at six.", "All are welcome."],
            sentences.Select(s => s.Text));
        Assert.Equal([0, 0, 1], sentences.Select(s => s.ParagraphIndex));
    }

    [Fact]
    public void ANewsletterWithNoWritingHasNoSentences() =>
        Assert.Empty(Sentences.In(new Document()));

    private static Document TwoParagraphs(string first, string second)
    {
        var document = new Document();
        document.PageMasters.Add(new PageMaster { Id = "master" });
        document.Stories.Add(new Story
        {
            Id = "story-1",
            Paragraphs =
            [
                new StoryParagraph { ParagraphStyleRef = "body", Runs = [new StoryRun { Text = first }] },
                new StoryParagraph { ParagraphStyleRef = "body", Runs = [new StoryRun { Text = second }] },
            ],
        });

        var page = new Page { Id = "page-1", MasterRef = "master" };
        page.Blocks.Add(new TextBlock { Id = "frame-1", StoryRef = "story-1" });
        document.Pages.Add(page);
        return document;
    }
}
