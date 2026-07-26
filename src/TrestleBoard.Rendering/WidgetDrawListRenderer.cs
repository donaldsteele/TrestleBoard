using SkiaSharp;
using TrestleBoard.Core.Model;
using TrestleBoard.Layout;
using TrestleBoard.Layout.Input;
using TrestleBoard.Layout.Widgets;

namespace TrestleBoard.Rendering;

/// <summary>
/// Replays a widget draw list onto any canvas (docs/M7-spec.md §4.3/§4.6). Text goes through
/// <see cref="PageRenderer.DrawRun"/> — the same path body text takes — and everything is clipped to
/// the frame, because layout is never allowed to grow the frame behind the user's back.
/// </summary>
public static class WidgetDrawListRenderer
{
    /// <summary>Grey face for the "not filled in yet" prompt; screen only, never printed.</summary>
    public const uint PromptArgb = 0xFF8A8A8A;

    public static void Render(
        SKCanvas canvas,
        WidgetDrawList drawList,
        RectPt frameRect,
        WidgetTextShaper? shaper = null,
        bool showEmptyPrompt = true)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(drawList);

        var rect = new SKRect(frameRect.X, frameRect.Y, frameRect.Right, frameRect.Bottom);
        int save = canvas.Save();
        canvas.ClipRect(rect);
        canvas.Translate(frameRect.X, frameRect.Y);

        foreach (WidgetDrawItem item in drawList.Items)
        {
            switch (item)
            {
                case WidgetTextItem text:
                    foreach (PositionedGlyphRun run in text.Runs)
                    {
                        PageRenderer.DrawRun(canvas, run);
                    }

                    break;

                case WidgetFillItem fill:
                    using (var paint = new SKPaint { Color = new SKColor(fill.ColorArgb), IsAntialias = false })
                    {
                        canvas.DrawRect(
                            new SKRect(fill.LeftPt, fill.TopPt, fill.RightPt, fill.BottomPt), paint);
                    }

                    break;

                case WidgetRuleItem rule:
                    // Antialiased: a 0.5pt hairline that lands between device pixels otherwise
                    // disappears on some rows and not others, and a table of half-drawn rules looks
                    // broken. Fills and rules rasterise identically on every OS — only glyph
                    // scalers differ (docs/M1-spec.md §5) — so this costs no determinism.
                    using (var paint = new SKPaint { Color = new SKColor(rule.ColorArgb), IsAntialias = true })
                    {
                        SKRect band = rule.Orientation == WidgetRuleOrientation.Horizontal
                            ? new SKRect(rule.StartPt, rule.PositionPt, rule.EndPt, rule.PositionPt + rule.WidthPt)
                            : new SKRect(rule.PositionPt, rule.StartPt, rule.PositionPt + rule.WidthPt, rule.EndPt);
                        canvas.DrawRect(band, paint);
                    }

                    break;

                default:
                    throw new NotSupportedException($"Unknown widget draw item: {item.GetType().Name}");
            }
        }

        if (drawList.IsEmpty && showEmptyPrompt && shaper is not null && drawList.EmptyPromptText.Length > 0)
        {
            DrawPrompt(canvas, shaper, drawList.EmptyPromptText, frameRect);
        }

        canvas.RestoreToCount(save);
    }

    /// <summary>
    /// The prompt is a RENDER-time decision, not part of the draw list: the widget cache is
    /// unaffected by it, and the PDF exporter simply turns it off (docs/M7-spec.md §8.4).
    /// </summary>
    private static void DrawPrompt(SKCanvas canvas, WidgetTextShaper shaper, string text, RectPt frameRect)
    {
        // Serif italic: no italic Source Sans face is bundled, and there is no system fallback.
        var style = new CharacterStyle(
            Layout.Fonts.BundledFonts.BodyFamily,
            Layout.Fonts.FontWeight.Regular,
            Layout.Fonts.FontStyleSlant.Italic,
            10f,
            PromptArgb);
        WidgetLineMetrics metrics = shaper.GetLineMetrics(style);
        float baseline = ((frameRect.Height - metrics.LineHeightPt) / 2f) + metrics.AscentPt;
        WidgetTextItem item = shaper.ShapeAligned(text, style, 0f, frameRect.Width, baseline, TextAlign.Center);
        foreach (PositionedGlyphRun run in item.Runs)
        {
            PageRenderer.DrawRun(canvas, run);
        }
    }
}
