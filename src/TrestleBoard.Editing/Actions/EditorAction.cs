namespace TrestleBoard.Editing.Actions;

/// <summary>
/// Where an action belongs in the panel and in the restructured Object menu (PLAN.md §11 M11).
/// The grouping is by what the user is looking at, not by which class implements it.
/// </summary>
public enum ActionGroup
{
    /// <summary>Opening, starting and exporting a newsletter.</summary>
    Newsletter,

    /// <summary>Undo, redo, and the clipboard.</summary>
    Edit,

    /// <summary>Bold, italic, paragraph style.</summary>
    Text,

    /// <summary>Putting a new thing on the page.</summary>
    Insert,

    /// <summary>Acting on whatever is selected right now.</summary>
    Item,

    /// <summary>Pictures.</summary>
    Picture,

    /// <summary>How text flows: wrap, link, continue.</summary>
    TextFlow,

    /// <summary>Front-to-back order.</summary>
    Arrange,

    /// <summary>Pages.</summary>
    Page,

    /// <summary>Zoom, theme, window regions.</summary>
    View,

    /// <summary>Updates and the about box.</summary>
    Help,
}

/// <summary>
/// One thing the user can do, described the way it is shown to them. This is the *declaration* —
/// how to actually do it lives in the App layer's action runner, which is the whole point of the
/// split: this project owns "can I, and if not, why not, in plain English" and nothing else, so
/// every reason string in the app is testable without an Avalonia session.
/// </summary>
/// <param name="Id">One of the constants on <see cref="ActionId"/>.</param>
/// <param name="Title">What the button says. Sentence case, plain language, no jargon (PLAN.md §6).</param>
/// <param name="ShortDescription">One sentence saying what will happen; shown under the title in the panel.</param>
/// <param name="Group">Which part of the panel and which submenu it belongs to.</param>
/// <param name="DisplayGesture">The shortcut as the user would type it, or null if it has none.</param>
/// <param name="IsPrimary">Primary actions sort first in their group and are drawn larger.</param>
public sealed record EditorAction(
    string Id,
    string Title,
    string ShortDescription,
    ActionGroup Group,
    string? DisplayGesture = null,
    bool IsPrimary = false);
