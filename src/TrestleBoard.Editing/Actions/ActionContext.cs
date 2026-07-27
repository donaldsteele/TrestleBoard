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

    // ---- The selection ------------------------------------------------------------------------

    public SelectionKind Selection { get; init; } = SelectionKind.None;

    public string? SelectedBlockId { get; init; }

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

    /// <summary>A generated birthday list was made for a month that is no longer the issue month (M13).</summary>
    public bool BirthdayListIsStale { get; init; }

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
