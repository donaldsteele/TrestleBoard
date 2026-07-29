using TrestleBoard.Core.Model;

namespace TrestleBoard.Core.Templates;

/// <summary>
/// The dimensions the standard style sheet is built at. The three call sites had drifted to
/// three slightly different sets of numbers; those differences are preserved here rather than
/// flattened, because flattening them would move pixels in the snapshot baselines.
/// </summary>
public sealed record StandardStyleMetrics
{
    public float BodySizePt { get; init; } = 11f;

    public float TableSizePt { get; init; } = 10f;

    public float TableHeaderSizePt { get; init; } = 12f;

    public float BodyLineSpacing { get; init; } = 1.3f;

    public float BodySpaceAfterPt { get; init; } = 7f;

    public float BodyFirstLineIndentPt { get; init; } = 14f;
}

/// <summary>
/// The one standard style sheet, shared by the shipped templates and both sample documents
/// (PLAN.md M14).
/// <para>
/// The paragraph-style combo in the toolbar was never broken — the documents were empty of
/// styles. <c>AddStandardStyles</c> defined four character styles and exactly ONE paragraph
/// style, so for a template-created document the combo had a single entry. This adds the roles a
/// newsletter actually has, and the bold/italic siblings of Body by name so that
/// <see cref="Text.CharacterStyleResolver"/> resolves them by name lookup rather than falling
/// through to its attribute scan.
/// </para>
/// <para>
/// Adding styles nothing uses changes no pixels — no snapshot baseline moves for this.
/// </para>
/// </summary>
public static class StandardStyles
{
    public const string BodyFamily = "Source Serif 4";
    public const string SansFamily = "Source Sans 3";

    public static void Add(Document document, StandardStyleMetrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        StandardStyleMetrics m = metrics ?? new StandardStyleMetrics();

        document.StyleSheet.CharacterStyles.AddRange(
        [
            new CharacterStyleDef { Name = "body", FontFamily = BodyFamily, SizePt = m.BodySizePt },
            new CharacterStyleDef { Name = "body-bold", FontFamily = BodyFamily, Weight = FontWeightToken.Bold, SizePt = m.BodySizePt },
            new CharacterStyleDef { Name = "body-italic", FontFamily = BodyFamily, Slant = FontSlantToken.Italic, SizePt = m.BodySizePt },
            new CharacterStyleDef { Name = "body-bold-italic", FontFamily = BodyFamily, Weight = FontWeightToken.Bold, Slant = FontSlantToken.Italic, SizePt = m.BodySizePt },
            new CharacterStyleDef { Name = "heading", FontFamily = SansFamily, Weight = FontWeightToken.Bold, SizePt = m.BodySizePt + 5f },
            new CharacterStyleDef { Name = "subheading", FontFamily = SansFamily, Weight = FontWeightToken.Bold, SizePt = m.BodySizePt + 2f },
            new CharacterStyleDef { Name = "caption", FontFamily = SansFamily, SizePt = m.BodySizePt - 2f },
            new CharacterStyleDef { Name = "quote", FontFamily = BodyFamily, Slant = FontSlantToken.Italic, SizePt = m.BodySizePt },
            new CharacterStyleDef { Name = "table", FontFamily = SansFamily, SizePt = m.TableSizePt },
            new CharacterStyleDef { Name = "table-header", FontFamily = SansFamily, Weight = FontWeightToken.Bold, SizePt = m.TableHeaderSizePt },
        ]);
        document.StyleSheet.ParagraphStyles.AddRange(
        [
            new ParagraphStyleDef
            {
                Name = "body",
                CharacterStyleRef = "body",
                LineSpacing = m.BodyLineSpacing,
                SpaceAfterPt = m.BodySpaceAfterPt,
                FirstLineIndentPt = m.BodyFirstLineIndentPt,
            },
            new ParagraphStyleDef { Name = "heading", CharacterStyleRef = "heading", LineSpacing = 1.15f, SpaceBeforePt = 10f, SpaceAfterPt = 4f },
            new ParagraphStyleDef { Name = "subheading", CharacterStyleRef = "subheading", LineSpacing = 1.15f, SpaceBeforePt = 8f, SpaceAfterPt = 3f },
            new ParagraphStyleDef { Name = "caption", CharacterStyleRef = "caption", LineSpacing = 1.2f, SpaceAfterPt = 4f },
            new ParagraphStyleDef { Name = "quote", CharacterStyleRef = "quote", LineSpacing = 1.25f, SpaceBeforePt = 6f, SpaceAfterPt = 6f, FirstLineIndentPt = 18f },
        ]);
        document.StyleSheet.TableStyles.Add(new TableStyleDef
        {
            Name = "lodge-table",
            HeaderCharacterStyleRef = "table-header",
            BodyCharacterStyleRef = "table",
            RuleArgb = 0xFF8A8A8A,
            RuleWidthPt = 0.5f,
        });
    }
}
