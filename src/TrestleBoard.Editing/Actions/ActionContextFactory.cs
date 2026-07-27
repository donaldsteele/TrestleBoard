using TrestleBoard.Core.Commands;
using TrestleBoard.Core.Model;
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
/// <param name="BirthdayListIsStale">M13: a generated birthday list was made for another month.</param>
/// <param name="CoverDateMissing">The cover heading has no meeting date filled in.</param>
public readonly record struct ShellFacts(
    bool ExportedPdfThisSession = false,
    bool SelectedWidgetHasListEditor = false,
    string? SelectedWidgetDisplayName = null,
    bool RosterEmptyButNeeded = false,
    bool BirthdayListIsStale = false,
    bool CoverDateMissing = false);

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
            selection = widgets?.IsWidget(blockId) == true ? SelectionKind.Widget
                : photos?.IsPhoto(blockId) == true ? SelectionKind.Photo
                : source.IsTextBlock(blockId) ? SelectionKind.TextFrame
                : SelectionKind.Shape;
        }

        Block? block = blockId is not null && session is not null ? TryFindBlock(session, blockId) : null;

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

            Selection = selection,
            SelectedBlockId = blockId,
            IsEditingText = editing,
            HasTextSelection = editing && !editor!.Selection.IsEmpty,
            SelectionIsTextFrame = isTextFrame,
            SelectionWraps = block?.WrapMode == WrapMode.Rectangle,
            SelectionIsLinked = textBlock is TextBlock { LinkNext: not null },
            SelectionIsOverset = frames?.IsSelectionOverset == true,
            CanAutoFlow = pages?.CanAutoFlow(textBlockId) == true,

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
        };
    }

    /// <summary>
    /// Non-throwing on purpose: a selection can outlive its block for one change notification, and a
    /// snapshot taken during that window must describe the world rather than fall over in it.
    /// </summary>
    private static Block? TryFindBlock(DocumentSession session, string blockId) =>
        session.Document.TryFindBlock(blockId, out _, out Block? block) ? block : null;

    /// <summary>
    /// Does an article still say "write this month's article here"? That prompt is what
    /// start-from-last-month leaves behind, and until M11 nothing ever mentioned it again.
    /// </summary>
    private static bool HoldsAPrompt(Document document) =>
        document.Stories.Any(story => story.Paragraphs.Any(p => p.Runs.Any(run =>
            run.Text.Contains(CarryForward.DefaultArticlePrompt, StringComparison.Ordinal))));
}
