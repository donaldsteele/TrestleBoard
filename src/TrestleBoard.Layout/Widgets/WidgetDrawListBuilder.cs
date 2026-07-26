using TrestleBoard.Layout.Input;

namespace TrestleBoard.Layout.Widgets;

/// <summary>
/// Accumulates draw items and tracks the content height, so six widget layouters do not each
/// hand-roll list construction (docs/M7-spec.md §4.3). Widget-local coordinates throughout.
/// </summary>
public sealed class WidgetDrawListBuilder
{
    private readonly List<WidgetDrawItem> _items = [];

    public WidgetDrawListBuilder(float widthPt)
    {
        WidthPt = widthPt;
    }

    public float WidthPt { get; }

    /// <summary>Running content height; every Add* call pushes it forward as needed.</summary>
    public float HeightPt { get; private set; }

    public int Count => _items.Count;

    public void Text(WidgetTextItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Runs.Count == 0)
        {
            return;
        }

        _items.Add(item);
    }

    /// <summary>Adds a run and advances the height to the run's descent line.</summary>
    public void TextLine(WidgetTextItem item, float bandBottomPt)
    {
        Text(item);
        Grow(bandBottomPt);
    }

    public void HRule(float yPt, float leftPt, float rightPt, float widthPt, uint colorArgb)
    {
        _items.Add(new WidgetRuleItem(WidgetRuleOrientation.Horizontal, yPt, leftPt, rightPt, widthPt, colorArgb));
        Grow(yPt + widthPt);
    }

    public void VRule(float xPt, float topPt, float bottomPt, float widthPt, uint colorArgb)
    {
        _items.Add(new WidgetRuleItem(WidgetRuleOrientation.Vertical, xPt, topPt, bottomPt, widthPt, colorArgb));
        Grow(bottomPt);
    }

    public void Fill(float leftPt, float topPt, float rightPt, float bottomPt, uint colorArgb)
    {
        _items.Add(new WidgetFillItem(leftPt, topPt, rightPt, bottomPt, colorArgb));
        Grow(bottomPt);
    }

    /// <summary>Four rules instead of a stroked rect: one fewer primitive to rasterise identically.</summary>
    public void Border(float leftPt, float topPt, float rightPt, float bottomPt, float widthPt, uint colorArgb)
    {
        HRule(topPt, leftPt, rightPt, widthPt, colorArgb);
        HRule(bottomPt - widthPt, leftPt, rightPt, widthPt, colorArgb);
        VRule(leftPt, topPt, bottomPt, widthPt, colorArgb);
        VRule(rightPt - widthPt, topPt, bottomPt, widthPt, colorArgb);
    }

    /// <summary>
    /// Puts a fill behind everything already added. Backgrounds are sized from the MEASURED content
    /// height, which is not known until the content is laid out — and the renderer paints in list
    /// order — so a card lays its content out first and slides its background underneath.
    /// </summary>
    public void PrependFill(float leftPt, float topPt, float rightPt, float bottomPt, uint colorArgb)
    {
        _items.Insert(0, new WidgetFillItem(leftPt, topPt, rightPt, bottomPt, colorArgb));
        Grow(bottomPt);
    }

    /// <summary>Four rules behind everything already added; see <see cref="PrependFill"/>.</summary>
    public void PrependBorder(
        float leftPt, float topPt, float rightPt, float bottomPt, float widthPt, uint colorArgb)
    {
        _items.InsertRange(0,
        [
            new WidgetRuleItem(WidgetRuleOrientation.Horizontal, topPt, leftPt, rightPt, widthPt, colorArgb),
            new WidgetRuleItem(WidgetRuleOrientation.Horizontal, bottomPt - widthPt, leftPt, rightPt, widthPt, colorArgb),
            new WidgetRuleItem(WidgetRuleOrientation.Vertical, leftPt, topPt, bottomPt, widthPt, colorArgb),
            new WidgetRuleItem(WidgetRuleOrientation.Vertical, rightPt - widthPt, topPt, bottomPt, widthPt, colorArgb),
        ]);
        Grow(bottomPt);
    }

    /// <summary>Pushes the content height forward without adding anything (gaps, padding).</summary>
    public void Grow(float toHeightPt)
    {
        if (toHeightPt > HeightPt)
        {
            HeightPt = toHeightPt;
        }
    }

    public WidgetDrawList Build(bool isEmpty = false, string emptyPromptText = "") =>
        new(_items.ToArray(), WidthPt, HeightPt, isEmpty, emptyPromptText);

    /// <summary>Convenience for the common "one full-width heading, then a rule" opening.</summary>
    public float Heading(WidgetTextShaper shaper, string text, CharacterStyle style, float lineSpacing, float topPt)
    {
        ArgumentNullException.ThrowIfNull(shaper);
        if (string.IsNullOrEmpty(text))
        {
            return topPt;
        }

        WidgetLineMetrics metrics = shaper.GetLineMetrics(style, lineSpacing);
        TextLine(
            shaper.ShapeRun(text, style, 0f, topPt + metrics.AscentPt),
            topPt + metrics.LineHeightPt);
        return topPt + metrics.LineHeightPt;
    }
}
