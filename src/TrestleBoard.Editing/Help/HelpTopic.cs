using System.Collections.Generic;
using TrestleBoard.Editing.Actions;

namespace TrestleBoard.Editing.Help;

/// <summary>
/// One answer in the "How do I…?" window (PLAN.md §11 M63).
///
/// <para>A topic is <b>generated from a catalog entry</b> rather than written beside it. The
/// catalog already knows every verb this app has, in plain language, with a one-sentence
/// description and the shortcut as the user would type it — writing that a second time would
/// create a second copy of the same fact, and the two would disagree the first week somebody
/// reworded a menu item. The same argument M55 made about status and M61 made about list numbers:
/// derive it, do not store it.</para>
///
/// <para><see cref="MenuPath"/> is the exception, and it is filled in by the App layer rather than
/// here. The path a command sits at is a fact about the menu bar, which lives in XAML the Editing
/// layer cannot see and must not learn about. It is null in the index and set when the window
/// builds, so this type stays honest about what it knows on its own.</para>
/// </summary>
/// <param name="ActionId">The action this answers "how do I" for.</param>
/// <param name="Title">What the app itself calls the command, verbatim from the catalog.</param>
/// <param name="Answer">The catalog's one-sentence description.</param>
/// <param name="Group">Which part of the app it belongs to, for browsing with an empty search box.</param>
/// <param name="Shortcut">The gesture as the user would type it, or null.</param>
/// <param name="OtherWords">
/// What a confused user might type instead of the title — "picture" for a photo, "email" for
/// sending. Authored in <see cref="HelpSearchWords"/> and empty for most actions, because most
/// titles already say the word somebody would look for.
/// </param>
/// <param name="MenuPath">Where it sits in the menu bar, or null if the App layer has not said.</param>
public sealed record HelpTopic(
    string ActionId,
    string Title,
    string Answer,
    ActionGroup Group,
    string? Shortcut,
    IReadOnlyList<string> OtherWords,
    string? MenuPath = null)
{
    /// <summary>The group's plain-language heading — "The words", "Add to the page".</summary>
    public string GroupLabel => ActionCatalog.DescribeGroup(Group);
}
