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
        [ActionId.InsertMonthCalendar] = "month-calendar",
        [ActionId.InsertEventCard] = "announcement",
        [ActionId.InsertCoverBanner] = "cover-banner",
        [ActionId.InsertSimpleList] = "simple-list",

        // Ten primary or item-level actions. Nine are IsPrimary; item.editList is not, but it sits
        // directly beside item.edit in the panel and reads as its twin without one.
        // M24. Both wear the same disk for the reason item.edit and item.editList share one: they
        // are the same act, and the only difference is whether the app already knows where to put it.
        [ActionId.Save] = "save",
        [ActionId.SaveAs] = "save",

        [ActionId.ExportPdf] = "export-pdf",
        [ActionId.Bold] = "bold",
        [ActionId.Italic] = "italic",
        [ActionId.FontsAndStyles] = "font",
        [ActionId.AddTextFrame] = "text-frame",
        [ActionId.InsertPhoto] = "photo",
        [ActionId.EditWidget] = "edit",
        [ActionId.EditWidgetList] = "edit",

        // M18. Putting a picture into an empty frame is the same act as inserting one, so it wears
        // the same glyph: the user is choosing a photograph either way, and the frame it lands in is
        // the only difference between them.
        [ActionId.ReplacePicture] = "photo",

        // M65. An emblem wears the picture glyph for the reason above: the user is putting a
        // picture on the page, and where it came from — their camera or the app's own shelf — is
        // not a difference a glyph should be trying to draw.
        [ActionId.InsertEmblem] = "photo",

        // M67. A page of a PDF becomes a picture the moment it arrives, so it wears the picture
        // glyph too. A document icon would advertise a PDF that no longer exists by the time
        // anything is on the page.
        [ActionId.BringInPdfPage] = "photo",
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
        ["month-calendar"] = "month-calendar",
        ["announcement"] = "announcement",
        ["coverBanner"] = "cover-banner",
        ["simple-list"] = "simple-list",
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

        // M75. A calendar glyph is the obvious choice and is exactly wrong: it is the universal
        // symbol for a date picker, and this question deliberately is NOT one — it asks for a month
        // and a year, and the issue has no day. The picture would promise the control the design
        // rejected.
        [ActionId.SetIssueDate] = "a calendar glyph promises the date picker this deliberately is not",

        // M39. A clock-with-arrow would be the obvious choice and is exactly wrong: it is the
        // undo glyph in every other program, and this is not undo — it replaces what is on screen.
        [ActionId.RestoreDocument] = "the obvious clock-arrow reads as undo, which this is not",

        // --- Clipboard. Universally recognised and barely used on a layout canvas: this app's text
        //     editing happens inside frames, and the scissors/clipboard trio would take three slots
        //     in the Edit group to say what everyone already knows.
        [ActionId.Cut] = "universally recognised, and barely used on a layout canvas",
        [ActionId.Copy] = "universally recognised, and barely used on a layout canvas",
        [ActionId.Paste] = "universally recognised, and barely used on a layout canvas",
        [ActionId.SelectAll] = "no shape distinguishes it from Copy at 20px",
        [ActionId.SelectAllFrames] = "the same shape as edit.selectAll, meaning something else",
        [ActionId.AddNextToSelection] = "an arrow, which is what every other navigation glyph is",
        [ActionId.AddPreviousToSelection] = "an arrow, which is what every other navigation glyph is",

        // M91. An arrow again, and the Page group is already four arrows deep — next, previous,
        // this page earlier, this page later. A fifth and sixth pointing the same two directions
        // while meaning something else entirely would take discrimination AWAY from the four that
        // are there, which is the opposite of what an icon is for.
        // M93. Every glyph for line spacing is a stack of horizontal bars with arrows, which at
        // 20px is indistinguishable from the alignment and list glyphs that mean other things.
        [ActionId.WritingLook] = "a stack of bars, which every other text glyph already is",

        [ActionId.MoveToNextPage] = "an arrow, and the page arrows already mean something else",
        [ActionId.MoveToPreviousPage] = "an arrow, and the page arrows already mean something else",

        // M21. A magnifying glass is the one clipboard-era glyph this audience would recognise, and
        // it is already spoken for by zoom — where it means something else entirely.
        [ActionId.Find] = "a magnifying glass would collide with zoom, where it means something else",
        [ActionId.Replace] = "the same glass again, and the difference is the words, not the picture",

        // --- Text. Bold, italic and the font picker have glyphs; the rest are about a named style,
        //     and the name IS the discriminator.
        [ActionId.ParagraphStyle] = "opens a further choice; the ▸ is the signal, not a picture",

        // M61. The usual bullet-list glyph is three dots and three lines, which at 20px is
        // indistinguishable from the numbered one beside it and from M60's table icon. The two
        // sentences tell them apart; three near-identical pictures would not.
        [ActionId.BulletList] = "the usual three-dots glyph is unreadable beside the numbered one",
        [ActionId.NumberList] = "the usual 1-2-3 glyph is unreadable beside the dotted one",
        [ActionId.BiggerText] = "the ladder is the point, and + / − already appear in the title",
        [ActionId.SmallerText] = "the ladder is the point, and + / − already appear in the title",
        [ActionId.FontJustHere] = "would be indistinguishable from text.fontsAndStyles",
        [ActionId.ClearFontOverride] = "would be indistinguishable from text.fontsAndStyles",

        // --- The selected thing.
        [ActionId.DeleteFrame] = "a bin icon invites the click this audience most fears",
        [ActionId.FitToContents] = "would be indistinguishable from view.fitPage",
        [ActionId.SyncBirthdays] = "the sentence names both ends of the operation; no glyph does",
        [ActionId.SyncOfficers] = "the sentence names both ends of the operation; no glyph does",

        [ActionId.AdjustPhoto] = "sliders read as decoration beside picture.fix's wand",

        // M18's other three. Each is words about a picture rather than an act on one, and the
        // pictures that would say "caption" or "description" are the words themselves.
        [ActionId.PositionPicture] = "a crosshair reads as a target, not as 'which part of it shows'",
        [ActionId.DismissCropNotice] = "a sentence about a warning, not a shape",
        [ActionId.CaptionPicture] = "the words under a picture cannot be drawn as a picture",
        [ActionId.DescribePicture] = "spoken, never drawn — there is no shape for what is said aloud",

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

        // M21's eight. The same argument, harder: six alignment glyphs differ from each other by
        // which edge of a diagram is emboldened, which is a distinction nobody makes at 20px, and
        // the two distribute ones differ from those six by even less.
        [ActionId.AlignLeft] = "six alignment glyphs differ by one emboldened edge — not at 20px",
        [ActionId.AlignCentres] = "six alignment glyphs differ by one emboldened edge — not at 20px",
        [ActionId.AlignRight] = "six alignment glyphs differ by one emboldened edge — not at 20px",
        [ActionId.AlignTop] = "six alignment glyphs differ by one emboldened edge — not at 20px",
        [ActionId.AlignMiddles] = "six alignment glyphs differ by one emboldened edge — not at 20px",
        [ActionId.AlignBottom] = "six alignment glyphs differ by one emboldened edge — not at 20px",
        [ActionId.DistributeHorizontally] = "differs from the alignment six by less than they differ from each other",
        [ActionId.DistributeVertically] = "differs from the alignment six by less than they differ from each other",

        // --- Pages. Next and previous have the toolbar chevrons; the rest act on the page itself
        //     and would need a second page glyph that means something different from the first.
        [ActionId.AddPage] = "would collide with the chevrons already used for page movement",
        [ActionId.RemovePage] = "would collide with the chevrons already used for page movement",
        [ActionId.MovePageEarlier] = "would collide with the chevrons already used for page movement",
        [ActionId.MovePageLater] = "would collide with the chevrons already used for page movement",

        // M76 (g). The rail's tiles ARE the picture of this command — each one is the page it goes
        // to, drawn at a size you can recognise — so a 20px glyph beside them would be a smaller,
        // worse copy of what the user is already looking at.
        [ActionId.GoToPage] = "the rail's own miniature pages are the picture; a glyph would repeat them",

        // --- View. Zoom and fit have glyphs; the rest are settings and navigation.
        [ActionId.ActualSize] = "a number, not a shape",
        [ActionId.Settings] = "a cogwheel is jargon to this audience",
        [ActionId.NextRegion] = "keyboard navigation, reached by F6 and not by looking",
        [ActionId.PreviousRegion] = "keyboard navigation, reached by F6 and not by looking",
        [ActionId.ToggleActionPanel] = "the panel it opens is the thing being described",

        // M76 (g): the same reason as the panel's switch, one surface along.
        [ActionId.TogglePageRail] = "the rail it opens is the thing being described",
        [ActionId.ShowFontChanges] = "a diagnostic overlay; no picture explains it",
        [ActionId.ShowMargins] = "a rectangle inside a rectangle, at 20px, is a grey square",

        // --- The address book. people.show has the book; the four that follow act ON the book and
        //     would each need the same book plus a modifier, which is four ambiguous icons.
        [ActionId.ImportPeople] = "acts on the address book; would repeat people.show's glyph",
        [ActionId.ExportPeople] = "acts on the address book; would repeat people.show's glyph",
        [ActionId.UndoPeopleChange] = "would collide with edit.undo while meaning something else",
        [ActionId.RestorePeople] = "acts on the address book; would repeat people.show's glyph",

        // --- Help. Every one of these is a sentence the user reads once.
        [ActionId.CheckForUpdates] = "read once, never hunted for",

        // M79. All three are about how a thing LOOKS, and a glyph for "a border" at panel size is
        // a small grey rectangle — which is what an icon-less action already looks like. The words
        // carry it; the page shows the result the moment it is pressed.
        [ActionId.AddRule] = "the result is visible on the page; a glyph would restate the words",

        // M81. All three are read as words: two are verbs about the chosen thing and the third is
        // a File-menu export beside the others, none of which carries a glyph either.
        [ActionId.Duplicate] = "a verb about the chosen thing, read as words",

        // M82. Offered from the what's-next card beside its twin, where both are read as sentences.
        [ActionId.GrowToFit] = "offered as a sentence on the what's-next card, never hunted for",

        // M83. A menu toggle set once for a frame and then left alone, like the other flow verbs.
        [ActionId.ToggleTwoColumns] = "a flow verb read as words, beside the others that carry none",
        [ActionId.ToggleLocked] = "a verb about the chosen thing, read as words",
        [ActionId.ExportPagePicture] = "an export beside the others, none of which carries a glyph",
        [ActionId.ToggleBorder] = "the result is visible on the page; a glyph would restate the words",
        [ActionId.ToggleShade] = "the result is visible on the page; a glyph would restate the words",

        // M78. A toggle in a menu, decided once when the newsletter is set up and then left
        // alone — the same reasoning as the other three view toggles, none of which carries one.
        [ActionId.ShowPageFooter] = "a menu toggle set once, not a button hunted for",

        // M77. Reached from a menu, at a moment when the user is being TOLD where it is rather
        // than looking for it — the crash card offers the same thing, and nobody hunts the Help
        // menu for a picture of a problem.
        [ActionId.SaveProblemReport] = "reached when the app has just offered it, never hunted for",
        [ActionId.About] = "read once, never hunted for",
        [ActionId.FontLicences] = "read once, never hunted for",
        [ActionId.Licence] = "read once, never hunted for",

        // M51. Every glyph that could mean "look it over" — an eye, a tick, a magnifier — already
        // means something else here or would be read as "this is finished", which is the one thing
        // the review must not promise before it has run.
        [ActionId.ReviewNewsletter] = "no symbol means 'go through it with me' without over-promising",

        // M52. Every spell-check glyph in circulation is the letters ABC with a tick, which is
        // two abstractions deep for a reader who is being asked to trust it.
        [ActionId.CheckSpelling] = "the usual ABC-and-a-tick means nothing to somebody meeting it cold",

        // M58. A loudspeaker would be a lie on a machine with no voice, which is exactly the
        // machine where this command matters most.
        [ActionId.ReadAloud] = "a loudspeaker would promise sound this computer may not have",

        // M59. A calendar glyph would say "a date", and this is about one particular old
        // newsletter; the sentence names it and no picture can.
        [ActionId.ShowLastYear] = "a calendar would say 'a date', not 'last year's September'",
        [ActionId.ShowSpelling] = "a diagnostic overlay; no picture explains it",

        // M53. The draft copy would wear export-pdf's glyph, which is the whole confusion it
        // exists to remove; a printer glyph beside it would then be the only thing saying which
        // of the two is which.
        [ActionId.ExportDraftPdf] = "would wear export-pdf's glyph, which is the confusion it removes",
        [ActionId.PrintPdf] = "offered on a card straight after the export, where the words are the point",

        // M56. An envelope would promise that TrestleBoard sends the mail, which it does not and
        // must not - it hands the message to the program the user already has.
        [ActionId.SendIt] = "an envelope would promise the app sends it, which it does not",

        // M57. Both would wear the save disk, which already means the newsletter itself - and
        // "save the newsletter" and "save its layout for next year" are the two things that must
        // not be confused with each other.
        [ActionId.SaveAsTemplate] = "would wear the save disk, which already means the newsletter",
        [ActionId.ManageTemplates] = "a list of names; the names are the point",

        // M54. There is no picture of "a memorial notice" that is not either grim or glib, and the
        // list this opens names each paragraph in the words a person would use for it.
        [ActionId.InsertPhrase] = "no glyph for a memorial is either dignified or clear",
        [ActionId.SavePhrase] = "would wear the save disk, which already means the newsletter",
        [ActionId.ShowExampleIssue] = "read once, never hunted for",

        // M63. A question mark is the obvious glyph and it is the wrong one: it is also what this
        // app puts beside anything it is unsure of, and the one place help must not look is
        // uncertain. The words "How do I…?" are already the clearest possible label for it.
        // M64. A box glyph would be the obvious one and it would say "archive" — a thing you put
        // away. This is a thing you hand to somebody, and the sentence is what carries that.
        [ActionId.PackUpForSuccessor] = "reached twice in a decade, and never by hunting for a picture",
        [ActionId.BringInAPack] = "reached twice in a decade, and never by hunting for a picture",
        // M66. A document-with-arrow glyph is what every other program uses for "import", and this
        // audience reads that shape as "download". The sentence is unambiguous and the glyph is not.
        [ActionId.BringInWriting] = "the usual import arrow reads as download to this audience",
        [ActionId.HowDoI] = "the words are shorter and plainer than any glyph for them",
        [ActionId.ShowTheTour] = "reached from the Help menu once a decade, never hunted for",
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
