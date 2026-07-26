using System.Text.Json;
using TrestleBoard.Core.Commands;
using TrestleBoard.Core.Model;
using TrestleBoard.Layout.Widgets;
using TrestleBoard.Rendering;

namespace TrestleBoard.Editing;

/// <summary>
/// Inserting and re-editing widgets (docs/M7-spec.md §7/§8). Headless like every other controller,
/// and deliberately ignorant of <c>TrestleBoard.Widgets</c>: it sees widgets only through
/// <see cref="IWidgetCatalog"/>, which is what keeps that project a leaf.
/// </summary>
public sealed class WidgetController
{
    /// <summary>Text wraps around every widget by default — that is PLAN.md §5's promise.</summary>
    public const float DefaultWrapMarginPt = FrameEditorController.DefaultWrapMarginPt;

    private readonly DocumentSession _session;
    private readonly DocumentRenderSource _layout;
    private readonly IWidgetCatalog _catalog;

    public WidgetController(DocumentSession session, DocumentRenderSource layout, IWidgetCatalog catalog)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public event EventHandler? Changed;

    /// <summary>Plain-language feedback for the shell's status line (PLAN.md §6).</summary>
    public string? StatusMessage { get; private set; }

    public bool IsWidget(string? blockId) => Find(blockId) is not null;

    public string? GetWidgetType(string? blockId) => Find(blockId)?.WidgetType;

    /// <summary>
    /// False for a widget this build does not understand — an unknown type, or a payload written by
    /// a later version. Such a block still moves, resizes and deletes, and is saved back verbatim
    /// (docs/M7-spec.md §2.1).
    /// </summary>
    public bool CanEdit(string? blockId)
    {
        if (Find(blockId) is not { } widget)
        {
            return false;
        }

        return _catalog.TryGetInfo(widget.WidgetType, out WidgetInfo info)
            && widget.DataVersion <= info.CurrentDataVersion;
    }

    /// <summary>The refusal sentence for a widget from a newer version (§2.1). Plain language, no jargon.</summary>
    public const string NewerVersionMessage =
        "This part of the newsletter was made by a newer version of TrestleBoard. You can move it, "
        + "resize it or delete it, and it will be saved exactly as it is — but this version cannot "
        + "change what is inside it.";

    /// <summary>
    /// Adds an EMPTY widget inside the page margins, cascading like a new text frame, and fits the
    /// frame to its content in the same undo step.
    /// </summary>
    public string InsertWidget(int pageIndex, string typeId)
    {
        ArgumentNullException.ThrowIfNull(typeId);
        Document document = _session.Document;
        Page page = document.Pages[pageIndex];
        PageMaster master = document.GetMaster(page.MasterRef);
        if (!_catalog.TryGetInfo(typeId, out WidgetInfo info))
        {
            throw new KeyNotFoundException($"Unknown widget type: {typeId}");
        }

        string blockId = NextId("widget", id => document.Pages.Any(p => p.Blocks.Any(b => b.Id == id)));
        float cascade = Math.Min(page.Blocks.Count * 18f, 90f);
        float maxX = Math.Max(master.MarginLeftPt, master.Size.Width - master.MarginRightPt - info.DefaultSizePt.Width);
        float maxY = Math.Max(master.MarginTopPt, master.Size.Height - master.MarginBottomPt - info.DefaultSizePt.Height);
        var rect = new RectPt(
            Math.Min(master.MarginLeftPt + cascade, maxX),
            Math.Min(master.MarginTopPt + cascade, maxY),
            info.DefaultSizePt.Width,
            info.DefaultSizePt.Height);

        var block = new WidgetBlock
        {
            Id = blockId,
            WidgetType = typeId,
            DataVersion = info.CurrentDataVersion,
            Data = _catalog.CreateEmptyData(typeId, SeedFrom(document)),
            FrameRect = rect,
            ZOrder = page.Blocks.Count == 0 ? 0 : page.Blocks.Max(b => b.ZOrder) + 1,
            WrapMode = WrapMode.Rectangle,
            WrapMarginPt = DefaultWrapMarginPt,
        };

        _session.Execute(new CompositeCommand(
            $"Add {info.DisplayName.ToLowerInvariant()}",
            new ChangeScope(ChangeKind.PageStructure, PageId: page.Id, BlockId: blockId),
            [new AddBlockCommand(page.Id, block)]));

        StatusMessage = null;
        Raise();
        return blockId;
    }

    /// <summary>
    /// The ONE commit path, shared by the wizard and the grid editor (docs/M7-spec.md §7.3). The
    /// composite exists so the Edit menu can name what the user did, and so a height change lands in
    /// the same undo step as the data change.
    /// </summary>
    public bool ApplyWidgetData(string blockId, JsonElement data, int dataVersion, string undoLabel)
    {
        ArgumentNullException.ThrowIfNull(blockId);
        ArgumentException.ThrowIfNullOrWhiteSpace(undoLabel);
        if (!CanEdit(blockId))
        {
            StatusMessage = NewerVersionMessage;
            Raise();
            return false;
        }

        WidgetBlock widget = Find(blockId)!;
        var children = new List<IDocumentCommand>
        {
            new SetWidgetDataCommand(blockId, data, dataVersion),
        };

        // Measured against the payload BEFORE it lands, so the resize rides in the same command
        // rather than costing the user a second Ctrl+Z (docs/M7-spec.md §7.3).
        if (_layout.TryMeasureWidgetHeight(blockId, data, dataVersion, widget.FrameRect.Width, out float height)
            && Math.Abs(height - widget.FrameRect.Height) > 0.5f)
        {
            children.Add(new ResizeBlockCommand(blockId, widget.FrameRect with { Height = height }));
        }

        _session.Execute(new CompositeCommand(
            undoLabel,
            new ChangeScope(ChangeKind.BlockContent, BlockId: blockId),
            children));

        StatusMessage = null;
        Raise();
        return true;
    }

    /// <summary>Grows (or shrinks) the frame to the widget's natural height — one undo step (§4.6).</summary>
    public bool FitToContents(string blockId)
    {
        ArgumentNullException.ThrowIfNull(blockId);
        if (Find(blockId) is not { } widget
            || !_layout.TryMeasureWidgetHeight(blockId, widget.FrameRect.Width, out float height)
            || Math.Abs(height - widget.FrameRect.Height) <= 0.5f)
        {
            return false;
        }

        _session.Execute(new ResizeBlockCommand(blockId, widget.FrameRect with { Height = height }));
        StatusMessage = null;
        Raise();
        return true;
    }

    /// <summary>True when the widget's content is taller than the box the user gave it.</summary>
    public bool IsOverflowing(string blockId) =>
        _layout.GetWidgetOverflowBlockIds().Contains(blockId, StringComparer.Ordinal);

    /// <summary>Non-personal document facts a new widget may copy (docs/M7-spec.md §8.3).</summary>
    public static WidgetSeed SeedFrom(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new WidgetSeed(
            document.Metadata.LodgeName,
            document.Metadata.IssueMonth,
            document.Metadata.IssueYear,
            document.Metadata.MeetingRule);
    }

    private WidgetBlock? Find(string? blockId)
    {
        if (blockId is null)
        {
            return null;
        }

        foreach (Page page in _session.Document.Pages)
        {
            foreach (Block block in page.Blocks)
            {
                if (block.Id == blockId)
                {
                    return block as WidgetBlock;
                }
            }
        }

        return null;
    }

    private static string NextId(string prefix, Func<string, bool> taken)
    {
        for (int i = 1; ; i++)
        {
            string candidate = $"{prefix}-{i}";
            if (!taken(candidate))
            {
                return candidate;
            }
        }
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
