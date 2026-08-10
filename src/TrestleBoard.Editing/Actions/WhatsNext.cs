namespace TrestleBoard.Editing.Actions;

/// <summary>One row of the "what's next" card: what to do, why, and the action that does it.</summary>
/// <param name="Title">The suggestion, as an instruction.</param>
/// <param name="Why">One sentence saying why it is being suggested.</param>
/// <param name="ActionId">The action to run, or null when the user has to do it by hand.</param>
public sealed record NextStep(string Title, string Why, string? ActionId = null);

/// <summary>
/// The panel's no-selection state (PLAN.md §11 M11): a short computed checklist of what this
/// newsletter still needs. This is where the monthly workflow finally becomes visible outside the
/// start dialog — before it, "start from last month" carried the data forward and then said nothing
/// more about what was left to do.
///
/// It only ever suggests; nothing here changes the document. A suggestion that acted on its own
/// would dirty a file the user opened only to look at.
/// </summary>
public static class WhatsNext
{
    /// <summary>The suggestions for this moment, most useful first. Empty means there is nothing to say.</summary>
    public static IReadOnlyList<NextStep> Suggestions(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var steps = new List<NextStep>();
        if (!context.HasDocument)
        {
            steps.Add(new NextStep(
                "Start a newsletter",
                "Nothing is open yet. A template gives you a cover and the usual pages already laid out.",
                Actions.ActionId.NewFromTemplate));
            return steps;
        }

        // M75 (c) — FIRST, and it leads the card while it is unanswered. A newsletter that does not
        // know which issue it is names its PDF " 2000-01.pdf", puts "January 2000" in the mail
        // subject to the whole lodge, and draws its birthday list from January. Nothing else on
        // this card is worth doing before this.
        //
        // This is also why there is no modal on open: the answer is asked for where the user is
        // already looking, not in a window standing between them and a newsletter they opened only
        // to read.
        if (!context.IssueDateChosen)
        {
            steps.Add(new NextStep(
                "Say which issue this is",
                "TrestleBoard does not know which month and year this newsletter is for. It names "
                + "the PDF, it names the email, and the birthday list is worked out from it.",
                Actions.ActionId.SetIssueDate));
        }

        // M75: not while the issue date is unanswered. The staleness flag is computed against
        // January in that state, so it is not a fact yet — and gate 24 forbids the card offering a
        // button that can only refuse, which this one would.
        if (context.BirthdayListIsStale && context.IssueDateChosen)
        {
            steps.Add(new NextStep(
                "Update the birthday list",
                "The birthday list no longer matches your address book — either this issue has moved "
                + "to another month, or somebody's details have changed.",
                Actions.ActionId.SyncBirthdays));
        }

        if (context.OfficersTableIsStale)
        {
            steps.Add(new NextStep(
                "Update the officers table",
                "Somebody's details changed in your address book since the officers table was "
                + "filled in.",
                Actions.ActionId.SyncOfficers));
        }

        if (context.RosterEmptyButNeeded)
        {
            steps.Add(new NextStep(
                "Fill in your address book",
                "A list of people is on the page but the address book is empty, so nothing can be filled in for you.",
                null));
        }

        // M18: the photo template ships three empty frames, and a first issue exported with grey
        // rectangles where the photographs should be is the most visible way this app can let
        // somebody down. Named after the CoverDateMissing precedent — a fact about the document,
        // computed, never acted on by itself.
        if (context.HasPicturePlaceholder)
        {
            steps.Add(new NextStep(
                "Put your photos in",
                "The photo pages are still showing empty picture frames. Double-click a grey one, "
                + "or choose one and use 'Put a picture here…'.",
                Actions.ActionId.ReplacePicture));
        }

        if (context.CoverDateMissing)
        {
            steps.Add(new NextStep(
                "Fill in the meeting date on the cover",
                "The cover heading is on page one but has no meeting date in it yet.",
                Actions.ActionId.EditWidget));
        }

        if (context.HasUnwrittenArticle)
        {
            steps.Add(new NextStep(
                "Write this month's articles",
                "One or more articles still hold the words that were put there to remind you.",
                null));
        }

        if (context.HasOversetText)
        {
            steps.Add(new NextStep(
                "Make the writing fit",
                "Somewhere in the newsletter there is more writing than its frame can show.",
                Actions.ActionId.AutoFlow));
        }

        // M75: same rule. "Make the PDF" is refused until the issue is named — the PDF is named
        // after it — so suggesting it here would be the app declining its own advice.
        if (!context.ExportedPdfThisSession && context.IssueDateChosen)
        {
            steps.Add(new NextStep(
                "Make the PDF",
                "The PDF is the file you email to the lodge. One has not been made since this was opened.",
                Actions.ActionId.ExportPdf));
        }

        return steps;
    }
}
