using System;
using System.Collections.Generic;
using System.Linq;
using TrestleBoard.Editing.Actions;
using TrestleBoard.Editing.Help;
using Xunit;

namespace TrestleBoard.Editing.Tests;

/// <summary>
/// M63's help index (PLAN.md §11 M63).
///
/// <para>The index is generated from the action catalog, so "every command has a topic" is true by
/// construction and a test asserting it can only ever pass. The tests that can actually fail are
/// the <b>corpus</b> — real words a confused user would type, and the command each one must find —
/// and the guard that no synonym names a command that has since been renamed away.</para>
/// </summary>
public sealed class HelpIndexTests
{
    // ---- the generated index ------------------------------------------------------------------

    /// <summary>
    /// PLAN.md's acceptance in as many words: <i>a catalog entry with no help topic fails a test</i>.
    ///
    /// <para>True by construction rather than by care, which is the point of generating it. The
    /// count assertion is the anti-vacuity guard: if the catalog were ever empty, or the projection
    /// silently filtered, both sides would agree on nothing and this test would still pass.</para>
    /// </summary>
    [Fact]
    public void EveryCommandThisAppHasCanBeAskedAbout()
    {
        Assert.Equal(
            ActionCatalog.All.Select(a => a.Id).Order(StringComparer.Ordinal),
            HelpIndex.Topics.Select(t => t.ActionId).Order(StringComparer.Ordinal));

        Assert.True(
            HelpIndex.Topics.Count > 100,
            $"only {HelpIndex.Topics.Count} topics — the catalog has {ActionCatalog.All.Count}");
    }

    [Fact]
    public void NoTopicIsLeftWithNothingToSay()
    {
        List<string> silent =
        [
            .. HelpIndex.Topics
                .Where(t => string.IsNullOrWhiteSpace(t.Title) || string.IsNullOrWhiteSpace(t.Answer))
                .Select(t => t.ActionId),
        ];

        Assert.True(silent.Count == 0, "these topics have no title or no answer: " + string.Join(", ", silent));
    }

    /// <summary>
    /// The shortcut is read from the catalog rather than from <c>KeyboardMap</c>, which is internal
    /// to the App layer. Two audit tests already prove the two agree, so this only has to prove the
    /// index did not lose it on the way through.
    /// </summary>
    [Fact]
    public void AShortcutReachesTheTopicItBelongsTo()
    {
        Assert.Equal("Ctrl+Z", HelpIndex.For(ActionId.Undo).Shortcut);
        Assert.Equal("Ctrl+B", HelpIndex.For(ActionId.Bold).Shortcut);
        Assert.Null(HelpIndex.For(ActionId.About).Shortcut);

        Assert.Equal(
            ActionCatalog.All.Count(a => a.DisplayGesture is not null),
            HelpIndex.Topics.Count(t => t.Shortcut is not null));
    }

    [Fact]
    public void TheMenuPathIsLeftForTheAppLayerToFillIn() =>
        Assert.All(HelpIndex.Topics, t => Assert.Null(t.MenuPath));

    [Fact]
    public void AskingAboutSomethingThatIsNotACommandIsAProgrammingError() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => HelpIndex.For("newsletter.teleport"));

    // ---- the corpus: what somebody actually types -----------------------------------------------

    /// <summary>
    /// The test this milestone lives or dies by. Each row is a word a member of this committee
    /// would plausibly type into the box, and the command it has to find. They are written in the
    /// user's vocabulary, not the app's — "picture" not "photo", "print" not "export", "members"
    /// not "address book" — because the whole reason the search box exists is that those two
    /// vocabularies differ.
    ///
    /// <para>A search box that answers "nothing found" to a word the user knows is worse than no
    /// search box at all: it teaches them the answer is not in there, and they stop asking.</para>
    /// </summary>
    [Theory]
    // The picture words. The app says "photo" in places and "picture" in others; both must work.
    [InlineData("picture", ActionId.InsertPhoto)]
    [InlineData("photograph", ActionId.InsertPhoto)]
    [InlineData("sideways", ActionId.FixPhoto)]
    [InlineData("upside down", ActionId.FixPhoto)]
    // Getting it out of the app, in the three words people use for it.
    [InlineData("pdf", ActionId.ExportPdf)]
    [InlineData("print", ActionId.PrintPdf)]
    [InlineData("email", ActionId.SendIt)]
    // The monthly cycle.
    [InlineData("next month", ActionId.StartFromLastMonth)]
    [InlineData("crash", ActionId.RestoreDocument)]
    [InlineData("spelling", ActionId.CheckSpelling)]
    [InlineData("last year", ActionId.ShowLastYear)]
    // The single most-asked question anybody has ever asked about a computer.
    //
    // The word tested is "oops" and not "mistake", and that is a finding rather than a convenience:
    // bare "mistake" is genuinely ambiguous in this app, because "Show my spelling mistakes" is
    // called that and "Look it over with me" is about finding mistakes too. Ranking Undo first for
    // it would mean teaching the search box that one reading of an ambiguous word is the only one.
    // "Mistake" stays a search word for Undo — it finds it third, behind the two commands that are
    // literally about mistakes, which is the right order.
    [InlineData("oops", ActionId.Undo)]
    // The address book, which nobody calls that.
    [InlineData("members", ActionId.ShowPeople)]
    [InlineData("roster", ActionId.ShowPeople)]
    // The page.
    [InlineData("birthdays", ActionId.InsertBirthdays)]
    [InlineData("officers", ActionId.InsertOfficers)]
    [InlineData("bullet", ActionId.BulletList)]
    [InlineData("bold", ActionId.Bold)]
    [InlineData("passed away", ActionId.InsertPhrase)]
    // "Too small" is a complaint about the writing, and the answer is to make it bigger. Its mirror
    // image, Smaller, must not win it.
    [InlineData("too small", ActionId.BiggerText)]
    // The app itself.
    [InlineData("colours", ActionId.Settings)]
    [InlineData("example", ActionId.ShowExampleIssue)]
    public void TheWordSomebodyWouldTypeFindsTheThingTheyMeant(string typed, string expected)
    {
        IReadOnlyList<HelpTopic> found = HelpIndex.Search(typed);

        Assert.True(found.Count > 0, $"\"{typed}\" found nothing at all");
        Assert.True(
            found[0].ActionId == expected,
            $"\"{typed}\" should have found {expected} first, but found "
            + string.Join(", ", found.Take(4).Select(t => t.ActionId)));
    }

    // ---- how the searching behaves ----------------------------------------------------------------

    /// <summary>An empty box browses the whole app, in menu order, rather than showing nothing.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyBoxOffersEverythingInMenuOrder(string? nothing)
    {
        Assert.Equal(
            ActionCatalog.All.Select(a => a.Id),
            HelpIndex.Search(nothing).Select(t => t.ActionId));
    }

    /// <summary>
    /// Two words narrow rather than widen. Somebody who types "picture caption" means both; a
    /// search that answered with every photo command and every caption command would bury the one
    /// thing they asked for under the two things they did not.
    /// </summary>
    [Fact]
    public void TypingASecondWordNarrowsTheAnswer()
    {
        int one = HelpIndex.Search("picture").Count;
        int two = HelpIndex.Search("picture caption").Count;

        Assert.True(two < one, $"\"picture caption\" returned {two} answers and \"picture\" {one}");
        Assert.Equal(ActionId.CaptionPicture, HelpIndex.Search("picture caption")[0].ActionId);
    }

    /// <summary>
    /// Prefix matching, not substring. "ear" must not find "last year" — a substring match on short
    /// words turns a search box into a random-answer machine, and this audience will read the first
    /// answer as the right one.
    /// </summary>
    [Fact]
    public void AWordIsMatchedFromItsStartAndNotFromTheMiddle()
    {
        Assert.DoesNotContain(
            HelpIndex.Search("ear"),
            t => t.ActionId == ActionId.ShowLastYear);

        // …but the real prefix still works, so this is not just a broken search.
        Assert.Contains(HelpIndex.Search("year"), t => t.ActionId == ActionId.ShowLastYear);
    }

    [Fact]
    public void AWordNobodyInThisAppUsesFindsNothingRatherThanEverything() =>
        Assert.Empty(HelpIndex.Search("kubernetes"));

    [Fact]
    public void SearchingIsNotFussyAboutCapitalsOrTrailingPunctuation()
    {
        Assert.Equal(
            HelpIndex.Search("bold").Select(t => t.ActionId),
            HelpIndex.Search("  BOLD?  ").Select(t => t.ActionId));
    }

    /// <summary>
    /// The command's own name beats a description mentioning it. Somebody typing "undo" wants Undo,
    /// not the several commands whose sentence happens to say "take back".
    /// </summary>
    [Fact]
    public void TheCommandsOwnNameOutranksASentenceMentioningIt() =>
        Assert.Equal(ActionId.Undo, HelpIndex.Search("undo")[0].ActionId);

    // ---- the guard on the authored half ------------------------------------------------------------

    /// <summary>
    /// The same guard <c>KeyboardAuditTests.EveryRegisteredGestureNamesARealAction</c> puts on the
    /// keyboard table. A renamed action leaves dead search words behind, and dead search words fail
    /// <i>silently</i> — nothing throws, the user simply never finds the thing.
    /// </summary>
    [Fact]
    public void EverySynonymNamesARealAction()
    {
        HashSet<string> real = ActionCatalog.All.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        List<string> dead = [.. HelpSearchWords.ActionsWithExtraWords.Where(id => !real.Contains(id))];

        Assert.True(dead.Count == 0, "these have search words but are no longer commands: " + string.Join(", ", dead));
    }

    [Fact]
    public void NoSynonymIsBlank()
    {
        List<string> blank =
        [
            .. HelpSearchWords.ActionsWithExtraWords
                .Where(id => HelpSearchWords.For(id).Any(string.IsNullOrWhiteSpace)),
        ];

        Assert.True(blank.Count == 0, "these have a blank search word: " + string.Join(", ", blank));
    }

    /// <summary>
    /// A synonym that merely repeats a word already in the title buys nothing and hides the fact
    /// that the table is meant for the <i>gap</i> between the app's vocabulary and the user's. This
    /// keeps the table honest about why each row is there.
    /// </summary>
    [Fact]
    public void EveryActionWithSearchWordsHasAtLeastOneTheTitleDoesNotAlreadySay()
    {
        var pointless = new List<string>();
        foreach (string id in HelpSearchWords.ActionsWithExtraWords)
        {
            string title = ActionCatalog.Get(id).Title;
            if (HelpSearchWords.For(id).All(w => title.Contains(w, StringComparison.OrdinalIgnoreCase)))
            {
                pointless.Add(id);
            }
        }

        Assert.True(
            pointless.Count == 0,
            "every search word for these is already in the title, so the row earns nothing: "
            + string.Join(", ", pointless));
    }
}
