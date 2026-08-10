using TrestleBoard.Core.Commands;
using TrestleBoard.Core.Model;
using TrestleBoard.Core.Templates;
using TrestleBoard.Core.Workflow;
using TrestleBoard.Rendering;

namespace TrestleBoard.Editing.Actions;

/// <summary>
/// Facts the shell knows and the editing controllers do not — whether a PDF has been exported this
/// session, and (from M12/M13) what the address book contains. Passed in rather than reached for,
/// so this project keeps no ambient state.
/// </summary>
/// <param name="ExportedPdfThisSession">A PDF has been written since this newsletter was opened.</param>
/// <param name="SelectedWidgetHasListEditor">
/// The selected widget's wizard has a list step, so the big-row grid editor is worth offering. Only
/// the widget registry can answer this and it lives above this project.
/// </param>
/// <param name="SelectedWidgetDisplayName">What the selected widget is called in the interface.</param>
/// <param name="RosterEmptyButNeeded">M12: a people widget is on the page and the address book is empty.</param>
/// <param name="BirthdayListIsStale">M13: a generated birthday list no longer matches the address book.</param>
/// <param name="RosterBirthdaysThisMonth">M13: how many people are born in this issue's month.</param>
/// <param name="OfficersTableIsStale">M19: a generated officers table no longer matches the book.</param>
/// <param name="RosterOfficesFilledIn">M19: how many of the twelve offices the book could fill in.</param>
/// <param name="SelectionFilledInFromRoster">
/// M19: the selected widget was filled in from the address book, and when. Null when it was typed.
/// </param>
/// <param name="CoverDateMissing">The cover heading has no meeting date filled in.</param>
/// <param name="RosterCount">M12: how many people the address book holds.</param>
/// <param name="RosterCanUndo">M12: there is one address-book change to take back.</param>
/// <param name="RosterUndoDescription">M12: what that change was, in the user's words.</param>
/// <param name="RosterHasEarlierVersions">M12: the backup ring holds something to restore.</param>
/// <param name="HasUnsavedChanges">M24: edited since it was last written to its file.</param>
/// <param name="DocumentFileName">M24: the file name this newsletter lives in, or null if it has none.</param>
/// <param name="DocumentHasEarlierVersions">M39: the .bak ring beside the user's file holds a copy.</param>
/// <param name="PageHasFrames">M49: the page being looked at has something on it to take hold of.</param>
/// <param name="CanReadPdfs">
/// M67: this computer's PDF reader loaded. A fact about the machine rather than the newsletter, and
/// the shell is the only layer that can find it out.
/// </param>
public readonly record struct ShellFacts(
    bool ExportedPdfThisSession = false,
    bool SelectedWidgetHasListEditor = false,
    string? SelectedWidgetDisplayName = null,
    bool RosterEmptyButNeeded = false,
    bool BirthdayListIsStale = false,
    int RosterBirthdaysThisMonth = 0,
    bool OfficersTableIsStale = false,
    int RosterOfficesFilledIn = 0,
    string? SelectionFilledInFromRoster = null,
    bool CoverDateMissing = false,
    int RosterCount = 0,
    bool RosterCanUndo = false,
    string? RosterUndoDescription = null,
    bool RosterHasEarlierVersions = false,
    bool HasUnsavedChanges = false,
    string? DocumentFileName = null,
    bool DocumentHasEarlierVersions = false,
    bool PageHasFrames = false,
    bool CanReadPdfs = false);

/// <summary>
/// Takes one snapshot of the editing state for the action catalog to reason about. Reading the
/// controllers happens here, once per change, instead of in thirty scattered places (PLAN.md §11 M11).
/// </summary>
public static class ActionContextFactory
{
    /// <summary>
    /// Builds the context. Every argument is optional because the shell genuinely has none of them
    /// before the first newsletter is opened, and "no newsletter" is a state the catalog must answer
    /// for rather than a state the caller has to avoid asking about.
    /// </summary>
    public static ActionContext Create(
        DocumentSession? session,
        DocumentRenderSource? source,
        TextEditorController? editor,
        FrameEditorController? frames,
        PhotoController? photos,
        WidgetController? widgets,
        PageFlowController? pages,
        int pageIndex,
        ShellFacts shell = default)
    {
        bool hasDocument = source is { PageCount: > 0 };
        bool editing = editor is { IsActive: true };
        string? blockId = frames?.SelectedBlockId;

        SelectionKind selection = SelectionKind.None;
        if (editing)
        {
            selection = SelectionKind.Text;
        }
        else if (blockId is not null && source is not null)
        {
            // The last arm is a FALL-THROUGH, not a classification: anything unrecognised is called
            // a shape. M72's drawing is asked about by name for that reason — left to the default it
            // would have looked like it worked while telling the user "A shape is selected".
            selection = widgets?.IsWidget(blockId) == true ? SelectionKind.Widget
                : photos?.IsPhoto(blockId) == true ? SelectionKind.Photo
                : photos?.IsVector(blockId) == true ? SelectionKind.Drawing
                : source.IsTextBlock(blockId) ? SelectionKind.TextFrame
                : SelectionKind.Shape;
        }

        Block? block = blockId is not null && session is not null ? TryFindBlock(session, blockId) : null;

        // "A picture is selected", in the sense the caption and swap commands mean it: a photograph
        // or a drawing (M74 (f)).
        bool isPicture = selection is SelectionKind.Photo or SelectionKind.Drawing;

        // "Is this about a frame of writing?" is true both when a text frame is selected as an
        // object and when the caret is inside one — the flow actions apply to both.
        string? textBlockId = editing ? editor!.BlockId : blockId;
        bool isTextFrame = textBlockId is not null && source?.IsTextBlock(textBlockId) == true;
        Block? textBlock = isTextFrame && session is not null ? TryFindBlock(session, textBlockId!) : null;

        return new ActionContext
        {
            HasDocument = hasDocument,
            PageCount = source?.PageCount ?? 0,
            PageIndex = pageIndex,
            CanStartFromLastMonth = hasDocument,
            HasOversetText = source is { IsOverset: true },
            HasUnsavedChanges = hasDocument && shell.HasUnsavedChanges,
            DocumentHasFile = shell.DocumentFileName is not null,
            DocumentFileName = shell.DocumentFileName,
            DocumentHasEarlierVersions = shell.DocumentHasEarlierVersions,
            PageHasFrames = shell.PageHasFrames,
            CanReadPdfs = shell.CanReadPdfs,

            Selection = selection,
            SelectedBlockId = blockId,
            SelectionCount = editing ? 0 : frames?.SelectionCount ?? 0,
            IsEditingText = editing,
            HasTextSelection = editing && !editor!.Selection.IsEmpty,
            SelectionIsTextFrame = isTextFrame,
            SelectionWraps = block?.WrapMode == WrapMode.Rectangle,
            SelectionIsLinked = textBlock is TextBlock { LinkNext: not null },
            SelectionIsOverset = frames?.IsSelectionOverset == true,
            CanAutoFlow = pages?.CanAutoFlow(textBlockId) == true,

            // M74 (f): a drawing is a picture too. These two were written against Photo alone, so
            // from M72 a captioned emblem was offered "Write a caption…" over the caption it
            // already had, and the greyed swap item read "Swap this picture…" of something that is
            // not swappable. The crop flags below stay on Photo on purpose — there is no crop on a
            // drawing to be stale.
            SelectedPictureIsEmpty = isPicture && photos?.IsPlaceholder(blockId) == true,
            SelectedPictureHasCaption =
                isPicture && !string.IsNullOrWhiteSpace(photos?.GetWorded(blockId)?.Caption),
            HasPicturePlaceholder = photos?.HasPlaceholder == true,
            PictureCropIsStale = selection == SelectionKind.Photo && photos?.CropIsStale(blockId) == true,
            CropStaleNote = selection == SelectionKind.Photo ? photos?.CropStaleNote(blockId) : null,

            SelectionUsesFontOverride = editing && editor!.SelectionUsesFontOverride,
            FontOverrideNote = editing ? editor!.DescribeFontOverride() : null,
            FontOverrideCount = editor?.CountFontOverrides() ?? 0,

            WidgetTypeId = selection == SelectionKind.Widget ? widgets?.GetWidgetType(blockId) : null,
            WidgetDisplayName = shell.SelectedWidgetDisplayName,
            CanEditWidget = selection == SelectionKind.Widget && widgets?.CanEdit(blockId) == true,
            WidgetHasListEditor = shell.SelectedWidgetHasListEditor,

            CanUndo = session?.CanUndo == true,
            CanRedo = session?.CanRedo == true,
            UndoDescription = session?.CanUndo == true ? session.UndoDescription : null,
            RedoDescription = session?.CanRedo == true ? session.RedoDescription : null,

            ExportedPdfThisSession = shell.ExportedPdfThisSession,
            HasUnwrittenArticle = session is not null && HoldsAPrompt(session.Document),
            CoverDateMissing = shell.CoverDateMissing,
            RosterEmptyButNeeded = shell.RosterEmptyButNeeded,
            BirthdayListIsStale = shell.BirthdayListIsStale,
            RosterBirthdaysThisMonth = shell.RosterBirthdaysThisMonth,
            OfficersTableIsStale = shell.OfficersTableIsStale,
            RosterOfficesFilledIn = shell.RosterOfficesFilledIn,
            SelectionFilledInFromRoster = shell.SelectionFilledInFromRoster,

            RosterCount = shell.RosterCount,
            RosterCanUndo = shell.RosterCanUndo,
            RosterUndoDescription = shell.RosterUndoDescription,
            RosterHasEarlierVersions = shell.RosterHasEarlierVersions,
        };
    }

    /// <summary>
    /// Non-throwing on purpose: a selection can outlive its block for one change notification, and a
    /// snapshot taken during that window must describe the world rather than fall over in it.
    /// </summary>
    private static Block? TryFindBlock(DocumentSession session, string blockId) =>
        session.Document.TryFindBlock(blockId, out _, out Block? block) ? block : null;

    /// <summary>
    /// Does anything still say "your words go here"? That prompt is what start-from-last-month
    /// leaves behind, and until M11 nothing ever mentioned it again.
    ///
    /// <para>M51: it used to look for carry-forward's article prompt and nothing else, so a
    /// newsletter started from a template — whose cover says "Write the Worshipful Master's message
    /// here…" and whose photo pages say two other things — was reported as having no prompts left.
    /// The card that exists to say "you have not written the article yet" stayed quiet for the one
    /// route where nothing had been written at all. All seven prompts now live in
    /// <see cref="PlaceholderPrompts"/> and all seven are matched.</para>
    /// </summary>
    private static bool HoldsAPrompt(Document document) =>
        document.Stories.Any(story => story.Paragraphs.Any(p => p.Runs.Any(run =>
            PlaceholderPrompts.LurksIn(run.Text))));
}
