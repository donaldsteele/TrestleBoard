using TrestleBoard.Core.Model;
using TrestleBoard.Layout.Fonts;
using TrestleBoard.Layout.Input;
using TrestleBoard.Layout.Widgets;

namespace TrestleBoard.Rendering;

/// <summary>
/// Layers the document's named styles over a widget's own defaults (docs/M7-spec.md §9). Document
/// styles win; anything the document does not say keeps the widget's fallback. No widget ever
/// writes into <c>Document.StyleSheet</c>, which is what keeps six widgets independent.
/// </summary>
public static class WidgetStyleResolver
{
    public static WidgetStyleContext Resolve(Document document, WidgetBlock block, WidgetStyleContext defaults)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(defaults);

        StyleSheet styles = document.StyleSheet;
        TableStyleDef? table = block.TableStyleRef is { } tableRef
            ? styles.TableStyles.Find(s => s.Name == tableRef)
            : null;
        FrameStyleDef? frame = block.FrameStyleRef is { } frameRef
            ? styles.FrameStyles.Find(s => s.Name == frameRef)
            : null;

        CharacterStyle heading = Character(styles, table?.HeaderCharacterStyleRef) ?? defaults.Heading;
        CharacterStyle body = Character(styles, table?.BodyCharacterStyleRef) ?? defaults.Body;

        return defaults with
        {
            Heading = heading,
            Body = body,
            // Emphasis tracks the resolved body face so a restyled table stays internally consistent.
            Emphasis = table?.BodyCharacterStyleRef is null
                ? defaults.Emphasis
                : body with { Weight = FontWeight.Bold },
            RuleArgb = table?.RuleArgb ?? defaults.RuleArgb,
            RuleWidthPt = table?.RuleWidthPt ?? defaults.RuleWidthPt,
            FillArgb = frame?.FillArgb ?? defaults.FillArgb,
            StrokeArgb = frame?.StrokeArgb ?? defaults.StrokeArgb,
            StrokeWidthPt = frame is null ? defaults.StrokeWidthPt : frame.StrokeWidthPt,
            PaddingPt = frame is null ? defaults.PaddingPt : frame.PaddingPt,
            ColorTokens = document.Theme.ColorTokens,
        };
    }

    private static CharacterStyle? Character(StyleSheet styles, string? name)
    {
        if (name is null)
        {
            return null;
        }

        CharacterStyleDef? def = styles.CharacterStyles.Find(s => s.Name == name);
        return def is null
            ? null
            : new CharacterStyle(
                def.FontFamily,
                def.Weight == FontWeightToken.Bold ? FontWeight.Bold : FontWeight.Regular,
                def.Slant == FontSlantToken.Italic ? FontStyleSlant.Italic : FontStyleSlant.Normal,
                def.SizePt,
                def.ColorArgb);
    }
}
