namespace TrestleBoard.Editing.Actions;

/// <summary>
/// Every action the app can perform, named once (PLAN.md §11 M11). A string constant rather than an
/// enum because these names cross into the App layer's handler map, the keyboard table, the panel and
/// the tests, and a stable spelling that shows up verbatim in a failure message is worth more here
/// than a compiler-checked ordinal.
/// </summary>
public static class ActionId
{
    // ---- The newsletter itself ----------------------------------------------------------------
    public const string Open = "newsletter.open";
    public const string OpenSample = "newsletter.openSample";
    public const string NewFromTemplate = "newsletter.newFromTemplate";
    public const string StartFromLastMonth = "newsletter.startFromLastMonth";
    public const string ExportPdf = "newsletter.exportPdf";
    public const string Exit = "newsletter.exit";

    // ---- Edit ---------------------------------------------------------------------------------
    public const string Undo = "edit.undo";
    public const string Redo = "edit.redo";
    public const string Cut = "edit.cut";
    public const string Copy = "edit.copy";
    public const string Paste = "edit.paste";
    public const string SelectAll = "edit.selectAll";

    // ---- Text ---------------------------------------------------------------------------------
    public const string Bold = "text.bold";
    public const string Italic = "text.italic";
    public const string ParagraphStyle = "text.paragraphStyle";

    // ---- Fonts and sizes (M14) ------------------------------------------------------------------
    public const string FontsAndStyles = "text.fontsAndStyles";
    public const string BiggerText = "text.bigger";
    public const string SmallerText = "text.smaller";
    public const string FontJustHere = "text.fontJustHere";
    public const string ClearFontOverride = "text.clearFontOverride";

    // ---- Putting things on the page -----------------------------------------------------------
    public const string AddTextFrame = "insert.textFrame";
    public const string InsertPhoto = "insert.photo";
    public const string InsertOfficers = "insert.officersTable";
    public const string InsertBirthdays = "insert.birthdayList";
    public const string InsertCommittees = "insert.committeeList";
    public const string InsertDistrictCalendar = "insert.districtCalendar";
    public const string InsertEventCard = "insert.eventCard";
    public const string InsertCoverBanner = "insert.coverBanner";

    // ---- The selected thing -------------------------------------------------------------------
    public const string DeleteFrame = "item.delete";
    public const string EditWidget = "item.edit";
    public const string EditWidgetList = "item.editList";
    public const string FitToContents = "item.fitToContents";

    /// <summary>M13: fill the birthday list in from the lodge address book.</summary>
    public const string SyncBirthdays = "item.syncBirthdays";

    // ---- Pictures -----------------------------------------------------------------------------
    public const string FixPhoto = "picture.fix";
    public const string AdjustPhoto = "picture.adjust";

    /// <summary>M18: put a picture into a frame that is already on the page, or swap the one in it.</summary>
    public const string ReplacePicture = "picture.replace";

    /// <summary>M18: the words a screen reader says instead of showing the picture.</summary>
    public const string DescribePicture = "picture.altText";

    /// <summary>M18: the words printed under the picture.</summary>
    public const string CaptionPicture = "picture.caption";

    /// <summary>M18: take the edges off the picture without touching the original file.</summary>
    public const string TrimPicture = "picture.trim";

    // ---- How text flows -----------------------------------------------------------------------
    public const string ToggleWrap = "flow.wrap";
    public const string LinkFrames = "flow.link";
    public const string UnlinkFrames = "flow.unlink";
    public const string AutoFlow = "flow.auto";

    // ---- Arranging ----------------------------------------------------------------------------
    public const string BringForward = "arrange.bringForward";
    public const string SendBackward = "arrange.sendBackward";
    public const string BringToFront = "arrange.bringToFront";
    public const string SendToBack = "arrange.sendToBack";

    // ---- Pages --------------------------------------------------------------------------------
    public const string NextPage = "page.next";
    public const string PreviousPage = "page.previous";
    public const string AddPage = "page.add";
    public const string RemovePage = "page.remove";
    public const string MovePageEarlier = "page.moveEarlier";
    public const string MovePageLater = "page.moveLater";

    // ---- Looking at it ------------------------------------------------------------------------
    public const string ZoomIn = "view.zoomIn";
    public const string ZoomOut = "view.zoomOut";
    public const string ActualSize = "view.actualSize";
    public const string FitPage = "view.fitPage";
    public const string Settings = "view.settings";
    public const string NextRegion = "view.nextRegion";
    public const string PreviousRegion = "view.previousRegion";
    public const string ToggleActionPanel = "view.toggleActionPanel";

    /// <summary>M14: underline the text whose font was changed by hand. Off by default.</summary>
    public const string ShowFontChanges = "view.showFontChanges";

    // ---- The address book (M12) ---------------------------------------------------------------
    public const string ShowPeople = "people.show";
    public const string ImportPeople = "people.import";
    public const string ExportPeople = "people.export";
    public const string UndoPeopleChange = "people.undo";
    public const string RestorePeople = "people.restore";

    // ---- Help ---------------------------------------------------------------------------------
    public const string CheckForUpdates = "help.checkForUpdates";
    public const string About = "help.about";

    /// <summary>M14: the OFL text for every bundled family, as the licence requires we ship it.</summary>
    public const string FontLicences = "help.fontLicences";

    /// <summary>
    /// M15: the five-page example issue. It has been the richest fixture in the repository since M8
    /// and was reachable from no menu item at all — a whole finished newsletter nobody could look
    /// at, which is exactly what someone opening this app for the first time wants to see.
    /// </summary>
    public const string ShowExampleIssue = "help.exampleIssue";
}
