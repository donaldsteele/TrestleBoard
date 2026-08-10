using System;
using System.Collections.Generic;
using System.Linq;
using TrestleBoard.Core.Model;
using TrestleBoard.Core.Templates;
using TrestleBoard.Editing.Review;
using Xunit;

namespace TrestleBoard.Editing.Tests;

/// <summary>
/// M51's checklist, driven without a window (PLAN.md §11 M51). <see cref="ReviewChecklist"/> takes a
/// document and two sets of block ids and returns records, so everything the review can ever say
/// about a newsletter can be asserted here, in milliseconds, against a document built in six lines.
///
/// <para>The fixture below is PLAN.md's acceptance criterion made literal: <i>a fixture document
/// seeded with one of each defect surfaces all of them</i>.</para>
/// </summary>
public sealed class ReviewChecklistTests
{
    [Fact]
    public void AFixtureWithOneOfEachDefectSurfacesAllOfThem()
    {
        Document document = OneOfEachDefect();

        IReadOnlyList<ReviewFinding> findings = ReviewChecklist.Build(
            document,
            oversetTailBlockIds: ["frame-overflowing"],
            emptyPictureBlockIds: ["picture-empty"]);

        ReviewFindingKind[] kinds = [.. findings.Select(f => f.Kind).Distinct()];

        Assert.Contains(ReviewFindingKind.UnwrittenWords, kinds);
        Assert.Contains(ReviewFindingKind.WritingThatRanOut, kinds);
        Assert.Contains(ReviewFindingKind.EmptyPictureFrame, kinds);
        Assert.Contains(ReviewFindingKind.PictureWithoutCaption, kinds);
        Assert.Contains(ReviewFindingKind.PictureWithoutDescription, kinds);
        Assert.Contains(ReviewFindingKind.DateFromAnotherMonth, kinds);
        Assert.Contains(ReviewFindingKind.LookAtThePage, kinds);
    }

    [Fact]
    public void ANewsletterWithNothingWrongStillGetsALookAtEveryPage()
    {
        Document document = Clean();

        IReadOnlyList<ReviewFinding> findings = ReviewChecklist.Build(document);

        Assert.All(findings, f => Assert.Equal(ReviewFindingKind.LookAtThePage, f.Kind));
        Assert.Equal(document.Pages.Count, findings.Count);
        Assert.Equal([1, 2], findings.Select(f => f.PageNumber));
    }

    /// <summary>
    /// The defect this milestone found on the way in: only carry-forward's article prompt was ever
    /// matched, so a newsletter started from a template — whose cover holds a different sentence —
    /// was reported as fully written.
    /// </summary>
    [Theory]
    [InlineData(PlaceholderPrompts.CoverEssay)]
    [InlineData(PlaceholderPrompts.News)]
    [InlineData(PlaceholderPrompts.MoreNews)]
    [InlineData(PlaceholderPrompts.AboutThePhoto)]
    [InlineData(PlaceholderPrompts.ClosingNote)]
    [InlineData(PlaceholderPrompts.Article)]
    public void EveryPromptATemplateCanLeaveBehindIsFound(string prompt)
    {
        Document document = Clean();
        document.Stories[0].Paragraphs[0].Runs[0].Text = prompt;

        IReadOnlyList<ReviewFinding> findings = ReviewChecklist.Build(document);

        ReviewFinding finding = Assert.Single(findings, f => f.Kind == ReviewFindingKind.UnwrittenWords);
        Assert.Contains(prompt, finding.Heading, StringComparison.Ordinal);
    }

    /// <summary>
    /// Weaker than "the story is exactly the prompt", on purpose: somebody who wrote a paragraph
    /// above the prompt and left it below has still left it, and it will still print.
    /// </summary>
    [Fact]
    public void APromptLeftUnderneathSomebodysWritingIsStillFound()
    {
        Document document = Clean();
        document.Stories[0].Paragraphs =
        [
            Para("Brethren, the summer has been a busy one."),
            Para(PlaceholderPrompts.Article),
        ];

        IReadOnlyList<ReviewFinding> findings = ReviewChecklist.Build(document);

        Assert.Single(findings, f => f.Kind == ReviewFindingKind.UnwrittenWords);
    }

    [Fact]
    public void AStoryThatRunsThroughThreeFramesIsAskedAboutOnce()
    {
        Document document = Clean();
        document.Stories[0].Paragraphs[0].Runs[0].Text = PlaceholderPrompts.Article;
        document.Pages[1].Blocks.Add(new TextBlock { Id = "frame-b", StoryRef = "story-1" });
        document.Pages[1].Blocks.Add(new TextBlock { Id = "frame-c", StoryRef = "story-1" });

        IReadOnlyList<ReviewFinding> findings = ReviewChecklist.Build(document);

        Assert.Single(findings, f => f.Kind == ReviewFindingKind.UnwrittenWords);
    }

    // ---- the date heuristic -----------------------------------------------------------------

    [Fact]
    public void AMonthBeforeThisIssuesIsAskedAboutRatherThanAsserted()
    {
        Document document = Clean(issueMonth: 9);
        document.Stories[0].Paragraphs[0].Runs[0].Text = "The August picnic was a fine evening.";

        ReviewFinding finding = Assert.Single(
            ReviewChecklist.Build(document),
            f => f.Kind == ReviewFindingKind.DateFromAnotherMonth);

        Assert.EndsWith("?", finding.Question, StringComparison.Ordinal);
        Assert.Contains("perfectly fine", finding.Question, StringComparison.Ordinal);
        Assert.DoesNotContain("wrong", finding.Question, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A September newsletter announcing the October picnic is doing its job. Flagging months ahead
    /// would make the check fire on almost every issue, and a checklist that cries wolf is one the
    /// committee stops running.
    /// </summary>
    [Fact]
    public void AMonthAheadOfThisIssuesIsNotAskedAbout()
    {
        Document document = Clean(issueMonth: 9);
        document.Stories[0].Paragraphs[0].Runs[0].Text = "The October picnic is on the 14th.";

        Assert.DoesNotContain(
            ReviewChecklist.Build(document),
            f => f.Kind == ReviewFindingKind.DateFromAnotherMonth);
    }

    [Fact]
    public void JanuaryLooksBackAtDecember()
    {
        Document document = Clean(issueMonth: 1);
        document.Stories[0].Paragraphs[0].Runs[0].Text = "Our December table lodge was well attended.";

        Assert.Single(
            ReviewChecklist.Build(document),
            f => f.Kind == ReviewFindingKind.DateFromAnotherMonth);
    }

    /// <summary>
    /// "May" inside "Maybe" and "March" inside "marching" are the two that would fire every month.
    /// </summary>
    [Theory]
    [InlineData(6, "Maybe the brethren will bring a guest.")]
    [InlineData(4, "The marching order for the procession is posted.")]
    public void AMonthNameInsideAnotherWordIsNotAMonth(int issueMonth, string prose)
    {
        Document document = Clean(issueMonth: issueMonth);
        document.Stories[0].Paragraphs[0].Runs[0].Text = prose;

        Assert.DoesNotContain(
            ReviewChecklist.Build(document),
            f => f.Kind == ReviewFindingKind.DateFromAnotherMonth);
    }

    // ---- pictures ---------------------------------------------------------------------------

    /// <summary>
    /// Three screens about one empty box would be three ways of saying the same thing. The caption
    /// and the description are questions about a picture, and there is no picture yet.
    /// </summary>
    [Fact]
    public void AnEmptyPictureBoxIsOneQuestionRatherThanThree()
    {
        Document document = Clean();
        document.Pages[0].Blocks.Add(new ImageFrame { Id = "picture-empty", AssetRef = "missing.png" });

        IReadOnlyList<ReviewFinding> findings = ReviewChecklist.Build(
            document,
            emptyPictureBlockIds: ["picture-empty"]);

        Assert.Single(findings, f => f.Kind == ReviewFindingKind.EmptyPictureFrame);
        Assert.DoesNotContain(findings, f => f.Kind == ReviewFindingKind.PictureWithoutCaption);
        Assert.DoesNotContain(findings, f => f.Kind == ReviewFindingKind.PictureWithoutDescription);
    }

    /// <summary>
    /// The six-page template ships its prompt IN the description field, so the obvious
    /// null-or-whitespace test calls every untouched photo frame described.
    /// </summary>
    [Fact]
    public void ThePromptTheTemplatePutInTheDescriptionIsNotADescription()
    {
        Document document = Clean();
        document.Pages[0].Blocks.Add(new ImageFrame
        {
            Id = "picture-1",
            AssetRef = "photo.jpg",
            Caption = "The degree team, September 2026.",
            AltText = PlaceholderPrompts.PhotoDescription,
        });

        Assert.Single(
            ReviewChecklist.Build(document),
            f => f.Kind == ReviewFindingKind.PictureWithoutDescription);
    }

    [Fact]
    public void APictureWithBothWordsIsAskedNothing()
    {
        Document document = Clean();
        document.Pages[0].Blocks.Add(new ImageFrame
        {
            Id = "picture-1",
            AssetRef = "photo.jpg",
            Caption = "The degree team.",
            AltText = "Five brethren standing before the altar.",
        });

        Assert.DoesNotContain(
            ReviewChecklist.Build(document),
            f => f.Kind is ReviewFindingKind.PictureWithoutCaption
                or ReviewFindingKind.PictureWithoutDescription);
    }

    /// <summary>
    /// M74 (f): a drawing is a picture as far as this list is concerned.
    ///
    /// <para>The walk reached picture findings through <c>case ImageFrame</c>, written when a
    /// photograph was the only kind of picture there was. From M72 an emblem is a
    /// <see cref="VectorBlock"/> — it carries a caption and a description like any other frame, and
    /// the review never once looked at either. A committee member who blanked an emblem's
    /// description, or never gave it one, was told the newsletter was ready; the one screen in the
    /// app whose whole job is to find a picture nobody can see could not see this one.</para>
    /// </summary>
    [Fact]
    public void ADrawingWithNoDescriptionIsAskedAboutJustAsAPhotographIs()
    {
        Document document = Clean();
        document.Pages[0].Blocks.Add(new VectorBlock
        {
            Id = "drawing-1",
            ViewBoxWidth = 100,
            ViewBoxHeight = 100,
            Parts = [new VectorPart { PathData = "M10,10 L90,90", StrokeWidth = 2 }],
            Caption = "The square and compasses.",
            AltText = "   ",
        });

        ReviewFinding finding = Assert.Single(
            ReviewChecklist.Build(document),
            f => f.Kind == ReviewFindingKind.PictureWithoutDescription);

        Assert.Equal("drawing-1", finding.BlockId);
    }

    // ---- shape of the whole list --------------------------------------------------------------

    [Fact]
    public void TheQuestionsComeInTheOrderTheReaderMeetsThemAndTheLookThroughIsLast()
    {
        Document document = OneOfEachDefect();

        IReadOnlyList<ReviewFinding> findings = ReviewChecklist.Build(
            document,
            ["frame-overflowing"],
            ["picture-empty"]);

        int firstLook = findings.ToList().FindIndex(f => f.Kind == ReviewFindingKind.LookAtThePage);
        Assert.All(
            findings.Take(firstLook),
            f => Assert.NotEqual(ReviewFindingKind.LookAtThePage, f.Kind));
        Assert.All(
            findings.Skip(firstLook),
            f => Assert.Equal(ReviewFindingKind.LookAtThePage, f.Kind));

        // Page order among the questions, so the review walks the newsletter rather than the model.
        int[] pages = [.. findings.Take(firstLook).Select(f => f.PageNumber)];
        Assert.Equal([.. pages.OrderBy(p => p)], pages);
    }

    /// <summary>
    /// PLAN.md §11 M51: the stale-date check is a heuristic and must be phrased as a question. The
    /// rest are near enough certain, but the review's whole manner is asking — an app that tells a
    /// volunteer their newsletter is wrong is an app they stop opening.
    /// </summary>
    [Fact]
    public void EveryQuestionIsAQuestion()
    {
        IReadOnlyList<ReviewFinding> findings = ReviewChecklist.Build(
            OneOfEachDefect(),
            ["frame-overflowing"],
            ["picture-empty"]);

        Assert.All(findings, f => Assert.EndsWith("?", f.Question.TrimEnd(), StringComparison.Ordinal));
        Assert.All(findings, f => Assert.False(string.IsNullOrWhiteSpace(f.Heading)));
    }

    [Fact]
    public void NothingTheChecklistDoesTouchesTheNewsletter()
    {
        Document document = OneOfEachDefect();
        string before = System.Text.Json.JsonSerializer.Serialize(document);

        ReviewChecklist.Build(document, ["frame-overflowing"], ["picture-empty"]);

        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(document));
    }

    // ---- fixtures -----------------------------------------------------------------------------

    /// <summary>
    /// Two pages carrying one of everything M51 looks for. Fictional throughout (PLAN.md §0 rule 2).
    /// </summary>
    private static Document OneOfEachDefect()
    {
        Document document = Clean(issueMonth: 9);
        document.Stories[0].Paragraphs[0].Runs[0].Text = PlaceholderPrompts.CoverEssay;

        document.Stories.Add(new Story
        {
            Id = "story-2",
            Paragraphs = [Para("The August picnic was a fine evening, and the lodge thanks the stewards.")],
        });
        document.Pages[1].Blocks.Add(new TextBlock { Id = "frame-overflowing", StoryRef = "story-2" });

        document.Pages[0].Blocks.Add(new ImageFrame { Id = "picture-empty", AssetRef = "missing.png" });
        document.Pages[1].Blocks.Add(new ImageFrame
        {
            Id = "picture-bare",
            AssetRef = "photo.jpg",
            Caption = null,
            AltText = "",
        });

        return document;
    }

    private static Document Clean(int issueMonth = 9)
    {
        var document = new Document
        {
            Metadata = { IssueMonth = issueMonth, IssueYear = 2026, Title = "Trestle Board" },
        };
        document.PageMasters.Add(new PageMaster { Id = "master" });
        document.Stories.Add(new Story
        {
            Id = "story-1",
            Paragraphs = [Para("Brethren, the lodge meets on the first Tuesday.")],
        });

        var page1 = new Page { Id = "page-1", MasterRef = "master" };
        page1.Blocks.Add(new TextBlock { Id = "frame-1", StoryRef = "story-1" });
        document.Pages.Add(page1);
        document.Pages.Add(new Page { Id = "page-2", MasterRef = "master" });

        return document;
    }

    private static StoryParagraph Para(string text) => new()
    {
        ParagraphStyleRef = "body",
        Runs = [new StoryRun { Text = text }],
    };
}
