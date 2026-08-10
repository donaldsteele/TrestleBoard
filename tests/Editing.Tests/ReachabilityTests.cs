using System.Collections.Generic;
using System.Linq;
using TrestleBoard.Editing.Actions;
using Xunit;

namespace TrestleBoard.Editing.Tests;

/// <summary>
/// Verification gate 24: no control is drawn only in states where its action is refused
/// (PLAN.md §11 M71).
///
/// <para><b>Why this can be a test at all.</b> <see cref="ActionCatalog.Evaluate"/> is a pure
/// function of an <see cref="ActionContext"/>, and <see cref="WhatsNext.Suggestions"/> is a pure
/// function of the same thing. So the context that PRODUCES a suggestion is exactly the context that
/// suggestion is rendered in, and asking whether its action can run in that context needs no window,
/// no document and no Avalonia. The owner's report — "Fill in the meeting date on the cover" doing
/// nothing on a fresh template — is one assertion in this file.</para>
///
/// <para><b>What this must NOT flag</b>, per M71 (d). A control that is <i>sometimes</i> unavailable
/// and explains itself is M11 working exactly as designed, and a gate that failed those would be
/// suppressed within a month and protect nothing afterwards. This asks only the narrow question:
/// is there ANY state in which this control is both drawn and usable?</para>
/// </summary>
public sealed class ReachabilityTests
{
    /// <summary>
    /// The panel's "What's next" list is rendered only when nothing is selected and no text is being
    /// edited (<c>ActionPanel.cs</c>: <c>!HasFrameSelection &amp;&amp; !IsEditingText</c>). Every
    /// context below therefore holds those two false — that is the honest reproduction of the state
    /// the buttons actually appear in.
    /// </summary>
    public static TheoryData<string, ActionContext> WhereSuggestionsAppear() => new()
    {
        { "no newsletter open", new ActionContext() },
        { "a fresh template", Fresh() },
        { "the birthday list has gone stale", WithRoster() with { BirthdayListIsStale = true } },
        { "the officers table has gone stale", WithRoster() with { OfficersTableIsStale = true } },
        { "the photo frames are still empty", Fresh() with { HasPicturePlaceholder = true } },
        { "the cover has no meeting date", Fresh() with { CoverDateMissing = true } },
        { "there is more writing than fits", Fresh() with { HasOversetText = true } },
        { "the PDF has not been made", Fresh() with { ExportedPdfThisSession = false } },
        { "an article is still unwritten", Fresh() with { HasUnwrittenArticle = true } },
        { "the address book is empty", Fresh() with { RosterEmptyButNeeded = true } },

        // M75: the state a newsletter started from a template is in. The card leads with "Say which
        // issue this is", and gate 24's whole point is that the button it offers can actually run.
        { "nobody has said which issue this is", Fresh() with { IssueDateChosen = false } },
    };

    /// <summary>
    /// **Gate 24.** Every suggestion the panel offers must be able to run in the very state that
    /// caused it to be offered. A suggestion that can only refuse is worse than no suggestion: the
    /// app named a task, the user pressed it, and the app declined its own advice.
    /// </summary>
    [Theory]
    [MemberData(nameof(WhereSuggestionsAppear))]
    public void EverySuggestionCanRunInTheStateThatOffersIt(string situation, ActionContext context)
    {
        List<string> dead = [];

        foreach (NextStep step in WhatsNext.Suggestions(context))
        {
            // A suggestion with no action is a prompt, not a button — it tells the user what to do
            // and is rendered as text. Nothing to check.
            if (step.ActionId is not { } actionId)
            {
                continue;
            }

            ActionAvailability can = ActionCatalog.Evaluate(actionId, context);
            if (!can.IsAvailable)
            {
                dead.Add($"“{step.Title}” → {actionId}: {can.Reason}");
            }
        }

        Assert.True(
            dead.Count == 0,
            $"With {situation}, the panel offers a suggestion it will then refuse:\n  "
            + string.Join("\n  ", dead)
            + "\n\nThe suggestion is drawn only in this state, so the button can never do anything. "
            + "Three of the other suggestions carry an explicit branch for exactly this — see "
            + "ReplacePicture in ActionCatalog, commented \"reachable the same two ways and for the "
            + "same reason\". PLAN.md §11 M71.");
    }

    /// <summary>
    /// The guard from M71 (d), asserted rather than trusted: this gate must stay quiet about a
    /// command that is legitimately unavailable and says why. If this test ever fails, the gate
    /// above has become too broad and will be switched off by whoever it annoys.
    /// </summary>
    [Fact]
    public void TheGateDoesNotObjectToAnHonestRefusal()
    {
        // Nothing selected, so "Change what this says" is refused — correctly, with a reason. It is
        // not offered by What's next in this state, so gate 24 has no opinion about it.
        ActionAvailability refused = ActionCatalog.Evaluate(ActionId.EditWidget, Fresh());

        Assert.False(refused.IsAvailable);
        Assert.False(string.IsNullOrWhiteSpace(refused.Reason));

        Assert.DoesNotContain(
            WhatsNext.Suggestions(Fresh()),
            step => step.ActionId == ActionId.EditWidget);
    }

    /// <summary>
    /// Anti-vacuity. If the suggestion list ever stops producing actionable steps, the gate above
    /// passes by having nothing to check — the way a test dies quietly.
    /// </summary>
    [Fact]
    public void TheSuggestionsBeingCheckedAreRealOnes()
    {
        ActionContext[] states =
        [
            new ActionContext(),
            Fresh(),
            WithRoster() with { BirthdayListIsStale = true },
            WithRoster() with { OfficersTableIsStale = true },
            Fresh() with { HasPicturePlaceholder = true },
            Fresh() with { CoverDateMissing = true },
            Fresh() with { HasOversetText = true },
            Fresh() with { HasUnwrittenArticle = true },
            Fresh() with { RosterEmptyButNeeded = true },
            Fresh() with { IssueDateChosen = false },
        ];

        int actionable = states
            .SelectMany(WhatsNext.Suggestions)
            .Count(step => step.ActionId is not null);

        Assert.True(actionable >= 8, $"only {actionable} actionable suggestions across every state");
    }

    /// <summary>
    /// A newsletter with a usable address book behind it.
    ///
    /// <para><b>The states this gate checks must be states that can really happen.</b> The first
    /// version of this file paired "the birthday list has gone stale" with an EMPTY address book,
    /// and the gate duly flagged both roster syncs as dead — wrongly. In the app that pairing does
    /// not arise: an empty address book produces "Fill in your address book" instead, and the sync
    /// suggestion only appears once there is something to sync. A reachability gate fed impossible
    /// contexts reports impossible bugs, and that is precisely the noise M71 (d) warns will get the
    /// gate switched off.</para>
    /// </summary>
    private static ActionContext WithRoster() => Fresh() with
    {
        RosterCount = 24,
        RosterBirthdaysThisMonth = 3,
        RosterOfficesFilledIn = 12,
    };

    /// <summary>A newsletter open, nothing selected, not typing — the state the panel shows the list in.</summary>
    private static ActionContext Fresh() => new()
    {
        HasDocument = true,
        PageCount = 1,
        Selection = SelectionKind.None,
        IsEditingText = false,

        // M75: told which issue it is, and with a cover heading to be asked on. The unanswered
        // state is a situation of its own in the table above, not the baseline for eight others.
        IssueDateChosen = true,
        HasCoverHeading = true,
    };
}
