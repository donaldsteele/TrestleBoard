using System;
using System.Collections.Generic;
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
}
