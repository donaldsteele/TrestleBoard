using Avalonia.Controls;
using TrestleBoard.Editing;
using TrestleBoard.Editing.Actions;

namespace TrestleBoard.App.Actions;

/// <summary>
/// The other half of M11's split: <see cref="ActionCatalog"/> owns "can I, and if not, why not",
/// and this owns "how" — one map from an action id to the code that performs it (PLAN.md §11 M11).
///
/// Everything the user can press goes through <see cref="RunAsync"/>: the menu bar, the panel, the
/// right-click flyout and the keyboard table. That is what makes explained refusal a property of the
/// app rather than of each surface — an action that cannot run says why, once, here.
/// </summary>
internal sealed class ActionRunner
{
    /// <summary>Which widget each Insert action puts on the page.</summary>
    private static readonly (string ActionId, string WidgetTypeId)[] WidgetInserts =
    [
        (ActionId.InsertOfficers, "officersTable"),
        (ActionId.InsertBirthdays, "birthdayList"),
        (ActionId.InsertCommittees, "committeeList"),
        (ActionId.InsertDistrictCalendar, "districtCalendar"),
        (ActionId.InsertMonthCalendar, "monthCalendar"),
        (ActionId.InsertEventCard, "eventCard"),
        (ActionId.InsertCoverBanner, "coverBanner"),
        (ActionId.InsertSimpleList, "simpleList"),
    ];

    private readonly MainWindow _window;
    private readonly Dictionary<string, Func<Control?, Task<bool>>> _handlers;

    internal ActionRunner(MainWindow window)
    {
        _window = window;
        _handlers = new Dictionary<string, Func<Control?, Task<bool>>>(StringComparer.Ordinal)
        {
            // ---- The newsletter itself ----------------------------------------------------------
            [ActionId.Open] = Async(() => window.OpenNewsletterAsync()),
            [ActionId.OpenSample] = Sync(window.OpenSample),
            [ActionId.NewFromTemplate] = Async(() => window.NewFromTemplateAsync()),
            [ActionId.StartFromLastMonth] = Async(() => window.StartFromLastMonthAsync()),
            [ActionId.Save] = Async(() => window.SaveAsync()),
            [ActionId.SaveAs] = Async(() => window.SaveAsAsync()),
            [ActionId.RestoreDocument] = Async(() => window.RestoreEarlierVersionAsync()),
            [ActionId.ReviewNewsletter] = Sync(window.ShowReview),
            [ActionId.CheckSpelling] = Sync(window.ShowSpellingCheck),
            [ActionId.ReadAloud] = Sync(window.ReadItBackToMe),
            [ActionId.ShowLastYear] = Async(() => window.ShowLastYearAsync()),
            [ActionId.ExportPdf] = Async(() => window.ExportPdfAsync()),
            [ActionId.ExportDraftPdf] = Async(() => window.ExportDraftPdfAsync()),
            [ActionId.ExportPagePicture] = Async(() => window.ExportPageAsPictureAsync()),
            [ActionId.PrintPdf] = Async(() => window.PrintTheLastPdfAsync()),
            [ActionId.SendIt] = Async(() => window.SendItAsync()),
            [ActionId.SaveAsTemplate] = Async(() => window.SaveAsTemplateAsync()),
            [ActionId.ManageTemplates] = Async(() => window.ShowMyTemplatesAsync()),
            [ActionId.SetIssueDate] = Async(() => window.AskWhichIssueThisIsAsync()),
            [ActionId.Exit] = Sync(window.Close),

            // ---- Edit -----------------------------------------------------------------------------
            [ActionId.Undo] = Sync(window.Undo),
            [ActionId.Redo] = Sync(window.Redo),
            [ActionId.Cut] = Async(() => window.CutAsync()),
            [ActionId.Copy] = Async(() => window.CopyAsync()),
            [ActionId.Paste] = Async(() => window.PasteAsync()),
            [ActionId.SelectAll] = Sync(window.SelectAllText),
            [ActionId.SelectAllFrames] = Sync(window.SelectEverythingOnThisPage),
            [ActionId.AddNextToSelection] = Sync(() => window.AlsoChoose(forward: true)),
            [ActionId.AddPreviousToSelection] = Sync(() => window.AlsoChoose(forward: false)),
            [ActionId.Find] = Sync(() => window.ShowFind(replacing: false)),
            [ActionId.Replace] = Sync(() => window.ShowFind(replacing: true)),

            // ---- Text -----------------------------------------------------------------------------
            [ActionId.Bold] = Sync(window.ToggleBold),
            [ActionId.Italic] = Sync(window.ToggleItalic),
            [ActionId.BulletList] = Sync(() => window.ToggleList(Core.Model.ListKinds.Bullet)),
            [ActionId.NumberList] = Sync(() => window.ToggleList(Core.Model.ListKinds.Number)),
            [ActionId.ParagraphStyle] = source =>
            {
                window.ShowParagraphStyles(source);
                return Task.FromResult(true);
            },

            // ---- Fonts and sizes (M14) --------------------------------------------------------------
            [ActionId.FontsAndStyles] = Async(() => window.ShowTextStylesAsync()),
            [ActionId.BiggerText] = Sync(() => window.StepTextSize(+1)),
            [ActionId.SmallerText] = Sync(() => window.StepTextSize(-1)),
            [ActionId.FontJustHere] = Async(() => window.UseFontJustHereAsync()),
            [ActionId.ClearFontOverride] = Sync(window.ClearFontOverrideHere),

            // ---- Putting things on the page --------------------------------------------------------
            [ActionId.AddTextFrame] = Sync(window.AddTextFrame),
            [ActionId.AddRule] = Sync(window.AddRuleAcrossThePage),
            [ActionId.AlignTextLeft] = Sync(() => window.AlignText(Core.Model.TextAlignment.Left)),
            [ActionId.AlignTextCentre] = Sync(() => window.AlignText(Core.Model.TextAlignment.Center)),
            [ActionId.AlignTextRight] = Sync(() => window.AlignText(Core.Model.TextAlignment.Right)),
            [ActionId.PageSetup] = Async(() => window.ChangeThePaperAsync()),
            [ActionId.ChangeCase] = Async(() => window.ChangeCaseAsync()),
            [ActionId.WordCount] = Sync(window.SayHowManyWords),
            [ActionId.InsertSymbol] = Async(() => window.InsertSymbolAsync()),
            [ActionId.WritingLook] = Async(() => window.ChangeWritingLookAsync()),
            [ActionId.AddBox] = Async(() => window.AddBoxAsync()),
            [ActionId.ShapeColours] = Async(() => window.ChangeShapeColoursAsync()),
            [ActionId.PositionAndSize] = Async(() => window.SayExactlyWhereItGoesAsync()),
            [ActionId.Duplicate] = Sync(window.DuplicateSelected),
            [ActionId.MoveToNextPage] = Sync(window.MoveSelectionToNextPage),
            [ActionId.MoveToPreviousPage] = Sync(window.MoveSelectionToPreviousPage),
            [ActionId.ToggleLocked] = Sync(window.ToggleLocked),
            [ActionId.ToggleBorder] = Sync(window.ToggleBorder),
            [ActionId.ToggleShade] = Sync(window.ToggleShade),
            [ActionId.InsertPhrase] = Async(() => window.InsertPhraseAsync()),
            [ActionId.SavePhrase] = Async(() => window.SavePhraseAsync()),
            [ActionId.InsertPhoto] = Async(() => window.InsertPhotoAsync()),

            // ---- The selected thing ----------------------------------------------------------------
            [ActionId.DeleteFrame] = Sync(window.DeleteSelectedFrame),
            [ActionId.EditWidget] = Async(() => window.EditWidgetAsync(grid: false)),
            [ActionId.EditWidgetList] = Async(() => window.EditWidgetAsync(grid: true)),
            [ActionId.FitToContents] = Sync(window.FitWidgetToContents),
            [ActionId.SyncBirthdays] = Async(() => window.SyncBirthdaysAsync()),
            [ActionId.SyncOfficers] = Async(() => window.SyncOfficersAsync()),

            // ---- Pictures --------------------------------------------------------------------------
            [ActionId.FixPhoto] = Sync(window.FixPhoto),
            [ActionId.AdjustPhoto] = Async(() => window.AdjustPhotoAsync()),
            [ActionId.ReplacePicture] = Async(() => window.ReplacePictureAsync()),
            [ActionId.PositionPicture] = Async(() => window.PositionPictureAsync()),
            [ActionId.DismissCropNotice] = Sync(window.DismissCropNotice),
            [ActionId.CaptionPicture] = Async(() => window.CaptionPictureAsync()),
            [ActionId.DescribePicture] = Async(() => window.DescribePictureAsync()),

            // ---- How text flows --------------------------------------------------------------------
            [ActionId.ToggleWrap] = Sync(window.ToggleWrap),
            [ActionId.LinkFrames] = Sync(window.BeginFrameLink),
            [ActionId.UnlinkFrames] = Sync(window.UnlinkFrames),
            [ActionId.AutoFlow] = Sync(window.AutoFlow),
            [ActionId.GrowToFit] = Sync(window.GrowTheBox),
            [ActionId.ToggleTwoColumns] = Sync(window.ToggleTwoColumns),

            // ---- Arranging -------------------------------------------------------------------------
            [ActionId.BringForward] = Sync(() => window.Restack(f => f.BringForward(), towardsFront: true)),
            [ActionId.SendBackward] = Sync(() => window.Restack(f => f.SendBackward(), towardsFront: false)),
            [ActionId.BringToFront] = Sync(() => window.Restack(f => f.BringToFront(), towardsFront: true)),
            [ActionId.SendToBack] = Sync(() => window.Restack(f => f.SendToBack(), towardsFront: false)),

            // ---- Lining things up (M21) -------------------------------------------------------
            [ActionId.AlignLeft] = Sync(() => window.AlignSelection(FrameAlignmentKind.Left)),
            [ActionId.AlignCentres] = Sync(() => window.AlignSelection(FrameAlignmentKind.CentreX)),
            [ActionId.AlignRight] = Sync(() => window.AlignSelection(FrameAlignmentKind.Right)),
            [ActionId.AlignTop] = Sync(() => window.AlignSelection(FrameAlignmentKind.Top)),
            [ActionId.AlignMiddles] = Sync(() => window.AlignSelection(FrameAlignmentKind.MiddleY)),
            [ActionId.AlignBottom] = Sync(() => window.AlignSelection(FrameAlignmentKind.Bottom)),
            [ActionId.DistributeHorizontally] = Sync(() => window.DistributeSelection(horizontal: true)),
            [ActionId.DistributeVertically] = Sync(() => window.DistributeSelection(horizontal: false)),

            // ---- Pages -----------------------------------------------------------------------------
            [ActionId.NextPage] = Sync(() => window.GoToRelativePage(+1)),
            [ActionId.PreviousPage] = Sync(() => window.GoToRelativePage(-1)),
            [ActionId.AddPage] = Sync(window.AddPage),
            [ActionId.RemovePage] = Sync(window.RemovePage),
            [ActionId.MovePageEarlier] = Sync(() => window.MovePage(-1)),
            [ActionId.MovePageLater] = Sync(() => window.MovePage(+1)),

            // M76 (g). THE ONE HANDLER IN THIS MAP THAT READS ITS SOURCE. Every other command needs
            // nothing but the id; this one needs to be told which page, and the answer rides in the
            // pressed control's Tag beside the id (see ActionTarget). That is why it is written out
            // rather than wrapped in Sync: `source` is the parameter, and the Sync helpers throw it
            // away. Its bool is the M73(f) answer — pressing the page you are already on is a real
            // "nothing happened", and saying otherwise would be the app claiming work it did not do.
            [ActionId.GoToPage] = source => Task.FromResult(window.GoToPageFrom(source)),
            [ActionId.ShowPageFooter] = Sync(window.TogglePageFooter),

            // ---- Looking at it ---------------------------------------------------------------------
            [ActionId.ZoomIn] = Sync(() => window.StepZoom(+1)),
            [ActionId.ZoomOut] = Sync(() => window.StepZoom(-1)),
            [ActionId.ActualSize] = Sync(window.ZoomToActualSize),
            [ActionId.FitPage] = Sync(window.FitPage),
            [ActionId.Settings] = Async(() => window.ShowSettingsAsync()),
            [ActionId.NextRegion] = Sync(() => window.CycleRegion(forward: true)),
            [ActionId.PreviousRegion] = Sync(() => window.CycleRegion(forward: false)),
            [ActionId.ToggleActionPanel] = Sync(window.ToggleActionPanel),
            [ActionId.TogglePageRail] = Sync(window.TogglePageRail),
            [ActionId.ShowFontChanges] = Sync(window.ToggleShowFontChanges),
            [ActionId.ShowSpelling] = Sync(window.ToggleShowSpelling),
            [ActionId.ShowMargins] = Sync(window.ToggleShowMargins),

            // ---- The address book (M12) --------------------------------------------------------------
            [ActionId.ShowPeople] = Async(() => window.ShowPeopleAsync()),
            [ActionId.ImportPeople] = Async(() => window.ImportPeopleAsync()),
            [ActionId.ExportPeople] = Async(() => window.ExportPeopleAsync()),
            [ActionId.UndoPeopleChange] = Sync(window.UndoPeopleChange),
            [ActionId.RestorePeople] = Async(() => window.RestorePeopleAsync()),

            // ---- Help ------------------------------------------------------------------------------
            [ActionId.CheckForUpdates] = Async(() => window.CheckForUpdatesForTest(userAsked: true)),
            [ActionId.SaveProblemReport] = Async(() => window.SaveAProblemReportAsync(null)),
            [ActionId.About] = Async(() => window.ShowAboutAsync()),
            [ActionId.FontLicences] = Async(() => window.ShowFontLicencesAsync()),
            [ActionId.Licence] = Async(() => window.ShowLicenceAsync()),
            [ActionId.ShowExampleIssue] = Sync(window.OpenIssueSample),
            [ActionId.InsertEmblem] = Async(() => window.InsertEmblemAsync()),
            [ActionId.BringInWriting] = Async(() => window.BringInWritingAsync()),
            [ActionId.BringInPdfPage] = Async(() => window.BringInPdfPageAsync()),
            [ActionId.PackUpForSuccessor] = Async(() => window.PackUpForSuccessorAsync()),
            [ActionId.BringInAPack] = Async(() => window.BringInAPackAsync()),
            [ActionId.HowDoI] = Sync(window.ShowHowDoI),
            [ActionId.ShowTheTour] = Async(() => window.ShowTheTourAsync(becauseTheyAsked: true)),
        };

        foreach ((string actionId, string widgetTypeId) in WidgetInserts)
        {
            string typeId = widgetTypeId;
            _handlers[actionId] = Async(() => window.InsertWidgetAsync(typeId));
        }
    }

    /// <summary>
    /// Set by the headless keyboard audit: records which action a key press reached and stops it
    /// short of running, so the test can press Ctrl+O without a file dialog opening on the runner.
    /// </summary>
    internal Func<string, bool>? InterceptorForTest { get; set; }

    /// <summary>The last action id this runner was asked for, whether or not it could run.</summary>
    internal string? LastActionForTest { get; private set; }

    /// <summary>Every action id in the catalog that this runner knows how to perform.</summary>
    internal IEnumerable<string> HandledIds => _handlers.Keys;

    internal bool Handles(string actionId) => _handlers.ContainsKey(actionId);

    /// <summary>
    /// Drops a handler, so a test can reach the "no handler at all" branch of
    /// <see cref="RunAsync"/> (PLAN.md §11 M73(f)). There is no other way in: the catalog throws on
    /// an action id it does not know, and <c>MenuIndexTests</c> makes a catalog action with no
    /// handler a build failure — which is exactly why that branch has to be tested deliberately
    /// rather than waited for.
    /// </summary>
    internal void ForgetHandlerForTest(string actionId) => _handlers.Remove(actionId);

    /// <summary>
    /// Runs an action, or explains why it cannot run. The refusal goes to the status bar, which is
    /// already a polite live region, so a screen-reader user hears the reason rather than nothing
    /// (PLAN.md §6).
    /// </summary>
    /// <param name="actionId">One of the <see cref="ActionId"/> constants.</param>
    /// <param name="source">The control the user pressed, for actions that open a flyout beside it.</param>
    /// <returns>
    /// Which of three things happened — refused, nothing, or something — and, for a refusal, the
    /// very sentence the status bar was given: the catalog's reason, the apology after a handler
    /// threw, or the bug report for an action id nobody implemented.
    ///
    /// <para>M70(b): four non-modal windows sit over the status bar, so an answer delivered only
    /// there is invisible to somebody looking at one of them. Handing the sentence back is what lets
    /// those windows repeat it where the user is actually looking. Every other caller starts a
    /// command with <c>_ = RunAsync(...)</c> and is unaffected.</para>
    ///
    /// <para>M73(f): this used to be a <c>string?</c> whose null meant "ran and nothing threw",
    /// which is not the same claim as "it happened" — and two windows made the second claim out of
    /// the first. A cancelled picker, a cancelled wizard, a handler that early-returned and an
    /// action id with no handler at all were all indistinguishable from success.</para>
    /// </returns>
    internal async Task<ActionOutcome> RunAsync(string actionId, Control? source = null)
    {
        LastActionForTest = actionId;

        // M77: every surface — the menu bar, the panel, the flyout, the keyboard table — comes
        // through here, so the trail is fed in one place and no surface can be forgotten. Recorded
        // BEFORE the availability check, because "asked for a thing the app refused" is exactly the
        // sort of step a maintainer reading the report needs to see.
        Diagnostics.ActionTrail.Shared.Record(actionId, DateTimeOffset.Now);

        if (InterceptorForTest is { } intercept && intercept(actionId))
        {
            // The keyboard audit stops the command short of running, so by construction nothing
            // happened — which is now something this can say rather than having to imply success.
            return ActionOutcome.Nothing;
        }

        ActionAvailability availability = ActionCatalog.Evaluate(actionId, _window.CurrentActionContext);
        if (!availability.IsAvailable)
        {
            _window.Announce(availability.Reason);
            return ActionOutcome.Refused(availability.Reason);
        }

        if (!_handlers.TryGetValue(actionId, out Func<Control?, Task<bool>>? handler))
        {
            // M73(f). This was `TryGetValue` inside an `if` with no `else`: an action id nobody had
            // implemented ran nothing, said nothing, and was reported to the caller exactly as a
            // command that had worked. `MenuIndexTests` makes it a build failure for a catalog
            // action to have no handler, so this is a bug and not a state — but the user must not
            // be the one who has to notice, and telling them "Done" would be the app claiming work
            // that no code exists to do.
            string missing =
                "TrestleBoard does not know how to do that, so nothing has happened and nothing in "
                + $"your newsletter has changed. This is a fault in the program, not in what you "
                + $"did. (No handler for “{actionId}”.)";
            _window.Announce(missing);
            _window.RefreshActions();
            return ActionOutcome.Refused(missing);
        }

        string? trouble = null;
        bool didSomething = false;
        try
        {
            didSomething = await handler(source);
        }
        catch (Exception ex)
        {
            // Every surface in the app starts a command with `_ = RunAsync(...)`, so before
            // this an exception from any handler went into a discarded Task and was never seen
            // by anyone: the button did nothing, the app said nothing, and whatever the handler
            // had half-finished stayed half-finished (review §14.2).
            //
            // Deliberately catching everything. This is the boundary between "a command" and
            // "the app", and on this side of it there is no caller left to handle anything —
            // the alternative is not a better error, it is the process going down and taking
            // the newsletter with it. What the user gets instead is a sentence and a window
            // that still holds their work.
            trouble =
                $"Something went wrong while doing that, so TrestleBoard stopped part way. "
                + $"Your newsletter is still here. ({ex.Message})";
            _window.Announce(trouble);
        }

        _window.RefreshActions();
        return trouble is not null
            ? ActionOutcome.Refused(trouble)
            : didSomething ? ActionOutcome.Did : ActionOutcome.Nothing;
    }

    /// <summary>
    /// A handler that always changes something — the default, and what every handler was before
    /// M73(f). Widen the shell method to return a <c>bool</c> and this becomes the overload below.
    /// </summary>
    private static Func<Control?, Task<bool>> Sync(Action action) => _ =>
    {
        action();
        return Task.FromResult(true);
    };

    /// <summary>A handler that says whether it changed anything.</summary>
    private static Func<Control?, Task<bool>> Sync(Func<bool> action) => _ => Task.FromResult(action());

    /// <summary>An awaited handler that always changes something.</summary>
    private static Func<Control?, Task<bool>> Async(Func<Task> action) => async _ =>
    {
        await action();
        return true;
    };

    /// <summary>An awaited handler that says whether it changed anything — a cancelled picker or
    /// wizard answers false, which is the whole of M73(f).</summary>
    private static Func<Control?, Task<bool>> Async(Func<Task<bool>> action) => async _ => await action();
}
