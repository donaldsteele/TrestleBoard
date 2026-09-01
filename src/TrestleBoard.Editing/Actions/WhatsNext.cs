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
        // M75 (e) defect 2: an EMPTY list counts too, and it is the case a real user meets first.
        // A freshly inserted or template birthday list is Manual, staleness answers false for a
        // manual list at its first line, and so this row only ever appeared to somebody who had
        // already brought birthdays in once. The feature never advertised itself on a new
        // newsletter, which is where it is most wanted.
        if ((context.BirthdayListIsStale || context.BirthdayListIsEmpty) && context.IssueDateChosen)
        {
            steps.Add(context.BirthdayListIsEmpty && !context.BirthdayListIsStale
                ? new NextStep(
                    "Fill in the birthday list",
                    $"The birthday list on the page is empty, and your address book has "
                    + $"{People(context.RosterBirthdaysThisMonth)} born in "
                    + $"{ActionCatalog.MonthName(context.IssueMonth)}, which is the month this issue "
                    + "is for.",
                    Actions.ActionId.SyncBirthdays)
                : new NextStep(
                    "Update the birthday list",
                    "The birthday list no longer matches your address book for "
                    + $"{ActionCatalog.MonthName(context.IssueMonth)} — either this issue has moved "
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

        // M75 (f): an unreadable address book looks empty from up here, and telling somebody to fill
        // in a book he has already filled in is how a locked file becomes an afternoon's work.
        if (context.RosterCouldNotBeRead)
        {
            steps.Add(new NextStep(
                "Let TrestleBoard reach your address book",
                "Your address book is on this computer but could not be read, so nothing can be "
                + "filled in for you. It is not empty. Close any other program using it, then start "
                + "TrestleBoard again.",
                null));
        }
        else if (context.RosterEmptyButNeeded)
        {
            // M82: this used to read "A list of people is on the page but the address book is
            // empty" — a sentence about STATE, where every other row on this card is an
            // instruction. It now leads with what to do, and says the state as the reason.
            steps.Add(new NextStep(
                "Fill in your address book",
                "Import your member list, or type a few names in, and TrestleBoard can fill in the "
                + "list of people already on the page. The address book is empty at the moment, so "
                + "there is nothing to fill it from.",
                Actions.ActionId.ImportPeople));
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
            // M82: how much, when the chosen frame is the one that is short. The marker has said
            // since M43 that there is more writing than fits and never said how much — and "about
            // forty words" is a quantity a committee can act on, because they know what forty
            // words of their own article looks like.
            steps.Add(new NextStep(
                "Make the writing fit",
                context.SelectionOversetWords is > 0 and { } words
                    ? Core.Text.OversetWords.Describe(words)
                        + " Make the box taller, or send the rest to the next page."
                    : "Somewhere in the newsletter there is more writing than its frame can show.",
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

    /// <summary>"one person" or "3 people" — a count read aloud, not a number in a field.</summary>
    private static string People(int count) => count == 1
        ? "one person"
        : $"{count.ToString(System.Globalization.CultureInfo.InvariantCulture)} people";
}
