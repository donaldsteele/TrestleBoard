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

    /// <summary>
    /// M24: writes the newsletter back to its own file. The app shipped through v0.9.1 with no such
    /// command at all — autosave held a crash snapshot that a clean exit then deleted, so a normal
    /// day's work could not be kept. Everything else in this milestone exists to serve this one.
    /// </summary>
    public const string Save = "newsletter.save";

    /// <summary>M24: writes it to a file the user picks, and that file becomes its home.</summary>
    public const string SaveAs = "newsletter.saveAs";

    /// <summary>
    /// M39: hands back one of the copies the rotating ring keeps beside the user's own file
    /// (PLAN.md §4). Autosave answers "the power went off"; this answers "I did that on purpose and
    /// now I want it back", which no crash snapshot can.
    /// </summary>
    public const string RestoreDocument = "newsletter.restore";

    /// <summary>
    /// M51: the newsletter walked top to bottom and turned into a short list of questions. It sits
    /// beside <see cref="ExportPdf"/> because that is where the committee's month reaches it, and
    /// it never stands between anyone and the PDF.
    /// </summary>
    public const string ReviewNewsletter = "newsletter.review";

    /// <summary>
    /// M52: the word-by-word pass, one word to a screen. A station of M51's review as well as a
    /// command of its own — PLAN.md scheduled M51 first for exactly that reason.
    /// </summary>
    public const string CheckSpelling = "newsletter.checkSpelling";

    /// <summary>
    /// M58: read the newsletter back one sentence at a time. Where no voice answers, the same walk
    /// runs silently — errors the eye slides over, the ear catches, and one sentence at a time with
    /// everything else out of the way catches a good many of the rest.
    /// </summary>
    public const string ReadAloud = "newsletter.readAloud";

    /// <summary>
    /// M59: last year's issue of this month, beside the one being written. The annual rhythm the
    /// product ignored — the picnic, the awards night, the installation notice.
    /// </summary>
    public const string ShowLastYear = "newsletter.lastYear";

    public const string ExportPdf = "newsletter.exportPdf";

    /// <summary>
    /// M81: one page as a picture, for the lodge's Facebook group or a text message. The PDF is
    /// what goes in the email; a picture is what goes anywhere else.
    /// </summary>
    public const string ExportPagePicture = "newsletter.exportPicture";

    /// <summary>
    /// M53: the same PDF with "DRAFT — not for sending" across every page. Before this, the copy
    /// the Master reviewed and the copy sixty people received differed only in the sender's memory.
    /// </summary>
    public const string ExportDraftPdf = "newsletter.exportDraft";

    /// <summary>
    /// M53: hand the finished PDF to whatever this computer already prints PDFs with. No print
    /// subsystem of our own — the hand-off IS the design.
    /// </summary>
    public const string PrintPdf = "newsletter.print";

    /// <summary>
    /// M56: hands the newsletter to the user's own mail program with the email group in the blind
    /// copy line. No accounts, no SMTP — the workflow used to end at a file on a disk.
    /// </summary>
    public const string SendIt = "newsletter.send";

    /// <summary>
    /// M57: keep this newsletter's layout as a starting point for a future issue. The manifest has
    /// carried an <c>isTemplate</c> flag since M2 that nothing ever set.
    /// </summary>
    public const string SaveAsTemplate = "newsletter.saveAsTemplate";

    /// <summary>M57: rename, remove, or hand one of your templates to a successor.</summary>
    public const string ManageTemplates = "newsletter.myTemplates";

    /// <summary>
    /// M75: which month and year this newsletter is for.
    ///
    /// <para>It opens the cover heading's own wizard on its first screen — the owner's ruling, and
    /// the right one: a second place to set the date is a second place to forget. This id exists so
    /// the question has a name every start path can call, every command that needs an issue date can
    /// point a <c>RemedyId</c> at, and the user can find again when they answer it wrongly.</para>
    /// </summary>
    public const string SetIssueDate = "newsletter.issueDate";
    public const string Exit = "newsletter.exit";

    // ---- Edit ---------------------------------------------------------------------------------
    public const string Undo = "edit.undo";
    public const string Redo = "edit.redo";
    public const string Cut = "edit.cut";
    public const string Copy = "edit.copy";
    public const string Paste = "edit.paste";
    public const string SelectAll = "edit.selectAll";

    /// <summary>
    /// M49: choose every frame on this page. The keyboard's answer to the marquee, which could only
    /// ever be drawn with a mouse — and in this app what a marquee is nearly always FOR is lining
    /// several things up at once, which is exactly what this hands to the align commands.
    /// </summary>
    public const string SelectAllFrames = "edit.selectAllFrames";

    /// <summary>
    /// M50: keep what is chosen and add the next thing on the page. The keyboard's answer to
    /// Shift+click, which is the last of §14.3's keyboard-coverage findings.
    /// </summary>
    public const string AddNextToSelection = "edit.alsoChooseNext";

    /// <summary>M50: the same, walking the other way.</summary>
    public const string AddPreviousToSelection = "edit.alsoChoosePrevious";

    /// <summary>M21: look for words anywhere in the newsletter's writing.</summary>
    public const string Find = "edit.find";

    /// <summary>M21: the same window, with the box for what to put there instead.</summary>
    public const string Replace = "edit.replace";

    // ---- Text ---------------------------------------------------------------------------------
    public const string Bold = "text.bold";
    public const string Italic = "text.italic";
    public const string ParagraphStyle = "text.paragraphStyle";

    /// <summary>
    /// M61: make the paragraph a point in a list, or put it back. Never "unordered" — §6.
    /// </summary>
    public const string BulletList = "text.bulletList";

    /// <summary>M61: the same, numbered. Never "ordered".</summary>
    public const string NumberList = "text.numberList";

    // ---- Fonts and sizes (M14) ------------------------------------------------------------------
    public const string FontsAndStyles = "text.fontsAndStyles";
    public const string BiggerText = "text.bigger";
    public const string SmallerText = "text.smaller";
    public const string FontJustHere = "text.fontJustHere";
    public const string ClearFontOverride = "text.clearFontOverride";

    // ---- Putting things on the page -----------------------------------------------------------
    public const string AddTextFrame = "insert.textFrame";

    /// <summary>
    /// M79: a line right across the page, under whatever is chosen.
    ///
    /// <para>Not a shape tool. A line the user draws is a line the user drags by accident, and
    /// dragging a hairline back to level is exactly the fine-motor task §6 exists to avoid.</para>
    /// </summary>
    public const string AddRule = "insert.rule";

    /// <summary>
    /// M54: a ready-made paragraph for a moment that is hard to write — a memorial, a
    /// sickness-and-distress entry. Blank-page paralysis is worst under grief.
    /// </summary>
    public const string InsertPhrase = "insert.phrase";

    /// <summary>M54: keep the words you have just written, to reach for again next time.</summary>
    public const string SavePhrase = "insert.savePhrase";
    public const string InsertPhoto = "insert.photo";
    public const string InsertOfficers = "insert.officersTable";
    public const string InsertBirthdays = "insert.birthdayList";
    public const string InsertCommittees = "insert.committeeList";
    public const string InsertDistrictCalendar = "insert.districtCalendar";
    public const string InsertEventCard = "insert.eventCard";
    public const string InsertCoverBanner = "insert.coverBanner";

    /// <summary>
    /// M60: the seventh widget — a table of the user's own, for content that is table-shaped but is
    /// not one of the six lists TrestleBoard fills in.
    /// </summary>
    public const string InsertSimpleList = "insert.simpleList";

    // ---- The selected thing -------------------------------------------------------------------
    public const string DeleteFrame = "item.delete";
    public const string EditWidget = "item.edit";
    public const string EditWidgetList = "item.editList";
    public const string FitToContents = "item.fitToContents";

    /// <summary>M13: fill the birthday list in from the lodge address book.</summary>
    public const string SyncBirthdays = "item.syncBirthdays";

    /// <summary>M19: fill the officers table in from the lodge address book.</summary>
    public const string SyncOfficers = "item.syncOfficers";

    // ---- Pictures -----------------------------------------------------------------------------
    public const string FixPhoto = "picture.fix";
    public const string AdjustPhoto = "picture.adjust";

    /// <summary>M18: put a picture into a frame that is already on the page, or swap the one in it.</summary>
    public const string ReplacePicture = "picture.replace";

    /// <summary>M18: the words a screen reader says instead of showing the picture.</summary>
    public const string DescribePicture = "picture.altText";

    /// <summary>M18: the words printed under the picture.</summary>
    public const string CaptionPicture = "picture.caption";

    /// <summary>
    /// M18: take the edges off the picture without touching the original file.
    ///
    /// <para><b>Retired as a command at M38 and kept only as a name.</b> It was a second entry
    /// point onto the SAME window as <see cref="AdjustPhoto"/> — <c>TrimPictureAsync</c> was
    /// <c>ShowAdjustWindowAsync(startOnTrim: true)</c>, identical dialog, different focus target —
    /// and two names for one door was most of what the review's "four near-synonym picture verbs"
    /// meant (§14.4). The trim sliders still carry their own heading inside that window.</para>
    ///
    /// <para>The constant stays because a `.tboard` never referenced it and no user setting holds
    /// it, but deleting an id outright makes the history unreadable to the next person who greps
    /// for "picture.trim" and finds nothing.</para>
    /// </summary>
    public const string TrimPicture = "picture.trim";

    /// <summary>M22: pan/recentre the already-sized crop window without changing its size.</summary>
    public const string PositionPicture = "picture.position";

    /// <summary>M23: hides the stretched-picture note until the frame changes shape again.</summary>
    public const string DismissCropNotice = "picture.dismissCropNotice";

    // ---- How text flows -----------------------------------------------------------------------
    public const string ToggleWrap = "flow.wrap";
    public const string LinkFrames = "flow.link";
    public const string UnlinkFrames = "flow.unlink";
    public const string AutoFlow = "flow.auto";

    /// <summary>
    /// M82: makes the chosen box taller until the writing fits, stopping at the bottom margin. The
    /// other half of the answer to "about forty words too long" — this one gives the writing more
    /// room where it is, and <see cref="AutoFlow"/> gives it a room of its own.
    /// </summary>
    public const string GrowToFit = "flow.growToFit";

    // ---- Arranging ----------------------------------------------------------------------------
    public const string BringForward = "arrange.bringForward";
    public const string SendBackward = "arrange.sendBackward";
    public const string BringToFront = "arrange.bringToFront";
    public const string SendToBack = "arrange.sendToBack";

    // ---- Lining things up (M21) ---------------------------------------------------------------
    // Eight commands and no keyboard chords: eight more unmemorable shortcuts would buy nothing,
    // and the Arrange menu's own mnemonics are the keyboard path (PLAN.md §6).
    public const string AlignLeft = "arrange.alignLeft";
    public const string AlignCentres = "arrange.alignCentres";
    public const string AlignRight = "arrange.alignRight";
    public const string AlignTop = "arrange.alignTop";
    public const string AlignMiddles = "arrange.alignMiddles";
    public const string AlignBottom = "arrange.alignBottom";
    public const string DistributeHorizontally = "arrange.distributeAcross";
    public const string DistributeVertically = "arrange.distributeDown";

    // ---- Pages --------------------------------------------------------------------------------
    public const string NextPage = "page.next";
    public const string PreviousPage = "page.previous";
    public const string AddPage = "page.add";
    public const string RemovePage = "page.remove";
    public const string MovePageEarlier = "page.moveEarlier";
    public const string MovePageLater = "page.moveLater";

    /// <summary>
    /// M76 (g): show a page the user picked, rather than the one after this one.
    ///
    /// <para><b>The catalog's first parameterised action, and the parameter is deliberately not
    /// here.</b> "Which page" is carried in the pressed control's <c>Tag</c> beside this id — see
    /// <c>ActionTarget</c> in the App layer — because the alternative, one <see cref="ActionId"/>
    /// per page, would make the size of the catalog depend on the newsletter that happens to be
    /// open (docs/M76-spec.md §6). What this entry means is therefore "can you jump to a page at
    /// all", which is a true and useful thing to be able to refuse: with a one-page newsletter
    /// there is nowhere to jump to, and the refusal says so in words.</para>
    /// </summary>
    public const string GoToPage = "page.goTo";

    /// <summary>
    /// M78: the line along the bottom of every page — the lodge, the issue, and "page 3 of 6".
    ///
    /// <para>A toggle over the whole newsletter rather than the current page: a cover with a footer
    /// and inside pages without one looks like a mistake, and nobody asked for the two to differ.
    /// Every fact it prints is already known, so there is nothing to fill in.</para>
    /// </summary>
    public const string ShowPageFooter = "page.footer";

    // ---- Looking at it ------------------------------------------------------------------------
    public const string ZoomIn = "view.zoomIn";
    public const string ZoomOut = "view.zoomOut";
    public const string ActualSize = "view.actualSize";
    public const string FitPage = "view.fitPage";
    public const string Settings = "view.settings";
    public const string NextRegion = "view.nextRegion";
    public const string PreviousRegion = "view.previousRegion";
    public const string ToggleActionPanel = "view.toggleActionPanel";

    /// <summary>
    /// M76 (g): show or hide the row of small pages down the left-hand side.
    ///
    /// <para>The same shape as <see cref="ToggleActionPanel"/>, because it is the same promise: a
    /// strip of chrome the user can put away, which folds itself away below the same window width
    /// rather than squeezing the page out (docs/M76-spec.md §6).</para>
    /// </summary>
    public const string TogglePageRail = "view.togglePageRail";

    /// <summary>M14: underline the text whose font was changed by hand. Off by default.</summary>
    public const string ShowFontChanges = "view.showFontChanges";

    /// <summary>
    /// M52: the dotted underline under a word the checker does not know. ON by default, unlike the
    /// two marks above — those are for hunting down a setting, and this one is about the reader's
    /// newsletter. A misspelling nobody is shown is one that prints.
    /// </summary>
    public const string ShowSpelling = "view.showSpelling";

    /// <summary>
    /// M47: draw the edge the printed page keeps clear, so somebody can see whether a frame they
    /// dragged is inside it. Off by default — a line the user did not ask for is noise.
    /// </summary>
    public const string ShowMargins = "view.showMargins";

    // ---- The address book (M12) ---------------------------------------------------------------
    public const string ShowPeople = "people.show";
    public const string ImportPeople = "people.import";
    public const string ExportPeople = "people.export";
    /// <summary>
    /// M79: a line round the chosen box. <c>FrameStyleRef</c> has been on every block since M1 and
    /// no command ever reached it, so "put a box round the fish-fry notice" — one of the two
    /// layout requests a committee actually makes — could not be done at all.
    /// </summary>
    /// <summary>
    /// M81: a copy of the chosen thing, a little down and across. How a novice makes a second event
    /// card without running the wizard again — copy has meant WORDS since M4.
    /// </summary>
    public const string Duplicate = "item.duplicate";

    /// <summary>M81: the chosen thing stays where it is. Position and size only.</summary>
    public const string ToggleLocked = "item.lock";

    public const string ToggleBorder = "item.border";

    /// <summary>M79: a pale tint behind the chosen box, to mark a notice.</summary>
    public const string ToggleShade = "item.shade";

    public const string UndoPeopleChange = "people.undo";
    public const string RestorePeople = "people.restore";

    // ---- Help ---------------------------------------------------------------------------------
    public const string CheckForUpdates = "help.checkForUpdates";

    /// <summary>
    /// M77: writes a file describing this installation and what it has just been asked to do, for
    /// the person who looks after TrestleBoard for the lodge.
    ///
    /// <para>It exists for the case where nothing crashed. A crash puts the same offer in front of
    /// the user by itself; this is for "it is doing something odd", which is the report this
    /// audience is far more likely to need and would otherwise have no way to make.</para>
    /// </summary>
    public const string SaveProblemReport = "help.problemReport";
    public const string About = "help.about";

    /// <summary>M14: the OFL text for every bundled family, as the licence requires we ship it.</summary>
    public const string FontLicences = "help.fontLicences";

    /// <summary>
    /// M68: the application's own licence, which §13 recorded as missing from M15 until the owner
    /// decided on 2026-08-08. Separate from <see cref="FontLicences"/> on purpose — the fonts are
    /// somebody else's work under somebody else's terms, and running the two together would blur
    /// which permission comes from where.
    /// </summary>
    public const string Licence = "help.licence";

    /// <summary>
    /// M15: the five-page example issue. It has been the richest fixture in the repository since M8
    /// and was reachable from no menu item at all — a whole finished newsletter nobody could look
    /// at, which is exactly what someone opening this app for the first time wants to see.
    /// </summary>
    public const string ShowExampleIssue = "help.exampleIssue";

    /// <summary>
    /// M67: a page of a PDF, brought in as a picture. Lodges receive finished flyers and notices as
    /// PDF — the Grand Lodge announcement, the district calendar page — and the committee wants the
    /// page as it is.
    /// </summary>
    public const string BringInPdfPage = "insert.pdfPage";

    /// <summary>
    /// M66: reads a Word document or a text file and brings the writing in. Committee members email
    /// articles; the round trip through Word and the clipboard is where the formatting shrapnel
    /// comes from.
    /// </summary>
    public const string BringInWriting = "insert.writingFromFile";

    /// <summary>
    /// M65: the emblem shelf. The craft's symbols are bundled with the app and drawn by it, so a
    /// committee never again has to find, vet and import a picture of the square and compasses.
    /// </summary>
    public const string InsertEmblem = "insert.emblem";

    /// <summary>
    /// M64: writes the address book, the templates, the phrase shelf, the personal dictionary and
    /// the settings into one file for whoever takes over. Committee turnover is the existential risk
    /// for a volunteer lodge, and until this everything but a single template died with the laptop.
    /// </summary>
    public const string PackUpForSuccessor = "everything.packUp";

    /// <summary>
    /// M64: reads a pack back in on the new machine, item by item and asking before it replaces
    /// anything that is already here.
    /// </summary>
    public const string BringInAPack = "everything.bringInAPack";

    /// <summary>
    /// M63: the "How do I…?" window. Its index is generated from this very catalog, so adding a
    /// command adds its answer — the help cannot fall behind the app because there is nowhere for a
    /// stale sentence to live.
    /// </summary>
    public const string HowDoI = "help.howDoI";

    /// <summary>
    /// M63: the five-screen tour, shown once on a new installation and afterwards only when asked
    /// for from here. Somebody who skipped it in their first minute needs a way back to it that is
    /// not reinstalling the app.
    /// </summary>
    public const string ShowTheTour = "help.tour";
}
