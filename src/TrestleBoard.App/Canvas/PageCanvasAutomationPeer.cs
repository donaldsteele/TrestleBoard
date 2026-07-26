using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using TrestleBoard.Rendering;

namespace TrestleBoard.App.Canvas;

/// <summary>
/// Makes the canvas readable (PLAN.md §6, docs/M9-spec.md §5). Without this a screen reader sees one
/// control with a document hidden inside it; with it, each block on the page is a child that
/// announces itself in plain language — "Photo: brothers at the picnic", "Lodge officers".
/// </summary>
public sealed class PageCanvasAutomationPeer : ControlAutomationPeer
{
    private readonly PageCanvasControl _canvas;

    public PageCanvasAutomationPeer(PageCanvasControl canvas)
        : base(canvas)
    {
        _canvas = canvas;
    }

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

    protected override string GetClassNameCore() => nameof(PageCanvasControl);

    protected override string? GetNameCore() =>
        _canvas.Source is { } source && source.PageCount > 0
            ? $"Newsletter page {_canvas.PageIndex + 1} of {source.PageCount}"
            : "Newsletter page";

    /// <summary>
    /// Tells the automation system the page has changed, so it re-asks for the children rather than
    /// serving the ones it cached the first time it looked.
    /// </summary>
    public void InvalidateBlocks() => InvalidateChildren();

    protected override IReadOnlyList<AutomationPeer> GetOrCreateChildrenCore()
    {
        var children = new List<AutomationPeer>();
        if (_canvas.Source is not { } source || source.PageCount == 0)
        {
            return children;
        }

        foreach ((string blockId, string name) in source.DescribeBlocks(_canvas.PageIndex))
        {
            children.Add(new BlockAutomationPeer(this, _canvas, blockId, name));
        }

        return children;
    }

    /// <summary>
    /// One block on the page. Selecting it through the peer moves the real selection, so a screen
    /// reader user drives the same editor everyone else does rather than a parallel one.
    /// </summary>
    private sealed class BlockAutomationPeer : AutomationPeer, IInvokeProvider
    {
        private readonly PageCanvasAutomationPeer _parent;
        private readonly PageCanvasControl _canvas;
        private readonly string _blockId;
        private readonly string _name;

        public BlockAutomationPeer(
            PageCanvasAutomationPeer parent, PageCanvasControl canvas, string blockId, string name)
        {
            _parent = parent;
            _canvas = canvas;
            _blockId = blockId;
            _name = name;
        }

        public void Invoke() => _canvas.FrameEditor?.Select(_blockId);

        protected override string GetNameCore() => _name;

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;

        protected override string GetClassNameCore() => "Block";

        protected override bool IsContentElementCore() => true;

        protected override bool IsControlElementCore() => true;

        protected override bool IsEnabledCore() => true;

        protected override bool IsKeyboardFocusableCore() => true;

        protected override bool HasKeyboardFocusCore() =>
            _canvas.FrameEditor?.SelectedBlockId == _blockId;

        protected override void SetFocusCore() => _canvas.FrameEditor?.Select(_blockId);

        protected override AutomationPeer? GetParentCore() => _parent;

        protected override IReadOnlyList<AutomationPeer> GetOrCreateChildrenCore() => [];

        protected override Avalonia.Rect GetBoundingRectangleCore() => default;

        protected override bool TrySetParent(AutomationPeer? parent) => false;

        protected override bool IsOffscreenCore() => false;

        protected override string? GetAcceleratorKeyCore() => null;

        protected override string? GetAccessKeyCore() => null;

        protected override string? GetAutomationIdCore() => _blockId;

        protected override void BringIntoViewCore()
        {
        }

        protected override AutomationPeer? GetLabeledByCore() => null;

        protected override bool ShowContextMenuCore() => false;

    }
}
