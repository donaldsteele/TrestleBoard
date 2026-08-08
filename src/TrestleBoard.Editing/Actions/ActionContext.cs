namespace TrestleBoard.Editing.Actions;

/// <summary>What kind of thing is selected on the page right now.</summary>
public enum SelectionKind
{
    /// <summary>Nothing is selected and no text session is open.</summary>
    None,

    /// <summary>The caret is in a story — the user is typing.</summary>
    Text,

    /// <summary>A text frame is selected as an object (not being typed into).</summary>
    TextFrame,

    /// <summary>A picture frame.</summary>
    Photo,

    /// <summary>One of the six built-in widgets.</summary>
    Widget,

    /// <summary>A rule, box or ornament.</summary>
    Shape,
}

/// <summary>
/// A flat snapshot of everything the catalog needs to decide what is possible (PLAN.md §11 M11).
///
/// Flat and immutable on purpose. The old code asked six controllers thirty questions scattered
/// across two chrome-refresh methods; a snapshot taken once per change means the panel, the menu and
/// the context flyout cannot disagree with each other, and a test can pose any situation in the app
/// by writing down eight booleans instead of building a document.
/// </summary>
public sealed record ActionContext
{
    /// <summary>No newsletter open — the state the app starts in.</summary>
    public static ActionContext Empty { get; } = new();

    // ---- The document -------------------------------------------------------------------------

    public bool HasDocument { get; init; }

    public int PageCount { get; init; }

    public int PageIndex { get; init; }

    /// <summary>True once anything has been carried forward or opened that could become next month's.</summary>
    public bool CanStartFromLastMonth { get; init; }

    /// <summary>Somewhere in the newsletter, text does not fit its frame.</summary>
    public bool HasOversetText { get; init; }

    /// <summary>M49: the page being looked at has at least one frame on it to take hold of.</summary>
    public bool PageHasFrames { get; init; }

    /// <summary>
    /// M24: the newsletter has been edited since it was last written to its file. Deliberately
    /// separate from <see cref="CanUndo"/> — a document can have a full undo stack and still be
    /// saved, and one that was undone all the way back to its opening state is still dirty on disk
    /// terms only if something was written in between.
    /// </summary>
    public bool HasUnsavedChanges { get; init; }

    /// <summary>M24: this newsletter has a file of its own, so Save knows where to put it.</summary>
    public bool DocumentHasFile { get; init; }

    /// <summary>
    /// M24: the file name alone — "September 2026.tboard" — for the sentences that tell the user
    /// where their work is. Never the full path: a folder full of directory names is not what
    /// someone reading a status bar needs, and PLAN.md §0 keeps real paths out of anything shown
    /// or captured.
    /// </summary>
    public string? DocumentFileName { get; init; }

    /// <summary>
    /// M39: at least one generation of the rotating <c>.bak</c> ring survives beside the user's own
    /// file, so there is an earlier version to go back to (PLAN.md §4).
    /// </summary>
    public bool DocumentHasEarlierVersions { get; init; }

    // ---- The selection ------------------------------------------------------------------------

    public SelectionKind Selection { get; init; } = SelectionKind.None;

    public string? SelectedBlockId { get; init; }

    /// <summary>
    /// How many things are chosen on the page (M21). One for an ordinary selection, more after
    /// Shift+click or a marquee drag, zero when nothing is chosen. It is what the align and
    /// distribute rules are written against, and it is deliberately a count rather than a list:
    /// nothing in the catalog needs to know WHICH blocks, only how many.
    /// </summary>
    public int SelectionCount { get; init; }

    /// <summary>The caret is in a story: typing goes somewhere.</summary>
    public bool IsEditingText { get; init; }

    /// <summary>Some text is highlighted, so cut and copy have something to work on.</summary>
    public bool HasTextSelection { get; init; }

    /// <summary>The selected frame is a text frame (selected as an object, or being typed into).</summary>
    public bool SelectionIsTextFrame { get; init; }

    /// <summary>Text is wrapping around the selected block.</summary>
    public bool SelectionWraps { get; init; }

    /// <summary>The selected frame already continues into another one.</summary>
    public bool SelectionIsLinked { get; init; }

    /// <summary>The selected frame holds more text than fits.</summary>
    public bool SelectionIsOverset { get; init; }

    /// <summary>There is somewhere for the overflowing text of the selected frame to go.</summary>
    public bool CanAutoFlow { get; init; }

    // ---- Pictures (M18) -----------------------------------------------------------------------

    /// <summary>
    /// The selected picture frame has nothing in it yet — the state every photo template ships in.
    /// It is what makes "Put a picture here…" and "Swap this picture…" the same command wearing two
    /// titles, and what stops the app offering to brighten a grey rectangle.
    /// </summary>
    public bool SelectedPictureIsEmpty { get; init; }

    /// <summary>The selected picture already has a caption printed under it.</summary>
    public bool SelectedPictureHasCaption { get; init; }

    /// <summary>Somewhere in the newsletter a picture frame is still empty (the "what's next" flag).</summary>
    public bool HasPicturePlaceholder { get; init; }

    /// <summary>
    /// M23: the selected picture's frame changed shape enough since its crop was set that it may
    /// look stretched. Never true for a frame that has never been cropped.
    /// </summary>
    public bool PictureCropIsStale { get; init; }

    /// <summary>The plain-language sentence for the notice above, or null when there is nothing to say.</summary>
    public string? CropStaleNote { get; init; }

    // ---- Fonts (M14) --------------------------------------------------------------------------

    /// <summary>The writing at the caret carries a "just here" font rather than its role's.</summary>
    public bool SelectionUsesFontOverride { get; init; }

    /// <summary>
    /// "This text uses EB Garamond instead of the Body text font." Null when there is nothing to
    /// say. One of the three ways the user can tell text has been overridden — the other two are
    /// the View overlay and the styles window's footer.
    /// </summary>
    public string? FontOverrideNote { get; init; }

    /// <summary>How many pieces of text in the whole newsletter carry a "just here" font.</summary>
    public int FontOverrideCount { get; init; }

    // ---- Widgets ------------------------------------------------------------------------------

    /// <summary>The widget type id, e.g. "officersTable"; null when the selection is not a widget.</summary>
    public string? WidgetTypeId { get; init; }

    /// <summary>What the widget is called in the interface, e.g. "Lodge officers".</summary>
    public string? WidgetDisplayName { get; init; }

    /// <summary>False when the widget was made by a newer TrestleBoard than this one.</summary>
    public bool CanEditWidget { get; init; }

    /// <summary>The widget has a list in it, so the big-row grid editor is worth offering.</summary>
    public bool WidgetHasListEditor { get; init; }

    // ---- The undo stack -----------------------------------------------------------------------

    public bool CanUndo { get; init; }

    public bool CanRedo { get; init; }

    /// <summary>"Move photo" — the tail of "Undo move photo".</summary>
    public string? UndoDescription { get; init; }

    public string? RedoDescription { get; init; }

    // ---- Facts only the shell knows -----------------------------------------------------------

    /// <summary>A PDF has been exported since this newsletter was opened.</summary>
    public bool ExportedPdfThisSession { get; init; }

    /// <summary>An article still holds the "write this month's article here" prompt.</summary>
    public bool HasUnwrittenArticle { get; init; }

    /// <summary>The cover heading is on the page but its meeting date has not been filled in.</summary>
    public bool CoverDateMissing { get; init; }

    /// <summary>The address book is empty while a widget that names people is on the page (M12).</summary>
    public bool RosterEmptyButNeeded { get; init; }

    /// <summary>
    /// A generated birthday list on the page no longer matches the address book — either the issue
    /// has moved to another month, or somebody's details changed (M13). Saying so is all this does:
    /// staleness never mutates the document.
    /// </summary>
    public bool BirthdayListIsStale { get; init; }

    /// <summary>How many people in the address book have a birthday in this issue's month (M13).</summary>
    public int RosterBirthdaysThisMonth { get; init; }

    /// <summary>
    /// An officers table on the page was filled in from the address book and no longer matches it
    /// (M19). Saying so is all this does — M13's rule carries over unchanged: staleness never
    /// mutates the document.
    /// </summary>
    public bool OfficersTableIsStale { get; init; }

    /// <summary>How many of the twelve offices the address book could fill in (M19).</summary>
    public int RosterOfficesFilledIn { get; init; }

    /// <summary>
    /// The selected widget was filled in from the address book, and this is when — "14 July 2026",
    /// or an empty string when the stamp cannot be read. Null when the selection was typed by hand,
    /// which is what makes the panel caption honest rather than decorative (M19).
    /// </summary>
    public string? SelectionFilledInFromRoster { get; init; }

    // ---- The address book (M12) ---------------------------------------------------------------

    /// <summary>How many people the address book holds.</summary>
    public int RosterCount { get; init; }

    /// <summary>There is one address-book change to take back. Deliberately separate from
    /// <see cref="CanUndo"/>: Ctrl+Z never crosses the roster/document boundary.</summary>
    public bool RosterCanUndo { get; init; }

    /// <summary>"Add A. Placeholder" — the tail of the People menu's undo item.</summary>
    public string? RosterUndoDescription { get; init; }

    /// <summary>The backup ring holds at least one earlier version to restore.</summary>
    public bool RosterHasEarlierVersions { get; init; }

    /// <summary>True when a block of some kind is selected as an object.</summary>
    public bool HasFrameSelection =>
        Selection is SelectionKind.TextFrame or SelectionKind.Photo
            or SelectionKind.Widget or SelectionKind.Shape;
}
