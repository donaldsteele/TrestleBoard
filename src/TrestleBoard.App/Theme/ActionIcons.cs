using TrestleBoard.Editing.Actions;

namespace TrestleBoard.App.Theme;

/// <summary>
/// Which actions and which widgets have a glyph, and — just as importantly — which do not
/// (PLAN.md §11 M16).
///
/// <para><b><see cref="EditorAction"/> gains no icon field.</b> <c>docs/M7-spec.md</c> already says
/// the App layer maps keys to glyphs, which is how <see cref="IWidgetDefinition"/>'s
/// <c>IconKey</c> has worked since M7; inventing a second mechanism for actions when a documented
/// one exists for widgets is the worse outcome, and a seventh positional field would touch all
/// sixty-seven catalog entries in a diff where the real change is invisible.</para>
///
/// <para><b>The map is deliberately partial, and <see cref="WithoutAnIcon"/> is a design record
/// rather than a backlog.</b> Every action that has no glyph is listed there with its reason, and
/// <c>IconTests</c> requires the two sets to partition <c>ActionCatalog.All</c> exactly — so a new
/// action cannot arrive without somebody deciding, on the record, whether it gets a picture.</para>
/// </summary>
internal static class ActionIcons
{
    /// <summary>The resource-key prefix every glyph in <c>Theme/Icons.axaml</c> shares.</summary>
    internal const string Prefix = "TrestleBoard.Icon.";

    private static readonly Dictionary<string, string> ForActions = new(StringComparer.Ordinal)
    {
        // The eight toolbar commands: the only buttons in the app a user reaches for by shape.
        [ActionId.Open] = "open-folder",
        [ActionId.Undo] = "undo",
        [ActionId.Redo] = "redo",
        [ActionId.PreviousPage] = "chevron-left",
        [ActionId.NextPage] = "chevron-right",
        [ActionId.ZoomOut] = "minus-circle",
        [ActionId.ZoomIn] = "plus-circle",
        [ActionId.FitPage] = "fit-page",

        // The Insert group, drawn with the same six glyphs the widgets themselves use — one
        // dictionary serving both, which is what closes M7's deferral rather than duplicating it.
        [ActionId.InsertOfficers] = "officers",
        [ActionId.InsertBirthdays] = "birthdays",
        [ActionId.InsertCommittees] = "committees",
        [ActionId.InsertDistrictCalendar] = "district-calendar",
        [ActionId.InsertEventCard] = "announcement",
        [ActionId.InsertCoverBanner] = "cover-banner",

        // Ten primary or item-level actions. Nine are IsPrimary; item.editList is not, but it sits
        // directly beside item.edit in the panel and reads as its twin without one.
        [ActionId.ExportPdf] = "export-pdf",
        [ActionId.Bold] = "bold",
        [ActionId.Italic] = "italic",
        [ActionId.FontsAndStyles] = "font",
        [ActionId.AddTextFrame] = "text-frame",
        [ActionId.InsertPhoto] = "photo",
        [ActionId.EditWidget] = "edit",
        [ActionId.EditWidgetList] = "edit",
        [ActionId.FixPhoto] = "wand",
        [ActionId.ToggleWrap] = "wrap",
        [ActionId.ShowPeople] = "people",
    };

    /// <summary>
    /// <see cref="IWidgetDefinition.IconKey"/> to glyph. These six keys are the complete set of
    /// values that property has ever returned, and <c>IconTests</c> checks that both ways round.
    /// </summary>
    private static readonly Dictionary<string, string> ForWidgets = new(StringComparer.Ordinal)
    {
        ["officers"] = "officers",
        ["birthdays"] = "birthdays",
        ["committees"] = "committees",
        ["districtCalendar"] = "district-calendar",
        ["announcement"] = "announcement",
        ["coverBanner"] = "cover-banner",
    };

    /// <summary>
    /// The actions that get no glyph, each with the reason it gets none. An entry here is a
    /// decision, not a to-do: adding a picture to any of these would make the panel noisier without
    /// making one action easier to find.
    /// </summary>
    private static readonly Dictionary<string, string> WithoutAnIcon = new(StringComparer.Ordinal)
    {
        // --- Whole-newsletter commands. Each is a sentence, not a symbol; there is no picture of
        //     "start from last month" that beats the words "Start from last month".
        [ActionId.OpenSample] = "an example, not a file operation — no shape says that",
        [ActionId.NewFromTemplate] = "a sentence, not a symbol",
        [ActionId.StartFromLastMonth] = "a sentence, not a symbol",
        [ActionId.Exit] = "a menu command, and the panel never offers it",

        // --- Clipboard. Universally recognised and barely used on a layout canvas: this app's text
        //     editing happens inside frames, and the scissors/clipboard trio would take three slots
        //     in the Edit group to say what everyone already knows.
        [ActionId.Cut] = "universally recognised, and barely used on a layout canvas",
        [ActionId.Copy] = "universally recognised, and barely used on a layout canvas",
        [ActionId.Paste] = "universally recognised, and barely used on a layout canvas",
        [ActionId.SelectAll] = "no shape distinguishes it from Copy at 20px",

        // --- Text. Bold, italic and the font picker have glyphs; the rest are about a named style,
        //     and the name IS the discriminator.
        [ActionId.ParagraphStyle] = "opens a further choice; the ▸ is the signal, not a picture",
        [ActionId.BiggerText] = "the ladder is the point, and + / − already appear in the title",
        [ActionId.SmallerText] = "the ladder is the point, and + / − already appear in the title",
        [ActionId.FontJustHere] = "would be indistinguishable from text.fontsAndStyles",
        [ActionId.ClearFontOverride] = "would be indistinguishable from text.fontsAndStyles",

        // --- The selected thing.
        [ActionId.DeleteFrame] = "a bin icon invites the click this audience most fears",
        [ActionId.FitToContents] = "would be indistinguishable from view.fitPage",
        [ActionId.SyncBirthdays] = "the sentence names both ends of the operation; no glyph does",

        [ActionId.AdjustPhoto] = "sliders read as decoration beside picture.fix's wand",

        // --- Flow. Only wrap gets one: the other three are relationships between two frames, and a
        //     relationship has no silhouette.
        [ActionId.LinkFrames] = "a relationship between two frames has no silhouette",
        [ActionId.UnlinkFrames] = "a relationship between two frames has no silhouette",
        [ActionId.AutoFlow] = "a relationship between two frames has no silhouette",

        // --- Arrange. Four near-identical stacking arrows add visual noise and no discrimination
        //     whatever — the group is the one place in the app where four icons would be WORSE.
        [ActionId.BringForward] = "four near-identical stacking arrows discriminate nothing",
        [ActionId.SendBackward] = "four near-identical stacking arrows discriminate nothing",
        [ActionId.BringToFront] = "four near-identical stacking arrows discriminate nothing",
        [ActionId.SendToBack] = "four near-identical stacking arrows discriminate nothing",

        // --- Pages. Next and previous have the toolbar chevrons; the rest act on the page itself
        //     and would need a second page glyph that means something different from the first.
        [ActionId.AddPage] = "would collide with the chevrons already used for page movement",
        [ActionId.RemovePage] = "would collide with the chevrons already used for page movement",
        [ActionId.MovePageEarlier] = "would collide with the chevrons already used for page movement",
        [ActionId.MovePageLater] = "would collide with the chevrons already used for page movement",

        // --- View. Zoom and fit have glyphs; the rest are settings and navigation.
        [ActionId.ActualSize] = "a number, not a shape",
        [ActionId.Settings] = "a cogwheel is jargon to this audience",
        [ActionId.NextRegion] = "keyboard navigation, reached by F6 and not by looking",
        [ActionId.PreviousRegion] = "keyboard navigation, reached by F6 and not by looking",
        [ActionId.ToggleActionPanel] = "the panel it opens is the thing being described",
        [ActionId.ShowFontChanges] = "a diagnostic overlay; no picture explains it",

        // --- The address book. people.show has the book; the four that follow act ON the book and
        //     would each need the same book plus a modifier, which is four ambiguous icons.
        [ActionId.ImportPeople] = "acts on the address book; would repeat people.show's glyph",
        [ActionId.ExportPeople] = "acts on the address book; would repeat people.show's glyph",
        [ActionId.UndoPeopleChange] = "would collide with edit.undo while meaning something else",
        [ActionId.RestorePeople] = "acts on the address book; would repeat people.show's glyph",

        // --- Help. Every one of these is a sentence the user reads once.
        [ActionId.CheckForUpdates] = "read once, never hunted for",
        [ActionId.About] = "read once, never hunted for",
        [ActionId.FontLicences] = "read once, never hunted for",
        [ActionId.ShowExampleIssue] = "read once, never hunted for",
    };

    /// <summary>The glyph resource key for an action, or null if it is deliberately text-only.</summary>
    internal static string? ForAction(string actionId) =>
        ForActions.TryGetValue(actionId, out string? key) ? Prefix + key : null;

    /// <summary>The glyph resource key for a widget's <c>IconKey</c>, or null if it has none.</summary>
    internal static string? ForWidget(string iconKey) =>
        ForWidgets.TryGetValue(iconKey, out string? key) ? Prefix + key : null;

    /// <summary>Every action id that has a glyph. For the tests.</summary>
    internal static IReadOnlyCollection<string> ActionsWithAnIcon => ForActions.Keys;

    /// <summary>Every action id deliberately without one, and why. For the tests.</summary>
    internal static IReadOnlyDictionary<string, string> ActionsWithoutAnIcon => WithoutAnIcon;

    /// <summary>Every widget <c>IconKey</c> that has a glyph. For the tests.</summary>
    internal static IReadOnlyCollection<string> WidgetKeysWithAnIcon => ForWidgets.Keys;

    /// <summary>Every glyph key this map can ask for, so the tests can check none is orphaned.</summary>
    internal static IReadOnlySet<string> EveryGlyphKey =>
        ForActions.Values.Concat(ForWidgets.Values).Select(v => Prefix + v).ToHashSet(StringComparer.Ordinal);
}
