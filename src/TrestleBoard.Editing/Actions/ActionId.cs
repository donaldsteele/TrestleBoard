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

    /// <summary>M105: the lodge's name, what this newsletter is called, and when the lodge meets.</summary>
    public const string AboutThisNewsletter = "newsletter.about";

    /// <summary>M106: the newsletters this person actually had open, most recent first.</summary>
    public const string RecentNewsletters = "newsletter.recent";
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

    /// <summary>
    /// M102: a line under the words — the last of M86's three deliverables.
    /// </summary>
    public const string Underline = "text.underline";
    /// <summary>
    /// M96: which way the writing is lined up. `TextAlignment` and the shift arithmetic in the
    /// layout engine have been live since M1 with no command able to reach them.
    /// </summary>
    public const string AlignTextLeft = "text.alignLeft";

    public const string AlignTextCentre = "text.alignCentre";

    public const string AlignTextRight = "text.alignRight";

    /// <summary>M109: the chosen paragraphs pulled in from both sides, to set them apart.</summary>
    public const string PullItIn = "text.pullItIn";

    /// <summary>
    /// M99: what colour the writing is. `CharacterStyleDef.ColorArgb` has been plumbed end to end
    /// since M1 — resolver, adapter, shaper, both renderers — and nothing could set it.
    /// </summary>
    public const string TextColour = "text.colour";

    /// <summary>
    /// M103: end this line without ending the paragraph. U+2028 has been a mandatory break in the
    /// layout engine since M1 and nothing could put one in.
    /// </summary>
    public const string LineBreak = "text.lineBreak";

    /// <summary>M103: remember how this writing looks.</summary>
    public const string PickUpLook = "text.pickUpLook";

    /// <summary>M103: make the highlighted writing look like what was picked up.</summary>
    public const string PutLookDown = "text.putLookDown";

    /// <summary>M98: capitals, small letters, or one capital per word.</summary>
    public const string ChangeCase = "text.changeCase";

    /// <summary>M98: how many words are in this piece of writing.</summary>
    public const string WordCount = "text.wordCount";

    /// <summary>M98: a character the keyboard has no key for — a dash, a degree sign, a fraction.</summary>
    public const string InsertSymbol = "insert.symbol";

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
    /// <summary>
    /// M93: how spaced out the writing is, and whether paragraphs start pushed in.
    ///
    /// <para>Four fields the layout engine has honoured since M1 that no command could reach.</para>
    /// </summary>
    public const string WritingLook = "text.writingLook";

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
    /// M95: a plain box or panel. <c>ShapeKind.Box</c> has rendered since M2 and nothing could
    /// make one — the only producer of a shape was the rule.
    /// </summary>
    public const string AddBox = "insert.box";

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

    /// <summary>
    /// M84: this month on a grid, with the stated meeting already marked. The district calendar is
    /// a table of six lodges; this is one lodge's month laid out the way a wall calendar is.
    /// </summary>
    public const string InsertMonthCalendar = "insert.monthCalendar";
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

    /// <summary>
    /// M83: two columns in one box of writing, or back to one. <c>ColumnCount</c> has been in the
    /// document model and in the layout engine's input since M1 and was always 1.
    /// </summary>
    public const string ToggleTwoColumns = "flow.twoColumns";

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
    /// <summary>
    /// M100: a whole page copied, with everything on it. `item.duplicate` has copied one thing
    /// since M81; nothing has ever copied a page.
    /// </summary>
    public const string DuplicatePage = "page.duplicate";

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
    /// <summary>
    /// M97: the paper itself — its size and its four margins. Five fields with eight readers and,
    /// until now, no writer anywhere in the application.
    /// </summary>
    public const string PageSetup = "page.setup";

    public const string ShowPageFooter = "page.footer";

    /// <summary>M107: whether the front page is left out of the line along the bottom.</summary>
    public const string FooterNotOnFrontPage = "page.footerNotOnFront";

    // ---- Looking at it ------------------------------------------------------------------------
    public const string ZoomIn = "view.zoomIn";
    public const string ZoomOut = "view.zoomOut";
    public const string ActualSize = "view.actualSize";
    public const string FitPage = "view.fitPage";

    /// <summary>M108: the page as wide as the window, so the writing is as large as it can be.</summary>
    public const string FitWidth = "view.fitWidth";
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

    /// <summary>
    /// M91: takes the chosen things off this page and puts them on the next one, at the same spot,
    /// and follows them there.
    ///
    /// <para>The canvas shows one page at a time, so there is no dragging something over a page
    /// edge — and a long, precise drag is the gesture §6 exists to avoid for this audience anyway.
    /// A command it is.</para>
    /// </summary>
    public const string MoveToNextPage = "item.moveToNextPage";

    /// <summary>M91: the same, backwards.</summary>
    public const string MoveToPreviousPage = "item.moveToPreviousPage";

    /// <summary>
    /// M94: type exactly where the chosen thing goes and how big it is.
    ///
    /// <para>The accessible route to geometry — until this it was drag or arrow key only, and a
    /// long precise drag is what §6 exists to avoid.</para>
    /// </summary>
    public const string PositionAndSize = "item.positionAndSize";

    /// <summary>M81: the chosen thing stays where it is. Position and size only.</summary>
    public const string ToggleLocked = "item.lock";

    /// <summary>M95: what colour a drawn box or line is.</summary>
    public const string ShapeColours = "item.shapeColours";

    /// <summary>M104: what colour an emblem is drawn in.</summary>
    public const string EmblemColour = "item.emblemColour";

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
