using System.Globalization;
using System.Text;

namespace TrestleBoard.Layout.Widgets;

/// <summary>
/// Canonical text rendering of a draw list (docs/M7-spec.md §4.5). Committed as golden files, these
/// dumps are the cross-OS determinism gate: unlike raster baselines they are SINGLE files, so a
/// difference between OSes is a widget bug, not a baseline to promote.
/// </summary>
public static class WidgetDrawListDump
{
    public static string Write(WidgetDrawList drawList)
    {
        ArgumentNullException.ThrowIfNull(drawList);
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"size {F(drawList.WidthPt)} x {F(drawList.HeightPt)}");
        text.Append('\n');
        if (drawList.IsEmpty)
        {
            text.Append(CultureInfo.InvariantCulture, $"empty \"{drawList.EmptyPromptText}\"");
            text.Append('\n');
        }

        foreach (WidgetDrawItem item in drawList.Items)
        {
            switch (item)
            {
                case WidgetTextItem textItem:
                    foreach (PositionedGlyphRun run in textItem.Runs)
                    {
                        text.Append(CultureInfo.InvariantCulture,
                            $"text origin={F(run.OriginX)},{F(run.BaselineY)} size={F(run.SizePt)} " +
                            $"color={run.ColorArgb:X8} font={run.Font.Key.Family}/{run.Font.Key.Weight}/{run.Font.Key.Slant} " +
                            $"advance={F(run.AdvanceWidthPt)} glyphs=");
                        for (int i = 0; i < run.Glyphs.Count; i++)
                        {
                            if (i > 0)
                            {
                                text.Append(',');
                            }

                            text.Append(CultureInfo.InvariantCulture, $"{run.Glyphs[i]}@{F(run.GlyphPenXPt[i])}");
                        }

                        text.Append('\n');
                    }

                    break;

                case WidgetRuleItem rule:
                    text.Append(CultureInfo.InvariantCulture,
                        $"rule {rule.Orientation} at={F(rule.PositionPt)} span={F(rule.StartPt)}..{F(rule.EndPt)} " +
                        $"width={F(rule.WidthPt)} color={rule.ColorArgb:X8}");
                    text.Append('\n');
                    break;

                case WidgetFillItem fill:
                    text.Append(CultureInfo.InvariantCulture,
                        $"fill {F(fill.LeftPt)},{F(fill.TopPt)} {F(fill.RightPt)},{F(fill.BottomPt)} " +
                        $"color={fill.ColorArgb:X8}");
                    text.Append('\n');
                    break;

                default:
                    throw new NotSupportedException($"Unknown draw item: {item.GetType().Name}");
            }
        }

        return text.ToString();
    }

    private static string F(float value) => value.ToString("F4", CultureInfo.InvariantCulture);
}
