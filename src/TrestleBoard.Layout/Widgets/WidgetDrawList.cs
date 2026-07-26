namespace TrestleBoard.Layout.Widgets;

public enum WidgetRuleOrientation
{
    Horizontal,
    Vertical,
}

/// <summary>
/// One device-independent drawing instruction in widget-local coordinates: origin (0,0) at the
/// frame's top-left, y-down, typographic points (docs/M7-spec.md §4.3). Three primitives cover all
/// six v1 widgets; anything else is deliberately excluded so cross-OS rasterisation stays identical.
/// </summary>
public abstract record WidgetDrawItem;

/// <summary>
/// Positioned glyphs. Produced ONLY by <see cref="WidgetTextShaper"/> — zero re-shaping at paint
/// time. <paramref name="Text"/> is the string those glyphs came from: the draw list is otherwise
/// write-only, and both the golden dumps and M9's screen-reader peers need to read a widget back.
/// </summary>
public sealed record WidgetTextItem(IReadOnlyList<PositionedGlyphRun> Runs, string Text = "") : WidgetDrawItem;

/// <summary>
/// Axis-aligned hairline. <paramref name="PositionPt"/> is y for Horizontal and x for Vertical;
/// Start/End span the other axis.
/// </summary>
public sealed record WidgetRuleItem(
    WidgetRuleOrientation Orientation,
    float PositionPt,
    float StartPt,
    float EndPt,
    float WidthPt,
    uint ColorArgb) : WidgetDrawItem;

public sealed record WidgetFillItem(
    float LeftPt,
    float TopPt,
    float RightPt,
    float BottomPt,
    uint ColorArgb) : WidgetDrawItem;

/// <summary>
/// A laid-out widget: what to draw, and how tall it wants to be at the requested width. Position
/// independent, so dragging a widget re-uses the cached list instead of re-laying-out per frame.
/// </summary>
public sealed class WidgetDrawList
{
    public WidgetDrawList(
        IReadOnlyList<WidgetDrawItem> items,
        float widthPt,
        float heightPt,
        bool isEmpty = false,
        string emptyPromptText = "")
    {
        Items = items ?? throw new ArgumentNullException(nameof(items));
        WidthPt = widthPt;
        HeightPt = heightPt;
        IsEmpty = isEmpty;
        EmptyPromptText = emptyPromptText ?? "";
    }

    public IReadOnlyList<WidgetDrawItem> Items { get; }

    public float WidthPt { get; }

    /// <summary>Natural content height at <see cref="WidthPt"/>. Drives fit-to-contents (§4.6).</summary>
    public float HeightPt { get; }

    /// <summary>True when the widget holds no user content yet; the renderer draws the prompt (§8.4).</summary>
    public bool IsEmpty { get; }

    /// <summary>Prompt shown on screen (never in a PDF) when <see cref="IsEmpty"/>.</summary>
    public string EmptyPromptText { get; }
}
