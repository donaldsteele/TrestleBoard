using System;
using System.Collections.Generic;
using TrestleBoard.Core.Commands;
using TrestleBoard.Core.Model;
using TrestleBoard.Core.Text;
using TrestleBoard.Editing.Review;
using Xunit;

namespace TrestleBoard.Editing.Tests;

/// <summary>
/// M58's walk, with no audio anywhere near it (PLAN.md §11 M58).
///
/// <para>The silent walk-through is the baseline rather than the fallback, and this is what that
/// buys: the whole feature's behaviour — where it starts, what "back" means at the end, what it
/// says about its own progress — is decided here, in a class that has never heard of a
/// loudspeaker.</para>
/// </summary>
public sealed class ReadAloudSessionTests
{
    private static ReadAloudSession Three() => new(
    [
        new Sentence("story-1", 0, 0, "The lodge meets on Tuesday."),
        new Sentence("story-1", 0, 28, "Supper is at six."),
        new Sentence("story-1", 1, 0, "All are welcome."),
    ]);

    [Fact]
    public void ItStartsBeforeTheFirstSentenceSoNothingIsSaidUninvited()
    {
        ReadAloudSession session = Three();

        Assert.Null(session.Current);
        Assert.Equal(0, session.Position);
        Assert.False(session.Finished);
    }

    [Fact]
    public void EachStepHandsBackTheNextSentence()
    {
        ReadAloudSession session = Three();

        Assert.Equal("The lodge meets on Tuesday.", session.Next()!.Text);
        Assert.Equal("Supper is at six.", session.Next()!.Text);
        Assert.Equal("All are welcome.", session.Next()!.Text);
    }

    [Fact]
    public void PastTheLastOneItIsFinishedRatherThanStuck()
    {
        ReadAloudSession session = Three();
        for (int i = 0; i < 3; i++)
        {
            session.Next();
        }

        Assert.Null(session.Next());
        Assert.True(session.Finished);
        Assert.Contains("all 3", session.ProgressText, StringComparison.Ordinal);
    }

    /// <summary>
    /// "Back" at the end means "say that again", not "one past the end". Somebody who missed the
    /// last sentence presses it, and the app has to land on the sentence rather than on nothing.
    /// </summary>
    [Fact]
    public void BackAtTheEndLandsOnTheLastSentence()
    {
        ReadAloudSession session = Three();
        while (!session.Finished)
        {
            session.Next();
        }

        Assert.Equal("All are welcome.", session.Back()!.Text);
    }

    [Fact]
    public void BackAtTheStartStaysOnTheFirstSentence()
    {
        ReadAloudSession session = Three();
        session.Next();

        Assert.Equal("The lodge meets on Tuesday.", session.Back()!.Text);
        Assert.Equal("The lodge meets on Tuesday.", session.Back()!.Text);
    }

    [Fact]
    public void ItSaysWhereItIsInWordsAPersonCanFollow()
    {
        ReadAloudSession session = Three();
        session.Next();
        session.Next();

        Assert.Equal("Sentence 2 of 3", session.ProgressText);
    }

    [Fact]
    public void RestartingGoesBackToBeforeTheFirstSentence()
    {
        ReadAloudSession session = Three();
        session.Next();
        session.Next();

        session.Restart();

        Assert.Null(session.Current);
        Assert.Equal(0, session.Position);
    }

    /// <summary>
    /// A newsletter of pictures and lists has nothing to read, and the walk says so rather than
    /// presenting an empty screen with a Next button on it.
    /// </summary>
    [Fact]
    public void ANewsletterWithNoWritingSaysSo()
    {
        var session = new ReadAloudSession([]);

        Assert.True(session.IsEmpty);
        Assert.Null(session.Next());
        Assert.Contains("nothing to read", session.ProgressText, StringComparison.Ordinal);
    }

    [Fact]
    public void ASentenceKnowsWhereItIsSoThePageCanBeTurnedToIt()
    {
        ReadAloudSession session = Three();
        session.Next();
        session.Next();
        session.Next();

        Sentence third = session.Current!;
        Assert.Equal("story-1", third.StoryId);
        Assert.Equal(1, third.ParagraphIndex);
        Assert.Equal(0, third.Offset);
        Assert.Equal("All are welcome.".Length, third.Length);
    }

    /// <summary>
    /// PLAN.md §11 M74 (e). All five anchors <c>WhereThatSentenceIsNow</c> tries are inside the
    /// story being read, so deleting that story outright misses every one of them and the fallback
    /// clamp lands on whatever sentence happens to sit at the same ordinal — in a different
    /// article, about a different subject. <c>WhatToSay</c> then told the reader "this is that
    /// sentence as it reads now", which is a claim of continuity with writing that no longer
    /// exists; the honest branch was unreachable while anything at all was left to read.
    /// </summary>
    [Fact]
    public void DeletingTheWholeStoryBeingReadIsSaidPlainlyRatherThanPassedOffAsTheSameSentence()
    {
        var document = new Document();
        document.PageMasters.Add(new PageMaster { Id = "master-1" });

        var mine = new Story { Id = "story-mine" };
        mine.Paragraphs.Add(new StoryParagraph
        {
            ParagraphStyleRef = "body",
            Runs = [new StoryRun { Text = "The lodge meets on Tuesday. Supper is at six." }],
        });
        var other = new Story { Id = "story-other" };
        other.Paragraphs.Add(new StoryParagraph
        {
            ParagraphStyleRef = "body",
            Runs = [new StoryRun { Text = "Dues are payable in January. See the Secretary." }],
        });
        document.Stories.Add(mine);
        document.Stories.Add(other);

        var page = new Page { Id = "page-1", MasterRef = "master-1" };
        page.Blocks.Add(new TextBlock
        {
            Id = "text-mine",
            StoryRef = "story-mine",
            FrameRect = new RectPt(54f, 54f, 200f, 200f),
        });
        page.Blocks.Add(new TextBlock
        {
            Id = "text-other",
            StoryRef = "story-other",
            FrameRect = new RectPt(300f, 54f, 200f, 200f),
        });
        document.Pages.Add(page);

        var live = new DocumentSession(document);
        using ReadAloudSession session = ReadAloudSession.For(live);

        session.Next();
        session.Next();
        Assert.Equal("Supper is at six.", session.Current!.Text);

        // The whole article the reader was in is taken off the page.
        live.Execute(new RemoveBlockCommand("page-1", "text-mine"));

        string notice = Assert.IsType<string>(session.TakeChangeNotice());
        Assert.Contains("not in the newsletter any more", notice, StringComparison.Ordinal);
        Assert.DoesNotContain("as it reads now", notice, StringComparison.Ordinal);

        // And it lands somewhere sensible rather than nowhere: there is still writing to read.
        Assert.NotNull(session.Current);
        Assert.Equal("story-other", session.Current!.StoryId);
    }
}
