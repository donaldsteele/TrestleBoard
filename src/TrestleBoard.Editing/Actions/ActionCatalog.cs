namespace TrestleBoard.Editing.Actions;

/// <summary>
/// The single list of everything the app can do, and the single place that decides whether each one
/// is possible right now (PLAN.md §11 M11).
///
/// Before this existed the answer was spread across roughly thirty <c>IsEnabled =</c> assignments in
/// two methods of the window's code-behind, and none of them carried a reason. The user saw controls
/// go grey and had no way to find out why. Everything here is a pure function of an
/// <see cref="ActionContext"/>, so the panel, the menu bar and the right-click flyout are fed from
/// one evaluation and cannot contradict each other — and every sentence the user will read is
/// testable without starting Avalonia.
/// </summary>
public static class ActionCatalog
{
    private const string NoNewsletter =
        "There is no newsletter open yet. Start one from a template, or open one you saved.";

    private const string ChooseSomething =
        "Nothing on the page is chosen. Click something on the page, or press Tab to step through it.";

    private const string NeedsPicture =
        "This needs a picture. Choose one on the page first.";

    /// <summary>
    /// M72. Four of the picture commands are refused on a drawing, and each refusal has to say
    /// something true rather than "this needs a picture" — a drawing plainly IS a picture to the
    /// person looking at the page, and being told otherwise about the thing they just chose reads
    /// as the app being confused. What is actually true is that these commands work on
    /// <i>photographs</i>: they crop, brighten and reposition pixels, and there are none here.
    /// </summary>
    private const string DrawingIsNotAPhotograph =
        "This is a drawing rather than a photograph, so there is nothing to crop or brighten. "
        + "It always shows all of itself, and it stays sharp at any size.";

    private const string EmptyPictureFrame =
        "There is no picture in this frame yet, so there is nothing to change. "
        + "Put one in first — double-clicking the frame on the page does the same thing.";

    /// <summary>
    /// M67. The one refusal in this app that is about the computer rather than about the
    /// newsletter, so it says what to do instead rather than what to do differently.
    /// </summary>
    private const string CannotReadPdfs =
        "This computer cannot read PDFs into the newsletter. Everything else works as usual — open "
        + "the PDF in another program, save the page as a picture, and use \"A picture\" instead.";

    private const string NeedsText =
        "Click into some writing first, then this changes the words you highlight.";

    private const string NeedsTextFrame =
        "This is about a frame of writing. Choose one on the page first.";

    private const string NeedsListItem =
        "This is about the lists TrestleBoard fills in for you, like the officers table. "
        + "Choose one on the page first.";

    /// <summary>M21: the two sentences that teach Shift+click, which is the only way to get here.</summary>
    private const string NeedsTwoThings =
        "This lines up two things or more. Choose one, then hold Shift and click the next one.";

    private const string NeedsThreeThings =
        "This shares the space out between three things or more. Choose one, then hold Shift and "
        + "click each of the others.";

    private const string NeedsBirthdayList =
        "This is about the birthday list. Choose one on the page first, or add one from the Insert menu.";

    private const string NeedsOfficersTable =
        "This is about the officers table. Choose one on the page first, or add one from the Insert menu.";

    /// <summary>
    /// The two widget type ids this project knows by name (M13, M19). <c>TrestleBoard.Widgets</c> is
    /// deliberately above this layer, so each is recognised by its stable id — the same string the
    /// document itself stores — rather than by a type reference that would invert the dependency.
    /// </summary>
    private const string BirthdayListTypeId = "birthdayList";

    private const string OfficersTableTypeId = "officersTable";

    private static readonly EditorAction[] AllActions =
    [
        // ---- The newsletter itself ------------------------------------------------------------
        new(ActionId.Open, "Open a newsletter…", "Opens a newsletter you saved earlier.",
            ActionGroup.Newsletter, "Ctrl+O"),
        new(ActionId.OpenSample, "Open the sample newsletter", "Shows an example issue to look around in.",
            ActionGroup.Newsletter),
        new(ActionId.NewFromTemplate, "Start from a template…", "Begins a new newsletter from a ready-made layout.",
            ActionGroup.Newsletter),
        new(ActionId.StartFromLastMonth, "Start from last month",
            "Copies this newsletter forward to next month and clears the articles.", ActionGroup.Newsletter),
        new(ActionId.Save, "Save this newsletter", "Keeps your work in its file so you can come back to it.",
            ActionGroup.Newsletter, "Ctrl+S", PrimaryRank: 2),
        new(ActionId.SaveAs, "Save it as a new file…",
            "Keeps your work in a file you choose, leaving the one you started from alone.",
            ActionGroup.Newsletter, "Ctrl+Shift+S"),
        new(ActionId.RestoreDocument, "Go back to an earlier version…",
            "Opens one of the copies TrestleBoard kept each time you saved.",
            ActionGroup.Newsletter),
        new(ActionId.ReviewNewsletter, "Look it over with me…",
            "Goes through the newsletter with you before you make the PDF, one question at a time.",
            ActionGroup.Newsletter),
        new(ActionId.CheckSpelling, "Check my spelling…",
            "Shows you one word at a time that TrestleBoard does not know, with the sentence it is in.",
            ActionGroup.Newsletter),
        new(ActionId.ShowLastYear, "Show me last year's…",
            "Opens the same month from last year, to look at while you write this one.",
            ActionGroup.Newsletter),
        new(ActionId.ReadAloud, "Read it back to me…",
            "Goes through the newsletter one sentence at a time, out loud where this computer can.",
            ActionGroup.Newsletter),
        // M76 (f): rank 1 of the Newsletter group, ahead of Save (2) and "Which issue is this?"
        // (3). The PDF is the terminal goal of the whole product — §7 calls it the file they email
        // to the lodge — and a rank is a standing claim rather than a claim about right now, so the
        // standing answer to "what did you come here to do" has to be the thing the committee is
        // here to do. The other two are each the right answer at a moment, and a moment is what the
        // what's-next card is for.
        new(ActionId.ExportPdf, "Make the PDF…", "Makes the file you email to the lodge.",
            ActionGroup.Newsletter, "Ctrl+E", PrimaryRank: 1),
        new(ActionId.ExportDraftPdf, "Make a draft copy…",
            "Makes a PDF with DRAFT across every page, for the Master to read before you send it.",
            ActionGroup.Newsletter),
        new(ActionId.PrintPdf, "Print it",
            "Sends the PDF you just made to your printer.", ActionGroup.Newsletter),
        new(ActionId.SaveAsTemplate, "Save this as one of my templates…",
            "Keeps this newsletter's layout to start from another time, without its writing or its date.",
            ActionGroup.Newsletter),
        new(ActionId.ManageTemplates, "My templates…",
            "Renames, removes, or hands on the templates you have saved.", ActionGroup.Newsletter),
        // M75. No chord. Every letter that reads as "issue", "month" or "date" is already spoken
        // for, and PLAN.md §6 asks that every command be reachable from the keyboard, not that
        // every command have a gesture — Alt+F then C reaches this one, like the twenty others
        // that carry none. It is a primary panel action instead, because until it is answered it
        // is the only thing worth doing to this newsletter.
        //
        // M76 (f) settles that claim against the other two in this group and it comes third, which
        // does NOT weaken the M75 argument: that argument is about the state before the question is
        // answered, and WhatsNext already leads its card with this command in exactly that state
        // ("Say which issue this is", and nothing else on the card is worth doing first). A rank is
        // fixed for all time; the card is where an offer that is only sometimes the answer belongs.
        new(ActionId.SetIssueDate, "Which issue is this?…",
            "Asks which month and year this newsletter is for, on the cover heading.",
            ActionGroup.Newsletter, PrimaryRank: 3),
        new(ActionId.SendIt, "Now send it…",
            "Opens your email with the brethren who get it by email already filled in.",
            ActionGroup.Newsletter),
        new(ActionId.Exit, "Exit", "Closes TrestleBoard.", ActionGroup.Newsletter),

        // ---- Edit -------------------------------------------------------------------------------
        new(ActionId.Undo, "Undo", "Takes back the last thing you did.", ActionGroup.Edit, "Ctrl+Z"),
        new(ActionId.Redo, "Redo", "Does again the thing you just took back.", ActionGroup.Edit, "Ctrl+Y"),
        new(ActionId.Cut, "Cut", "Removes the highlighted words and keeps a copy.", ActionGroup.Edit, "Ctrl+X"),
        new(ActionId.Copy, "Copy", "Keeps a copy of the highlighted words.", ActionGroup.Edit, "Ctrl+C"),
        new(ActionId.Paste, "Paste", "Puts the copied words where the cursor is.", ActionGroup.Edit, "Ctrl+V"),
        new(ActionId.SelectAll, "Select all", "Highlights everything in this piece of writing.",
            ActionGroup.Edit, "Ctrl+A"),
        // M49: NO chord, deliberately. Ctrl+A was the obvious choice — the same key, the other
        // side of the mode, and KeyboardMap could scope the two rows apart cleanly. But the MENU
        // would then show two items both advertising Ctrl+A, and a user who cannot tell which one
        // they are about to get is the exact confusion this review is about. Section 6 asks that
        // every command be reachable from the keyboard, not that every command have a chord; Alt+E
        // then E reaches this one, like the twenty other commands that carry no gesture.
        new(ActionId.SelectAllFrames, "Choose everything on this page",
            "Takes hold of every box and picture on the page at once, ready to line them up.",
            ActionGroup.Edit),
        new(ActionId.AddNextToSelection, "Also choose the next one",
            "Keeps what you have chosen and takes hold of the next thing on the page as well.",
            ActionGroup.Edit, "Ctrl+Tab"),
        new(ActionId.AddPreviousToSelection, "Also choose the one before",
            "Keeps what you have chosen and takes hold of the one before it as well.",
            ActionGroup.Edit, "Ctrl+Shift+Tab"),
        new(ActionId.Find, "Find…", "Looks for words anywhere in this newsletter.",
            ActionGroup.Edit, "Ctrl+F"),
        new(ActionId.Replace, "Find and replace…",
            "Looks for words and puts different ones in their place.", ActionGroup.Edit, "Ctrl+H"),

        // ---- Text -------------------------------------------------------------------------------
        // M76 (f): Bold 1, Italic 2, "Change the font for this style" 3. All three used to assert
        // the primary treatment for themselves and all three got it at once, which is the panel of
        // three gold bars the M76 audit found. Bold leads because it is the commonest thing anybody
        // does to writing they have just highlighted; the style command is last because it changes
        // every paragraph of that kind in the newsletter, which is a bigger act than the emphasis
        // bar should be inviting.
        new(ActionId.Bold, "Bold", "Makes the highlighted words heavier.", ActionGroup.Text, "Ctrl+B",
            PrimaryRank: 1),
        new(ActionId.Italic, "Italic", "Slants the highlighted words.", ActionGroup.Text, "Ctrl+I",
            PrimaryRank: 2),
        new(ActionId.BulletList, "Make this a list of points",
            "Puts a dot in front of this paragraph, and keeps the wrapped lines lined up under the "
            + "writing.", ActionGroup.Text),
        new(ActionId.NumberList, "Make this a numbered list",
            "Numbers this paragraph and the ones with it, and renumbers them if you add another.",
            ActionGroup.Text),
        new(ActionId.ParagraphStyle, "Paragraph style ▸",
            "Chooses what kind of paragraph this is, such as a heading.", ActionGroup.Text),
        new(ActionId.FontsAndStyles, "Change the font for this style ▸",
            "Chooses the typeface and size for every piece of writing of this kind.",
            ActionGroup.Text, "Ctrl+Shift+D", PrimaryRank: 3),
        new(ActionId.BiggerText, "+ Bigger", "Makes this kind of writing one step larger everywhere.",
            ActionGroup.Text, "Ctrl+Shift+."),
        new(ActionId.SmallerText, "− Smaller", "Makes this kind of writing one step smaller everywhere.",
            ActionGroup.Text, "Ctrl+Shift+,"),
        new(ActionId.FontJustHere, "Use a different font just here…",
            "Changes the typeface of the highlighted words only, leaving the rest alone.",
            ActionGroup.Text),
        new(ActionId.ClearFontOverride, "Put it back to the usual font",
            "Returns the highlighted words to the font the rest of this kind of writing uses.",
            ActionGroup.Text),

        // ---- Putting things on the page ---------------------------------------------------------
        new(ActionId.InsertPhrase, "Words for hard news…",
            "Offers ready-made wording for a memorial, a get-well message and other hard moments.",
            ActionGroup.Insert),
        new(ActionId.SavePhrase, "Keep these words for next time…",
            "Saves the writing you have highlighted, so you can use it again in a later issue.",
            ActionGroup.Insert),
        // M76 (f): a box for writing leads the Insert group and a picture follows it. A trestle
        // board is mostly words — the pictures go into the frames the template already ships — so
        // the standing answer to "what are you here to add" is somewhere to write.
        new(ActionId.AddTextFrame, "Add a box for writing", "Puts an empty box on the page for you to write in.",
            ActionGroup.Insert, "Ctrl+Shift+T", PrimaryRank: 1),
        new(ActionId.InsertPhoto, "Insert a picture…", "Puts a photograph on the page.",
            ActionGroup.Insert, "Ctrl+Shift+P", PrimaryRank: 2),
        new(ActionId.InsertOfficers, "Lodge officers", "Adds the officers table and asks who they are.",
            ActionGroup.Insert),
        new(ActionId.InsertBirthdays, "Birthdays", "Adds the birthday list.", ActionGroup.Insert),
        new(ActionId.InsertCommittees, "Committees", "Adds the committee list.", ActionGroup.Insert),
        new(ActionId.InsertDistrictCalendar, "District calendar", "Adds the 22nd District meeting table.",
            ActionGroup.Insert),
        new(ActionId.InsertEventCard, "Announcement box", "Adds a box for one announcement.", ActionGroup.Insert),
        new(ActionId.InsertCoverBanner, "Cover heading", "Adds the lodge name and meeting details for page one.",
            ActionGroup.Insert),
        new(ActionId.InsertSimpleList, "A list of my own",
            "Adds a list of up to three columns that you fill in yourself.", ActionGroup.Insert),

        // ---- The selected thing -----------------------------------------------------------------
        new(ActionId.DeleteFrame, "Delete this", "Takes it off the page. You can undo this.",
            ActionGroup.Item, "Delete"),
        new(ActionId.EditWidget, "Change what this says…", "Asks the questions again, already filled in.",
            ActionGroup.Item, "Ctrl+Shift+E", PrimaryRank: 1),
        new(ActionId.EditWidgetList, "Edit the list…", "Shows the whole list at once, with big rows.",
            ActionGroup.Item, "Ctrl+Shift+G"),
        new(ActionId.FitToContents, "Make it fit what is in it", "Makes the box exactly as tall as what is in it.",
            ActionGroup.Item, "Ctrl+Shift+Y"),
        new(ActionId.SyncBirthdays, "Bring in birthdays from the address book",
            "Fills the birthday list in from your address book, showing you the changes first.",
            ActionGroup.Item, "Ctrl+Shift+U"),
        new(ActionId.SyncOfficers, "Fill in the officers from the address book",
            "Fills the officers table in from your address book, showing you the changes first.",
            ActionGroup.Item, "Ctrl+Shift+B"),

        // ---- Pictures ---------------------------------------------------------------------------
        // M18 puts the replace command FIRST: an empty frame is the state every photo template
        // ships in, and until M18 no command in the app could fill one.
        //
        // M76 (f) makes that ordering the emphasis too: rank 1 here, "Fix this picture" rank 2.
        // Until there is a picture in the frame there is nothing to fix, and once there is one this
        // command is the way to change it — so putting the gold on the other would spend it on a
        // command that is refused in the state the templates ship in.
        new(ActionId.ReplacePicture, "Put a picture here…", "Chooses a picture file and puts it in this frame.",
            ActionGroup.Picture, "Ctrl+Shift+O", PrimaryRank: 1),
        new(ActionId.FixPhoto, "Fix this picture", "Crops it to the frame and brightens it in one step.",
            ActionGroup.Picture, "Ctrl+Shift+F", PrimaryRank: 2),
        // M38: these two names describe the OUTCOME rather than the operation, and there are two of
        // them rather than three.
        //
        // "Adjust the picture…" and "Trim the edges…" were separate entries that opened the SAME
        // window — TrimPictureAsync is ShowAdjustWindowAsync(startOnTrim: true), identical dialog,
        // different focus target. Two names for one door is most of what "four near-synonym picture
        // verbs" meant (review §14.4). The trim sliders keep their own heading INSIDE the window, so
        // anybody scanning for the word still finds it.
        //
        // "Adjust" and "Fix" are near-synonyms in English and gave no clue which did what. They now
        // read as a pair: Fix does it for you, Change how it looks lets you do it.
        new(ActionId.AdjustPhoto, "Change how it looks…",
            "Brightness, colour, turning it, and taking the edges off.",
            ActionGroup.Picture, "Ctrl+Shift+A"),
        new(ActionId.PositionPicture, "Choose which part shows…",
            "Slides the picture inside its frame without changing how much of it shows.",
            ActionGroup.Picture),
        new(ActionId.DismissCropNotice, "Dismiss this note",
            "Hides the stretched-picture warning until the frame changes shape again.",
            ActionGroup.Picture),
        new(ActionId.CaptionPicture, "Write a caption…", "Writes the words printed under the picture.",
            ActionGroup.Picture),
        new(ActionId.DescribePicture, "Describe this picture…",
            "Writes what somebody who cannot see it should be told.", ActionGroup.Picture),

        // ---- How text flows ---------------------------------------------------------------------
        new(ActionId.ToggleWrap, "Make the writing flow around it", "Makes the writing on the page flow around it.",
            ActionGroup.TextFlow, "Ctrl+Shift+W", PrimaryRank: 1),
        new(ActionId.LinkFrames, "Continue this text in another frame…",
            "Lets a long article carry on in a second box.", ActionGroup.TextFlow, "Ctrl+Shift+L"),
        new(ActionId.UnlinkFrames, "Stop continuing into the next frame",
            "Ends the link so this box stands on its own.", ActionGroup.TextFlow, "Ctrl+Shift+K"),
        new(ActionId.AutoFlow, "Make the rest fit",
            "Adds pages and boxes until all of this writing has somewhere to go.",
            ActionGroup.TextFlow, "Ctrl+Shift+M"),

        // ---- Arranging --------------------------------------------------------------------------
        new(ActionId.BringForward, "Move it forward", "Moves it one step towards the front.",
            ActionGroup.Arrange, "Ctrl+]"),
        new(ActionId.SendBackward, "Move it back", "Moves it one step towards the back.",
            ActionGroup.Arrange, "Ctrl+["),
        new(ActionId.BringToFront, "Move it to the front", "Puts it in front of everything else.",
            ActionGroup.Arrange, "Ctrl+Shift+]"),
        new(ActionId.SendToBack, "Move it to the back", "Puts it behind everything else.",
            ActionGroup.Arrange, "Ctrl+Shift+["),

        // ---- Lining things up (M21) -------------------------------------------------------------
        // Each one says "the things you have chosen", because that is the whole difference: these
        // are the first commands in the app that act on more than one thing at a time.
        new(ActionId.AlignLeft, "Line up the left edges",
            "Moves the things you have chosen so their left edges are in a line.", ActionGroup.Arrange),
        new(ActionId.AlignCentres, "Line up the centres, side to side",
            "Moves them sideways until their middles are one above the other.", ActionGroup.Arrange),
        new(ActionId.AlignRight, "Line up the right edges",
            "Moves the things you have chosen so their right edges are in a line.", ActionGroup.Arrange),
        new(ActionId.AlignTop, "Line up the top edges",
            "Moves the things you have chosen so their tops are in a line.", ActionGroup.Arrange),
        new(ActionId.AlignMiddles, "Line up the middles, top to bottom",
            "Moves them up and down until their middles are side by side.", ActionGroup.Arrange),
        new(ActionId.AlignBottom, "Line up the bottom edges",
            "Moves the things you have chosen so their bottoms are in a line.", ActionGroup.Arrange),
        new(ActionId.DistributeHorizontally, "Space them out evenly, side to side",
            "Leaves the same gap between each one, without moving the two on the ends.",
            ActionGroup.Arrange),
        new(ActionId.DistributeVertically, "Space them out evenly, top to bottom",
            "Leaves the same gap above and below each one, without moving the top and bottom ones.",
            ActionGroup.Arrange),

        // ---- Pages ------------------------------------------------------------------------------
        new(ActionId.NextPage, "Next page", "Shows the following page.", ActionGroup.Page, "Ctrl+PageDown"),
        new(ActionId.PreviousPage, "Previous page", "Shows the page before this one.",
            ActionGroup.Page, "Ctrl+PageUp"),
        new(ActionId.AddPage, "Add a page after this one", "Puts a new empty page in.", ActionGroup.Page,
            "Ctrl+Shift+N"),
        new(ActionId.RemovePage, "Delete this page", "Takes this page out. You can undo this.", ActionGroup.Page),
        new(ActionId.MovePageEarlier, "Move this page earlier", "Swaps it with the page before it.",
            ActionGroup.Page),
        new(ActionId.MovePageLater, "Move this page later", "Swaps it with the page after it.", ActionGroup.Page),

        // M76 (g). The command the page rail is made of: one press, any page, instead of pressing
        // "Next page" three times to reach page four. No shortcut — the target is a page number and
        // a chord cannot carry one; the rail's own arrow keys are the keyboard path, and the menu
        // item below opens the rail and stands on the page you are on (docs/M76-spec.md §6).
        new(ActionId.GoToPage, "Go to a page", "Shows whichever page you pick out of the row down the side.",
            ActionGroup.Page),

        // ---- Looking at it ----------------------------------------------------------------------
        new(ActionId.ZoomIn, "Zoom in", "Makes the page on screen bigger.", ActionGroup.View, "Ctrl+="),
        new(ActionId.ZoomOut, "Zoom out", "Makes the page on screen smaller.", ActionGroup.View, "Ctrl+-"),
        new(ActionId.ActualSize, "Actual size", "Shows the page at its printed size.", ActionGroup.View, "Ctrl+0"),
        new(ActionId.FitPage, "Fit page", "Shows the whole page in the window.", ActionGroup.View, "Ctrl+1"),
        // M28: F10 rather than a Ctrl chord. Nothing else in the app claims a bare function key
        // except F2 and F6, and the window that makes everything bigger is the one an elderly user
        // most needs to be able to find with one finger.
        new(ActionId.Settings, "How things look…", "Changes the theme and how big everything is.",
            ActionGroup.View, "F10"),
        new(ActionId.NextRegion, "Move to the next part of the window",
            "Moves between the page, the panel, the toolbar and the menus.", ActionGroup.View, "F6"),
        new(ActionId.PreviousRegion, "Move to the previous part of the window",
            "Moves the other way round the window.", ActionGroup.View, "Shift+F6"),
        new(ActionId.ToggleActionPanel, "Show what I can do",
            "Shows or hides the panel of things you can do to what you have chosen.", ActionGroup.View),
        // M76 (g): the rail's own switch, declared beside the panel's because it makes the same
        // promise about the same kind of thing — chrome the user is allowed to put away.
        new(ActionId.TogglePageRail, "Show the pages down the side",
            "Shows or hides the row of small pages down the left, so you can see the whole "
            + "newsletter at once and go straight to any page of it.", ActionGroup.View),
        new(ActionId.ShowMargins, "Show the edge to keep inside",
            "Draws a faint line where the printing stops, so you can see what is too close to it.",
            ActionGroup.View),
        new(ActionId.ShowFontChanges, "Show where fonts were changed",
            "Underlines, on screen only, any writing whose font was changed by hand.",
            ActionGroup.View),
        new(ActionId.ShowSpelling, "Show my spelling mistakes",
            "Puts a dotted line, on screen only, under any word TrestleBoard does not know.",
            ActionGroup.View),

        // ---- The address book (M12) ---------------------------------------------------------------
        new(ActionId.ShowPeople, "People…", "Opens your lodge address book.",
            ActionGroup.People, "Ctrl+Shift+R", PrimaryRank: 1),
        new(ActionId.ImportPeople, "Import from a file…",
            "Reads a list of members from a spreadsheet you already have.", ActionGroup.People),
        new(ActionId.ExportPeople, "Save as a spreadsheet…",
            "Writes your address book out so you can open it in Excel.", ActionGroup.People),
        new(ActionId.UndoPeopleChange, "Undo the last change",
            "Takes back the last change to your address book.", ActionGroup.People),
        new(ActionId.RestorePeople, "Restore an earlier version…",
            "Puts your address book back as it was on an earlier day.", ActionGroup.People),

        // ---- Bringing writing in (M66) ------------------------------------------------------------
        new(ActionId.BringInWriting, "Bring in writing from a file…",
            "Reads a Word document or a text file and puts the writing on the page, in this "
            + "newsletter's own lettering.", ActionGroup.Insert),

        // ---- A page from a PDF (M67) ----------------------------------------------------------------
        new(ActionId.BringInPdfPage, "A page from a PDF…",
            "Shows you the pages of a PDF and puts the one you choose on the page as a picture.",
            ActionGroup.Insert),

        // ---- The emblem shelf (M65) ---------------------------------------------------------------
        new(ActionId.InsertEmblem, "Add an emblem…",
            "Puts one of the craft's emblems on the page — the square and compasses, the working "
            + "tools, a rule or a corner ornament.", ActionGroup.Insert),

        // ---- Everything on this computer (M64) --------------------------------------------------
        new(ActionId.PackUpForSuccessor, "Pack everything up for my successor…",
            "Writes your address book, your templates, your saved wordings and your settings into "
            + "one file to hand on.", ActionGroup.Everything),
        new(ActionId.BringInAPack, "Bring in a predecessor's pack…",
            "Puts back what the last committee packed up. You choose what to take, and nothing is "
            + "replaced without being asked.", ActionGroup.Everything),

        // ---- Help -------------------------------------------------------------------------------
        new(ActionId.HowDoI, "How do I…?",
            "Opens a search box over everything TrestleBoard can do. Type it in your own words.",
            ActionGroup.Help, "F1", PrimaryRank: 1),
        new(ActionId.ShowTheTour, "Show me round again",
            "Walks through how a month goes, in five screens. The same one you were shown the first "
            + "time TrestleBoard opened.",
            ActionGroup.Help),
        new(ActionId.CheckForUpdates, "Check for an update", "Asks whether a newer TrestleBoard exists.",
            ActionGroup.Help),
        new(ActionId.SaveProblemReport, "Save a report of a problem…",
            "Writes a file you can email to whoever looks after TrestleBoard, saying what this "
            + "computer is and what went wrong. It holds nothing about your members.",
            ActionGroup.Help),
        new(ActionId.About, "About TrestleBoard", "Shows which version this is.", ActionGroup.Help),
        new(ActionId.FontLicences, "Fonts and licences",
            "Lists the typefaces that came with TrestleBoard and the licence each one is used under.",
            ActionGroup.Help),
        new(ActionId.Licence, "Licence",
            "Shows what you are allowed to do with TrestleBoard itself. It is free for lodges, "
            + "churches, charities and personal use.",
            ActionGroup.Help),
        new(ActionId.ShowExampleIssue, "Show me an example newsletter",
            "Opens a finished five-page newsletter you can look around in and change without saving.",
            ActionGroup.Help),
    ];

    private static readonly Dictionary<string, EditorAction> ById =
        AllActions.ToDictionary(a => a.Id, StringComparer.Ordinal);

    /// <summary>
    /// M76 (f). <b>Two actions in one group may not claim the same non-zero
    /// <see cref="EditorAction.PrimaryRank"/>, and the catalog refuses to exist if they do.</b>
    ///
    /// <para>This is the same stance <c>ActionAvailability</c> takes on an empty reason: the
    /// invariant is enforced where the mistake is made rather than reported where it shows up.
    /// A tie would have to be broken by something, and the only thing left to break it with is
    /// declaration order — "whichever one happened to be enumerated first", which is not a rule
    /// anybody can read off the source and predict, and which is exactly what the emphasis was
    /// being rescued from. So ties are made impossible instead of resolved.</para>
    ///
    /// <para>A negative rank is refused for the same reason: zero already means "no claim", so a
    /// negative number is somebody expressing something the scale does not carry.</para>
    /// </summary>
    static ActionCatalog()
    {
        foreach (IGrouping<ActionGroup, EditorAction> group in AllActions.GroupBy(a => a.Group))
        {
            foreach (EditorAction action in group.Where(a => a.PrimaryRank < 0))
            {
                throw new InvalidOperationException(
                    $"{action.Id} declares a negative PrimaryRank ({action.PrimaryRank}). Zero "
                    + "means no claim to the primary treatment; ranks start at 1.");
            }

            IGrouping<int, EditorAction>? clash = group
                .Where(a => a.PrimaryRank > 0)
                .GroupBy(a => a.PrimaryRank)
                .FirstOrDefault(byRank => byRank.Count() > 1);

            if (clash is not null)
            {
                throw new InvalidOperationException(
                    $"{string.Join(" and ", clash.Select(a => a.Id))} both claim PrimaryRank "
                    + $"{clash.Key} in the {group.Key} group. Only one action per group may hold a "
                    + "rank, because only one of them can be drawn as the offer the user came for.");
            }
        }
    }

    /// <summary>Every action, in declaration order.</summary>
    public static IReadOnlyList<EditorAction> All => AllActions;

    /// <summary>The declaration for one id. Throws for an unknown id — that is a programming error.</summary>
    public static EditorAction Get(string actionId) => ById[actionId];

    /// <summary>
    /// M35: <c>NotNullWhen</c>, and a nullable out. The signature promised a non-null
    /// <see cref="EditorAction"/> on every path and handed back null on the false one, so a caller
    /// that ignored the bool got a NullReferenceException somewhere later instead of a compiler
    /// warning here (review §14.2). The `out action!` that used to suppress it was the compiler
    /// being told to stop noticing.
    /// </summary>
    public static bool TryGet(
        string actionId,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out EditorAction? action) =>
        ById.TryGetValue(actionId, out action);

    /// <summary>
    /// What this action is called RIGHT NOW (PLAN.md §11 M18). Almost every action has one name; two
    /// picture commands do not, because the same command means two different things depending on
    /// whether the frame already holds a photograph, and "Swap this picture…" offered over an empty
    /// grey rectangle would be describing something that is not there.
    ///
    /// <para>The catalog's own <see cref="EditorAction.Title"/> stays the empty-frame wording, so a
    /// surface that has not been taught about this — a menu header written in XAML — still reads
    /// correctly rather than reading wrongly.</para>
    /// </summary>
    public static string TitleFor(string actionId, ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return actionId switch
        {
            // M74 (f): "swap" is a photograph's word, and this said it about anything that was not
            // an empty picture frame — a drawing, a box of writing, a blank selection. The greyed
            // menu row over a selected emblem read "Swap this picture…" of something the very next
            // method refuses to swap by name. It has to be a photograph before it can be swapped.
            ActionId.ReplacePicture
                when context.Selection == SelectionKind.Photo && !context.SelectedPictureIsEmpty =>
                "Swap this picture…",
            ActionId.CaptionPicture when context.SelectedPictureHasCaption => "Change the caption…",

            // M24: a newsletter that has never been saved has no file to save INTO, so this one
            // will ask where to put it — and the ellipsis is how every other program on the
            // machine promises that a question is coming.
            ActionId.Save when !context.DocumentHasFile => "Save this newsletter…",
            _ => Get(actionId).Title,
        };
    }

    /// <summary>
    /// Can the user do this right now, and if not, why not? The one decision point; everything the
    /// user sees about availability comes from here.
    /// </summary>
    public static ActionAvailability Evaluate(string actionId, ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return actionId switch
        {
            // Always available: these are how you get a newsletter in the first place. The address
            // book's own two doors are here too — it is app state, so it does not need a newsletter
            // open, and an empty book is exactly when importing matters most.
            ActionId.Open or ActionId.OpenSample or ActionId.NewFromTemplate or ActionId.Exit
                or ActionId.Settings or ActionId.NextRegion or ActionId.PreviousRegion
                or ActionId.ToggleActionPanel
                // M76 (g): showing or hiding the rail is a fact about the window, not about the
                // newsletter — there is something to say either way, and an empty window is
                // exactly when somebody might want the strip out of the road.
                or ActionId.TogglePageRail
                or ActionId.CheckForUpdates or ActionId.About
                or ActionId.FontLicences or ActionId.Licence or ActionId.ShowExampleIssue
                or ActionId.ManageTemplates
                // M63: help must never be unavailable. An app that will not tell you how to do
                // something because of what you have selected is the exact moment help is needed.
                or ActionId.HowDoI or ActionId.ShowTheTour
                // M77: a report about an app that is misbehaving must not be refused BY the app
                // that is misbehaving. It needs no newsletter and no selection — the facts it
                // carries are about the installation, and they are all there before anything is
                // open.
                or ActionId.SaveProblemReport
                // M64: neither needs a newsletter open, and both are most likely to be reached on a
                // computer that has never had one — the successor's, on their first afternoon.
                or ActionId.PackUpForSuccessor or ActionId.BringInAPack
                or ActionId.ShowPeople or ActionId.ImportPeople =>
                ActionAvailability.Available,

            // ---- The address book (M12) -----------------------------------------------------------
            // M75 (f): the unreadable case first here too. "Save as a spreadsheet" over a book that
            // could not be read would write an empty spreadsheet the user might then re-import.
            ActionId.ExportPeople =>
                context.RosterCouldNotBeRead ? ActionAvailability.Blocked(CouldNotReadTheAddressBook)
                : context.RosterCount > 0 ? ActionAvailability.Available
                : ActionAvailability.Blocked(
                    "Your address book is empty, so there is nothing to save yet. Import a list, or "
                    + "add somebody in the People window.",
                    ActionId.ImportPeople),
            ActionId.UndoPeopleChange => context.RosterCanUndo
                ? ActionAvailability.Available
                : ActionAvailability.Blocked(
                    "Nothing has changed in your address book since TrestleBoard was opened."),
            ActionId.RestorePeople => context.RosterHasEarlierVersions
                ? ActionAvailability.Available
                : ActionAvailability.Blocked(
                    "There are no earlier versions of your address book yet. TrestleBoard keeps one "
                    + "every time you change it."),

            ActionId.StartFromLastMonth => !context.CanStartFromLastMonth
                ? ActionAvailability.Blocked(
                    "There is no newsletter open to carry forward. Open last month's newsletter first.",
                    ActionId.Open)
                : RequiresIssueDate(context, "next month is worked out from it"),

            // M75 (a). Available whenever there is a cover heading to ask it on — including after
            // it has been answered, because the ask is also the correction. A wrong month typed
            // once and unfixable would be worse than no ask at all.
            ActionId.SetIssueDate => !context.HasDocument
                ? ActionAvailability.Blocked(NoNewsletter, ActionId.NewFromTemplate)
                : context.HasCoverHeading
                    ? ActionAvailability.Available
                    : ActionAvailability.Blocked(
                        "This newsletter has no cover heading on it, and that is where TrestleBoard "
                        + "asks which issue you are making. Add one to the front page first.",
                        ActionId.InsertCoverBanner),

            // M24. Saving an unchanged newsletter would rewrite the file for nothing, so it is
            // refused — but the refusal is the sentence that answers the question the user was
            // really asking when they reached for Ctrl+S: is my work safe?
            ActionId.Save => !context.HasDocument
                ? ActionAvailability.Blocked(NoNewsletter, ActionId.NewFromTemplate)
                : context.HasUnsavedChanges
                    ? ActionAvailability.Available
                    : ActionAvailability.Blocked(
                        context.DocumentFileName is { } saved
                            ? $"Everything is saved already. Your work is in {saved}."
                            : "There is nothing new to save.",
                        ActionId.SaveAs),
            ActionId.SaveAs => RequiresIssueDate(context, "the file is named after it"),

            // M39. Two different "no" here, because they send the user somewhere different: a
            // newsletter with no file of its own has never been saved over, and one with a file but
            // no ring has been saved exactly once.
            ActionId.RestoreDocument => !context.DocumentHasFile
                ? ActionAvailability.Blocked(
                    "This newsletter has not been saved yet, so there are no earlier versions of it.",
                    ActionId.SaveAs)
                : context.DocumentHasEarlierVersions
                    ? ActionAvailability.Available
                    : ActionAvailability.Blocked(
                        "You have saved this newsletter once, so there is nothing earlier to go back "
                        + "to yet. TrestleBoard keeps a copy every time you save over it."),

            // M75 (c): spelling and reading aloud are about the words on the page and do not care
            // which issue it is; the other four all put the issue's month somewhere a reader will
            // see it, or go looking for it in the archive.
            ActionId.CheckSpelling or ActionId.ReadAloud => RequiresDocument(context),
            ActionId.ReviewNewsletter =>
                RequiresIssueDate(context, "the check goes through the issue's own month with you"),
            ActionId.ShowLastYear =>
                RequiresIssueDate(context, "last year's issue is found by it"),
            ActionId.ExportPdf or ActionId.ExportDraftPdf =>
                RequiresIssueDate(context, "the PDF is named after it"),

            // M53: there has to BE a PDF before it can go to a printer, and the reason names the
            // command that makes one rather than leaving the user to work it out.
            // M56 needs a newsletter to talk about but NOT a PDF: somebody may want to warn the
            // lodge that this month's issue is coming, and refusing until they have exported would
            // be the app deciding the order of their evening.
            ActionId.SendIt => RequiresIssueDate(context, "the email says so in its subject line"),

            // M75: NOT gated on the issue date. A template is a layout with the date deliberately
            // taken out of it — refusing to make one until a date is filled in would be refusing
            // the one thing that is meant to have none.
            ActionId.SaveAsTemplate => RequiresDocument(context),

            ActionId.PrintPdf => context.ExportedPdfThisSession
                ? ActionAvailability.Available
                : ActionAvailability.Blocked(
                    "You have not made the PDF yet, so there is nothing to print. Make it first, "
                    + "and TrestleBoard will offer to print it for you.",
                    ActionId.ExportPdf),

            // ---- Edit ---------------------------------------------------------------------------
            // M49, review §14.3: this app has TWO undo stacks — the newsletter's and the address
            // book's — and the second was disclosed only inside the People menu. So somebody who
            // corrected a telephone number and reached for Ctrl+Z was told "there is nothing to
            // take back", which was true of the newsletter and false of what they had just done.
            ActionId.Undo => context.CanUndo
                ? ActionAvailability.Available
                : ActionAvailability.Blocked(
                    context.RosterCanUndo
                        ? "There is nothing to take back in this newsletter. Your last change to the "
                          + "address book can be taken back from the People menu."
                        : "There is nothing to take back yet.",
                    context.RosterCanUndo ? ActionId.UndoPeopleChange : null),
            ActionId.Redo => context.CanRedo
                ? ActionAvailability.Available
                : ActionAvailability.Blocked(
                    "There is nothing to do again. This becomes possible after you undo something.",
                    ActionId.Undo),
            ActionId.Cut or ActionId.Copy => !context.IsEditingText
                ? ActionAvailability.NotApplicable(NeedsText)
                : context.HasTextSelection
                    ? ActionAvailability.Available
                    : ActionAvailability.Blocked(
                        "No words are highlighted. Drag across some words first, or press Ctrl+A to take them all.",
                        ActionId.SelectAll),
            // M18: paste stopped being only about words. Outside a piece of writing it puts a
            // picture from the clipboard on the page, so it needs a newsletter rather than a caret.
            // What is actually on the clipboard is the shell's to read — asking here would mean
            // reading the clipboard on every refresh — and it says so out loud when there is
            // nothing to paste.
            ActionId.Paste => context.IsEditingText || context.HasDocument
                ? ActionAvailability.Available
                : ActionAvailability.NotApplicable(NeedsText),
            ActionId.SelectAll => context.IsEditingText
                ? ActionAvailability.Available
                : ActionAvailability.NotApplicable(NeedsText),

            // M21. Neither needs a caret: looking for words is something you do TO the newsletter,
            // and requiring the user to click into a frame first would mean requiring them to guess
            // which frame the words are in — the exact thing they opened this to find out.
            ActionId.Find or ActionId.Replace => RequiresDocument(context),

            // ---- Text ---------------------------------------------------------------------------
            ActionId.Bold or ActionId.Italic or ActionId.ParagraphStyle
                or ActionId.BulletList or ActionId.NumberList => context.IsEditingText
                ? ActionAvailability.Available
                : ActionAvailability.NotApplicable(NeedsText),

            // M54. Both need somewhere for the words to go, or somewhere to take them from, and
            // that is the caret — the same condition Bold has, said in this command's own terms.
            ActionId.InsertPhrase => context.IsEditingText
                ? ActionAvailability.Available
                : ActionAvailability.NotApplicable(
                    "Click into some writing first, and the words will go in where the cursor is."),
            ActionId.SavePhrase => context.HasTextSelection
                ? ActionAvailability.Available
                : ActionAvailability.NotApplicable(
                    "Highlight the words you would like to keep first, then this saves them to "
                    + "reach for again next time."),

            // ---- Fonts and sizes (M14) ------------------------------------------------------------
            // Style-first: these three act on the kind of writing the caret is in, so they need a
            // caret but not a highlight. The font picker itself only needs a newsletter, because it
            // is a whole-document sheet the user can open to look at.
            ActionId.FontsAndStyles => RequiresDocument(context),
            ActionId.BiggerText or ActionId.SmallerText or ActionId.FontJustHere =>
                context.IsEditingText
                    ? ActionAvailability.Available
                    : ActionAvailability.NotApplicable(NeedsText),
            ActionId.ClearFontOverride => !context.IsEditingText
                ? ActionAvailability.NotApplicable(NeedsText)
                : context.SelectionUsesFontOverride
                    ? ActionAvailability.Available
                    : ActionAvailability.NotApplicable(
                        "This writing already uses the font its kind of writing normally uses."),
            ActionId.ShowFontChanges or ActionId.ShowMargins or ActionId.ShowSpelling =>
                RequiresDocument(context),

            // M50. Same rule as SelectAllFrames: there has to be something on the page. It is
            // deliberately NOT gated on there already being a selection — with nothing chosen,
            // "also choose the next one" simply chooses one, which is the same generosity
            // AddToSelection has always shown a first Shift+click.
            ActionId.AddNextToSelection or ActionId.AddPreviousToSelection => !context.HasDocument
                ? ActionAvailability.Blocked(NoNewsletter, ActionId.NewFromTemplate)
                : context.PageHasFrames
                    ? ActionAvailability.Available
                    : ActionAvailability.Blocked(
                        "There is nothing on this page yet to take hold of.",
                        ActionId.AddTextFrame),

            // M49. Refused when the page is empty, and the refusal says which page it looked at:
            // "nothing happened" over a page the user cannot see the contents of is the failure
            // this rule exists to prevent.
            ActionId.SelectAllFrames => !context.HasDocument
                ? ActionAvailability.Blocked(NoNewsletter, ActionId.NewFromTemplate)
                : context.PageHasFrames
                    ? ActionAvailability.Available
                    : ActionAvailability.Blocked(
                        "There is nothing on this page yet to take hold of.",
                        ActionId.AddTextFrame),

            // ---- Putting things on the page -----------------------------------------------------
            ActionId.AddTextFrame or ActionId.InsertPhoto or ActionId.InsertOfficers
                or ActionId.InsertBirthdays or ActionId.InsertCommittees
                or ActionId.InsertDistrictCalendar or ActionId.InsertEventCard
                or ActionId.InsertCoverBanner or ActionId.InsertSimpleList
                or ActionId.InsertEmblem or ActionId.BringInWriting => RequiresDocument(context),

            // M67: two reasons it might not be possible, and the order matters. "No newsletter" is
            // the ordinary one and comes first; "this computer cannot read PDFs" is a fact about
            // the machine that will never change by itself, so it is said only once the other is
            // out of the way — telling somebody with no newsletter open about a missing native
            // library would be answering a question they did not ask.
            ActionId.BringInPdfPage =>
                !context.HasDocument ? ActionAvailability.Blocked(NoNewsletter, ActionId.NewFromTemplate)
                : context.CanReadPdfs ? ActionAvailability.Available
                : ActionAvailability.Blocked(CannotReadPdfs, ActionId.InsertPhoto),

            // ---- The selected thing -------------------------------------------------------------
            ActionId.DeleteFrame => context.HasFrameSelection
                ? ActionAvailability.Available
                : ActionAvailability.NotApplicable(ChooseSomething),

            // M71, reachable the same two ways and for the same reason as ReplacePicture below. The
            // "what's next" card offers this as "Fill in the meeting date on the cover", and that
            // card is drawn ONLY with nothing chosen and nothing being typed — so in that state the
            // command had no way to work at all. With nothing chosen, the shell finds the cover
            // heading, turns to it, chooses it and says so out loud.
            ActionId.EditWidget =>
                context.CoverDateMissing && !context.HasFrameSelection && !context.IsEditingText
                    ? ActionAvailability.Available
                    : EvaluateWidget(context, ActionAvailability.Available),
            ActionId.EditWidgetList => EvaluateWidget(
                context,
                context.WidgetHasListEditor
                    ? ActionAvailability.Available
                    : ActionAvailability.NotApplicable("There is no list in this item to edit.")),
            ActionId.FitToContents => context.Selection == SelectionKind.Widget
                ? ActionAvailability.Available
                : ActionAvailability.NotApplicable(NeedsListItem),

            // Reachable two ways (M13): with the list selected, and — because that is where the
            // user is actually standing when they are told the list has gone stale — from the
            // "what's next" card with nothing selected at all. The shell finds the stale list.
            // M75 (e) defect 2: an empty list reaches it the second way too. Without this the
            // "Fill in the birthday list" row on the card would offer a button that can only refuse,
            // which gate 24 forbids and which is the shape this milestone is about.
            ActionId.SyncBirthdays =>
                context.WidgetTypeId == BirthdayListTypeId
                    ? EvaluateWidget(context, EvaluateBirthdaySync(context))
                    : context.BirthdayListIsStale || context.BirthdayListIsEmpty
                        ? EvaluateBirthdaySync(context)
                        : ActionAvailability.NotApplicable(NeedsBirthdayList),

            // M19, reachable the same two ways and for the same reason.
            ActionId.SyncOfficers =>
                context.WidgetTypeId == OfficersTableTypeId
                    ? EvaluateWidget(context, EvaluateOfficersSync(context))
                    : context.OfficersTableIsStale
                        ? EvaluateOfficersSync(context)
                        : ActionAvailability.NotApplicable(NeedsOfficersTable),

            // ---- Pictures -------------------------------------------------------------------------
            // Putting one in and describing one are the two things an EMPTY frame can still do;
            // everything else needs a picture to work on, and says so by name rather than greying.
            // Replace is reachable two ways, like M13's birthday sync: with a picture frame chosen,
            // and — because that is where the user is standing when the "what's next" card tells
            // them the photo pages are still empty — with nothing chosen at all. The shell finds the
            // first empty frame in that case.
            ActionId.ReplacePicture =>
                context.Selection == SelectionKind.Photo
                    ? ActionAvailability.Available

                    // M72: a drawing is not a frame with something in it — it IS the thing, so there
                    // is nothing to put a picture into. Said by name rather than left to the
                    // "choose a picture first" sentence, which would be untrue of what is chosen.
                    : context.Selection == SelectionKind.Drawing
                        ? ActionAvailability.NotApplicable(
                            "This is a drawing, not a frame with a picture in it. To put a photograph "
                            + "here instead, take the drawing off the page and use \"A picture\".")
                        : context.HasPicturePlaceholder && !context.HasFrameSelection && !context.IsEditingText
                            ? ActionAvailability.Available
                            : ActionAvailability.NotApplicable(NeedsPicture),
            // M72: a drawing is described. It arrives already described, and this is how that
            // sentence is corrected — withholding it would put the one thing a screen-reader user
            // depends on out of their reach.
            ActionId.DescribePicture =>
                context.Selection is SelectionKind.Photo or SelectionKind.Drawing
                    ? ActionAvailability.Available
                    : ActionAvailability.NotApplicable(NeedsPicture),

            // M72: and captioned. A caption is words printed under a frame — it is about the page,
            // not about pixels, and an emblem on a cover is exactly the kind of thing a committee
            // writes a line under.
            ActionId.CaptionPicture => context.Selection == SelectionKind.Drawing
                ? ActionAvailability.Available
                : context.Selection != SelectionKind.Photo
                    ? ActionAvailability.NotApplicable(NeedsPicture)
                    : context.SelectedPictureIsEmpty
                        ? ActionAvailability.Blocked(EmptyPictureFrame, ActionId.ReplacePicture)
                        : ActionAvailability.Available,

            // M72: the four that are about pixels are withheld from a drawing, by name and with a
            // reason of its own. FixPhoto's Sobel-and-skin-tone auto-crop, the brightness and
            // auto-levels recipe, and the crop-positioning window all act on an ImageRecipe that a
            // drawing does not have — offering them would be offering something that cannot work.
            ActionId.FixPhoto or ActionId.AdjustPhoto or ActionId.PositionPicture =>
                context.Selection == SelectionKind.Drawing
                    ? ActionAvailability.NotApplicable(DrawingIsNotAPhotograph)
                    : context.Selection != SelectionKind.Photo
                        ? ActionAvailability.NotApplicable(NeedsPicture)
                        : context.SelectedPictureIsEmpty
                            ? ActionAvailability.Blocked(EmptyPictureFrame, ActionId.ReplacePicture)
                            : ActionAvailability.Available,

            // M23: absent unless the picture actually has something stale to dismiss — never greyed,
            // since there is no reason to give for a notice that is not showing.
            ActionId.DismissCropNotice => context.PictureCropIsStale
                ? ActionAvailability.Available
                : ActionAvailability.NotApplicable(NeedsPicture),

            // ---- How text flows -------------------------------------------------------------------
            ActionId.ToggleWrap => context.HasFrameSelection
                ? ActionAvailability.Available
                : ActionAvailability.NotApplicable(ChooseSomething),
            ActionId.LinkFrames => context.SelectionIsTextFrame
                ? ActionAvailability.Available
                : ActionAvailability.NotApplicable(NeedsTextFrame),
            ActionId.UnlinkFrames => !context.SelectionIsTextFrame
                ? ActionAvailability.NotApplicable(NeedsTextFrame)
                : context.SelectionIsLinked
                    ? ActionAvailability.Available
                    : ActionAvailability.Blocked(
                        "This frame does not continue into another one yet.", ActionId.LinkFrames),
            // M71, reachable the same two ways and for the same reason as ReplacePicture below. The
            // "what's next" card offers this as "Make the writing fit" when something in the
            // newsletter has more writing than fits, and that card is drawn ONLY with nothing chosen
            // and nothing being typed. With nothing chosen, the shell finds the first story that has
            // run out of room, turns to it, chooses it and says so out loud.
            ActionId.AutoFlow => !context.SelectionIsTextFrame
                ? context.HasOversetText && !context.HasFrameSelection && !context.IsEditingText
                    ? ActionAvailability.Available
                    : ActionAvailability.NotApplicable(NeedsTextFrame)
                : context.CanAutoFlow
                    ? ActionAvailability.Available
                    : ActionAvailability.Blocked("All of this writing already fits, so there is nothing to move."),

            // ---- Arranging ------------------------------------------------------------------------
            ActionId.BringForward or ActionId.SendBackward
                or ActionId.BringToFront or ActionId.SendToBack => context.HasFrameSelection
                ? ActionAvailability.Available
                : ActionAvailability.NotApplicable(ChooseSomething),

            // M21. Lining things up is meaningless with one thing, so with one thing chosen these
            // are ABSENT from the panel rather than greyed in it — the M11 rule, which is also why
            // a single selection's panel looks exactly as it did before this milestone.
            ActionId.AlignLeft or ActionId.AlignCentres or ActionId.AlignRight
                or ActionId.AlignTop or ActionId.AlignMiddles or ActionId.AlignBottom =>
                context.SelectionCount >= 2
                    ? ActionAvailability.Available
                    : ActionAvailability.NotApplicable(NeedsTwoThings),
            ActionId.DistributeHorizontally or ActionId.DistributeVertically =>
                context.SelectionCount >= 3
                    ? ActionAvailability.Available
                    : ActionAvailability.NotApplicable(NeedsThreeThings),

            // ---- Pages ----------------------------------------------------------------------------
            ActionId.NextPage => !context.HasDocument
                ? RequiresDocument(context)
                : context.PageIndex < context.PageCount - 1
                    ? ActionAvailability.Available
                    : ActionAvailability.Blocked("This is the last page."),
            ActionId.PreviousPage => !context.HasDocument
                ? RequiresDocument(context)
                : context.PageIndex > 0
                    ? ActionAvailability.Available
                    : ActionAvailability.Blocked("This is the first page."),
            ActionId.AddPage => RequiresDocument(context),
            ActionId.RemovePage => !context.HasDocument
                ? RequiresDocument(context)
                : context.PageCount > 1
                    ? ActionAvailability.Available
                    : ActionAvailability.Blocked("A newsletter needs at least one page."),
            ActionId.MovePageEarlier => !context.HasDocument
                ? RequiresDocument(context)
                : context.PageIndex > 0
                    ? ActionAvailability.Available
                    : ActionAvailability.Blocked("This page is already first."),
            ActionId.MovePageLater => !context.HasDocument
                ? RequiresDocument(context)
                : context.PageIndex < context.PageCount - 1
                    ? ActionAvailability.Available
                    : ActionAvailability.Blocked("This page is already last."),

            // M76 (g). The one availability rule in the catalog that answers for a command with a
            // PARAMETER, and it is written to answer the question it can actually answer: not "can
            // you go to page four", which depends on a number this layer never sees, but "is there
            // anywhere to go at all". A one-page newsletter is the whole of the no — and it is
            // refused in words rather than by a greyed rail, because a rail whose tiles go grey is
            // the exact thing M11 exists to remove (docs/M76-spec.md §6).
            ActionId.GoToPage => !context.HasDocument
                ? RequiresDocument(context)
                : context.PageCount > 1
                    ? ActionAvailability.Available
                    : ActionAvailability.Blocked(
                        "There is only one page, so there is nowhere else to go."),

            // ---- Looking at it --------------------------------------------------------------------
            ActionId.ZoomIn or ActionId.ZoomOut or ActionId.ActualSize or ActionId.FitPage =>
                RequiresDocument(context),

            _ => throw new ArgumentOutOfRangeException(
                nameof(actionId), actionId, "No availability rule is written for this action."),
        };
    }

    /// <summary>
    /// What the panel shows for the current selection: the actions that are about this thing, with
    /// the ones that do not apply left out entirely rather than greyed (PLAN.md §6).
    /// </summary>
    public static IReadOnlyList<ActionOffer> ForSelection(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var offers = new List<ActionOffer>();
        foreach (ActionGroup group in PanelGroups(context))
        {
            foreach (EditorAction action in AllActions.Where(a => a.Group == group))
            {
                ActionAvailability availability = Evaluate(action.Id, context);
                if (availability.Kind != ActionAvailabilityKind.NotApplicable)
                {
                    offers.Add(new ActionOffer(action, availability));
                }
            }
        }

        return offers;
    }

    /// <summary>
    /// Which of these offers — if any — the panel draws in the primary treatment: <b>at most one
    /// per <see cref="ActionGroup"/></b>, whatever the selection (PLAN.md §11 M76 (f)).
    ///
    /// <para>M16 defined that treatment as "the offer you probably came for", singular, and then
    /// left every action to assert it for itself. Fifteen did, and <c>action-panel-photo.png</c>
    /// is what that came to: three navy-and-gold slabs stacked one above the other in a single
    /// panel. Three simultaneous offers are not three answers to that question; they are the
    /// absence of one, and the gold bar is the most expensive ornament in the palette being spent
    /// on all of them at once.</para>
    ///
    /// <para><b>The rule, whole:</b> an action with <see cref="EditorAction.PrimaryRank"/> of zero
    /// never wins; among the ranked actions <i>present in these offers</i>, the lowest rank in each
    /// group wins; and two actions in a group cannot share a rank, because the static constructor
    /// above refuses to let the catalog exist if they do. Nothing here depends on the order the
    /// offers arrive in — hand this the same set shuffled and it answers the same thing, which is
    /// the property "the first one that happens to be enumerated" would not have had.</para>
    ///
    /// <para><b>Presence, not availability, is what the ranks are filtered by.</b> A blocked offer
    /// can still win its group and still be drawn primary, exactly as it was before M76: the panel
    /// never greys anything (M11), a blocked button carries its reason in words and stays pressable,
    /// and demoting the emphasis of a refusal would make the panel jump its gold bar about as the
    /// selection changed — churn, on a surface built for people who need it to stay still.</para>
    ///
    /// <para>Everything that does not win is drawn with the ordinary action treatment. Losing is
    /// not being disabled, and it is not being unstyled — <c>ActionSurfaceTests</c> walks every
    /// window proving that no app-made button carries neither treatment.</para>
    /// </summary>
    public static IReadOnlySet<string> PrimaryOffers(IEnumerable<ActionOffer> offers)
    {
        ArgumentNullException.ThrowIfNull(offers);

        var winners = new Dictionary<ActionGroup, EditorAction>();
        foreach (ActionOffer offer in offers)
        {
            EditorAction action = offer.Action;
            if (action.PrimaryRank <= 0)
            {
                continue;
            }

            if (winners.TryGetValue(action.Group, out EditorAction? held)
                && held.PrimaryRank <= action.PrimaryRank)
            {
                continue;
            }

            winners[action.Group] = action;
        }

        return winners.Values.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The panel's heading, and the sentence a screen reader hears when the selection changes.</summary>
    public static string DescribeSelection(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // M21: more than one thing chosen is its own answer. Naming the kind of the first one
        // ("A photo is selected") while three things are highlighted would be a lie about two of
        // them, and the panel heading is a live region a screen reader reads out.
        if (context.SelectionCount > 1)
        {
            return $"{context.SelectionCount} things are selected";
        }

        return context.Selection switch
        {
            SelectionKind.Text => "You are writing",
            SelectionKind.TextFrame => "A box of writing is chosen",
            SelectionKind.Photo => "A photo is selected",
            SelectionKind.Widget => $"{context.WidgetDisplayName ?? "An item"} is selected",

            // M72. "A shape is selected" is what the fall-through would have said, and it is the
            // wrong word for the square and compasses somebody just put on their cover.
            SelectionKind.Drawing => "A drawing is selected",
            SelectionKind.Shape => "A shape is selected",
            _ => context.HasDocument ? "Nothing is selected" : "No newsletter is open",
        };
    }

    /// <summary>
    /// A sentence under the panel's heading saying how this thing is edited, or null when the
    /// heading already says everything there is to say (PLAN.md §11 M17).
    ///
    /// <para>It exists for the widgets. Their text is drawn from a payload rather than from a
    /// story, so a click inside one lands on no paragraph at all and nothing happens — correct by
    /// the M7 design, and indistinguishable from a broken program. The panel is where the user is
    /// already looking after a click, so it is where the answer goes.</para>
    /// </summary>
    public static string? DescribeSelectionHint(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // M21: with several things chosen, the panel says what having chosen them is FOR, and how
        // to change what is in the set. Shift+click is the only way into this state and nothing
        // else in the app teaches it.
        if (context.SelectionCount > 1)
        {
            return "Hold Shift and click to add another one, or to take one out. "
                + "The lining-up commands below act on all of them at once.";
        }

        // M18: an empty picture frame has the same problem the widgets have — clicking it does
        // something invisible — with the extra sting that the photo template ships three of them and
        // nothing in the app could fill one until M18.
        if (context.Selection == SelectionKind.Photo)
        {
            return context.SelectedPictureIsEmpty
                ? "There is no picture in this frame yet. Use 'Put a picture here…' — "
                  + "or just double-click it on the page."
                : null;
        }

        if (context.Selection != SelectionKind.Widget)
        {
            return null;
        }

        if (!context.CanEditWidget)
        {
            return null;
        }

        // M19: the owner's complaint was that the widgets are not driven by the address book. Half
        // of that was false, and the half that was true was that nothing ANYWHERE said so. A list
        // that came out of the address book now says it, on the panel, in the widget editor, and in
        // the "what's next" card.
        string hint = "This is a filled-in list. Use 'Change what this says…' to edit it — "
            + "or just double-click it on the page.";
        return context.SelectionFilledInFromRoster is { } filledIn
            ? DescribeFilledIn(filledIn) + " " + hint
            : hint;
    }

    /// <summary>
    /// "Filled in from your address book on 14 July 2026." — or without the date when the stamp is
    /// missing or unreadable, because a sentence with an empty gap in it reads worse than no date.
    /// One place, so the panel and the widget editor say the same words (M19).
    /// </summary>
    public static string DescribeFilledIn(string? whenText) =>
        string.IsNullOrWhiteSpace(whenText)
            ? "Filled in from your address book."
            : $"Filled in from your address book on {whenText}.";

    /// <summary>
    /// Which groups the panel shows for this selection. Ordered: the thing you most likely want
    /// first. With nothing selected the panel shows the "what's next" card and the ways of putting
    /// something new on the page.
    /// </summary>
    private static ActionGroup[] PanelGroups(ActionContext context) => context.Selection switch
    {
        SelectionKind.Text => [ActionGroup.Text, ActionGroup.Edit],
        SelectionKind.TextFrame => [ActionGroup.TextFlow, ActionGroup.Item, ActionGroup.Arrange],
        SelectionKind.Photo =>
            [ActionGroup.Picture, ActionGroup.Item, ActionGroup.TextFlow, ActionGroup.Arrange],
        SelectionKind.Widget => [ActionGroup.Item, ActionGroup.TextFlow, ActionGroup.Arrange],

        // M72: the same groups a photo gets, in the same order. "This picture" earns its place
        // because two of its commands — the caption and the description — are granted to a drawing;
        // the four that are about pixels appear in it saying why they do not apply, which is M11
        // working rather than a group that should have been hidden.
        SelectionKind.Drawing =>
            [ActionGroup.Picture, ActionGroup.Item, ActionGroup.TextFlow, ActionGroup.Arrange],
        SelectionKind.Shape => [ActionGroup.Item, ActionGroup.TextFlow, ActionGroup.Arrange],
        _ => context.HasDocument ? [ActionGroup.Insert] : [],
    };

    /// <summary>The heading each group gets in the panel — plain language, never a class name.</summary>
    public static string DescribeGroup(ActionGroup group) => group switch
    {
        ActionGroup.Newsletter => "This newsletter",
        ActionGroup.Edit => "Editing",
        ActionGroup.Text => "The words",
        ActionGroup.Insert => "Add to the page",
        ActionGroup.Item => "The thing you chose",
        ActionGroup.Picture => "This picture",
        ActionGroup.TextFlow => "How text flows",
        ActionGroup.Arrange => "Front and back",
        ActionGroup.Page => "Pages",
        ActionGroup.View => "Looking at it",
        ActionGroup.People => "Your address book",
        ActionGroup.Everything => "Everything on this computer",
        _ => "Help",
    };

    /// <summary>
    /// Whether the address book has anything to contribute to this month (M13). Both refusals name
    /// the door out: an empty book wants importing, and a book with nobody born this month is not
    /// broken — it is simply a quiet month, and saying so is better than a grey button.
    /// </summary>
    private static ActionAvailability EvaluateBirthdaySync(ActionContext context)
    {
        // M75 (c), and it goes FIRST. With no issue date the projection filters the address book to
        // January, so the next branch down would report "nobody in your address book has a birthday
        // in this issue's month" — a confident falsehood about a book full of birthdays, and the
        // sentence the owner was actually looking at when he reported this.
        if (!context.IssueDateChosen)
        {
            return ActionAvailability.Blocked(
                NoIssueDate("the birthday list is worked out from it"),
                ActionId.SetIssueDate);
        }

        // M75 (f), and it goes before the empty test, because an unreadable book LOOKS empty from
        // here: RosterService hands out an empty placeholder when the file will not load. Told it
        // was empty, the user imports his list again over a file that was only locked.
        // M75 (f), and it goes before the empty test, because an unreadable book LOOKS empty from
        // here: RosterService hands out an empty placeholder when the file will not load. Told it
        // was empty, the user imports his list again over a file that was only locked.
        if (context.RosterCouldNotBeRead)
        {
            return ActionAvailability.Blocked(CouldNotReadTheAddressBook);
        }

        if (context.RosterCount == 0)
        {
            return ActionAvailability.Blocked(
                "Your address book is empty, so there are no birthdays to bring in. Import your "
                + "member list first, or type the birthdays in yourself.",
                ActionId.ImportPeople);
        }

        if (context.RosterBirthdaysThisMonth == 0)
        {
            // M75 (e) defect 1: it NAMES THE MONTH. "This issue's month" is read as the month the
            // user thinks he is working in, which is exactly how a wrong issue date hides — the
            // owner read this sentence about a July address book and a January newsletter and it
            // told him nothing. Naming January would have ended the hunt in one glance.
            return ActionAvailability.Blocked(
                $"Nobody in your address book has a birthday in {MonthName(context.IssueMonth)}, "
                + "which is the month this issue is for. You can still type a birthday in yourself, "
                + "or add the missing dates in the People window.",
                ActionId.ShowPeople);
        }

        return ActionAvailability.Available;
    }

    /// <summary>
    /// Whether the address book has anything to say about who holds office (M19). Both refusals name
    /// the door out, exactly as the birthday rule does: an empty book wants importing, and a book
    /// where nobody's office field is filled in is not broken — it is simply a book nobody has
    /// recorded offices in, and saying so is better than a grey button.
    /// </summary>
    private static ActionAvailability EvaluateOfficersSync(ActionContext context)
    {
        // M75 (f): the same third branch, for the same reason. Every rule that can say "your address
        // book is empty" must first be sure that it is.
        // M75 (f): the same third branch, for the same reason. Every rule that can say "your address
        // book is empty" must first be sure that it is.
        if (context.RosterCouldNotBeRead)
        {
            return ActionAvailability.Blocked(CouldNotReadTheAddressBook);
        }

        if (context.RosterCount == 0)
        {
            return ActionAvailability.Blocked(
                "Your address book is empty, so there are no officers to fill in. Import your member "
                + "list first, or type the officers in yourself.",
                ActionId.ImportPeople);
        }

        if (context.RosterOfficesFilledIn == 0)
        {
            return ActionAvailability.Blocked(
                "Nobody in your address book has an office written against his name, so there is "
                + "nothing to fill in. Open the People window and write in who holds each office.",
                ActionId.ShowPeople);
        }

        return ActionAvailability.Available;
    }

    /// <summary>
    /// M75 (c): a newsletter, and a newsletter that knows which issue it is.
    ///
    /// <para>The eight commands that route through here are the ones that put the issue's month in
    /// front of a reader or go looking for it in the archive. Before M75 every one of them ran
    /// happily against the model's default and produced <c>" 2000-01.pdf"</c>, a mail subject
    /// reading "Trestle Board — January 2000", and an archive search for January 1999 that told the
    /// user their folder was empty <i>after</i> making them choose it.</para>
    ///
    /// <para>The refusal names the consequence rather than the field, because "issue metadata is
    /// unset" is not a sentence this audience should ever have to read — and it carries a
    /// <c>RemedyId</c> to the one command that fixes it, which is the M11 shape
    /// <c>EvaluateBirthdaySync</c>'s <c>ImportPeople</c> remedy established.</para>
    /// </summary>
    /// <param name="because">A clause completing "…yet, and {because}."</param>
    private static ActionAvailability RequiresIssueDate(ActionContext context, string because) =>
        !context.HasDocument
            ? ActionAvailability.Blocked(NoNewsletter, ActionId.NewFromTemplate)
            : context.IssueDateChosen
                ? ActionAvailability.Available
                : ActionAvailability.Blocked(NoIssueDate(because), ActionId.SetIssueDate);

    /// <summary>
    /// M75 (f): what the user is told when the address book file is there but would not load.
    ///
    /// <para>It carries no <c>RemedyId</c> on purpose. Every remedy in this catalog is a command the
    /// app can run, and there is nothing the app can do about a file another program is holding —
    /// offering "Import your member list" here would invite the user to overwrite a good address
    /// book he still has. The sentence says who can fix it, which is him.</para>
    /// </summary>
    public const string CouldNotReadTheAddressBook =
        "TrestleBoard could not read your address book, so it does not know who is in it. It is not "
        + "empty — the file is on this computer but would not open, which usually means another "
        + "program is using it. Close that program, then start TrestleBoard again.";

    /// <summary>
    /// The issue's month by name, for the refusals that must say WHICH month (M75 (e)).
    ///
    /// <para>Invariant culture, like every other month name the app prints: the newsletter is
    /// written in English and the sentence is read beside a cover that says "July".</para>
    /// </summary>
    public static string MonthName(int month) =>
        month is >= 1 and <= 12
            ? System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month)
            : "this issue's month";

    /// <summary>The one sentence, so eight commands cannot drift into eight ways of saying it.</summary>
    private static string NoIssueDate(string because) =>
        "TrestleBoard does not know which month and year this newsletter is for yet, and "
        + because
        + ". Use “Which issue is this?…” to say — it opens the cover heading and asks.";

    private static ActionAvailability RequiresDocument(ActionContext context) =>
        context.HasDocument
            ? ActionAvailability.Available
            : ActionAvailability.Blocked(NoNewsletter, ActionId.NewFromTemplate);

    /// <summary>
    /// The widget rules all share one shape: is a widget selected at all, was it made by a newer
    /// TrestleBoard, and only then the action's own question.
    /// </summary>
    private static ActionAvailability EvaluateWidget(ActionContext context, ActionAvailability whenEditable)
    {
        if (context.Selection != SelectionKind.Widget)
        {
            return ActionAvailability.NotApplicable(NeedsListItem);
        }

        return context.CanEditWidget
            ? whenEditable
            : ActionAvailability.Blocked(
                "This item was made by a newer TrestleBoard than this one, so its questions are not "
                + "known here. You can still move, resize or delete it.",
                ActionId.CheckForUpdates);
    }
}
