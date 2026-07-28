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

        if (context.BirthdayListIsStale)
        {
            steps.Add(new NextStep(
                "Update the birthday list",
                "The birthday list no longer matches your address book — either this issue has moved "
                + "to another month, or somebody's details have changed.",
                Actions.ActionId.SyncBirthdays));
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
                "The photo pages are still showing placeholders. Double-click a grey picture frame, "
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

        if (!context.ExportedPdfThisSession)
        {
            steps.Add(new NextStep(
                "Export the PDF",
                "The PDF is the file you email to the lodge. Nothing has been exported since this was opened.",
                Actions.ActionId.ExportPdf));
        }

        return steps;
    }
}
