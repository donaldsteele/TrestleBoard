using TrestleBoard.Core.Model;

namespace TrestleBoard.Core.Text;

/// <summary>
/// Finds (or describes) the (weight, slant) sibling of a character style (docs/M4-spec.md §6.1).
/// Naming convention: "body", "body-bold", "body-italic", "body-bold-italic". Bold/italic
/// toggling retargets runs at sibling styles — runs never carry direct formatting in v1.
/// </summary>
public static class CharacterStyleResolver
{
    private const string BoldSuffix = "-bold";
    private const string ItalicSuffix = "-italic";

    /// <summary>Strips the -bold/-italic suffixes to recover the family base name.</summary>
    public static string BaseName(string styleName)
    {
        ArgumentException.ThrowIfNullOrEmpty(styleName);
        string name = styleName;
        if (name.EndsWith(ItalicSuffix, StringComparison.Ordinal))
        {
            name = name[..^ItalicSuffix.Length];
        }

        if (name.EndsWith(BoldSuffix, StringComparison.Ordinal))
        {
            name = name[..^BoldSuffix.Length];
        }

        return name;
    }

    public static string VariantName(string baseName, FontWeightToken weight, FontSlantToken slant)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseName);
        string name = baseName;
        if (weight == FontWeightToken.Bold)
        {
            name += BoldSuffix;
        }

        if (slant == FontSlantToken.Italic)
        {
            name += ItalicSuffix;
        }

        return name;
    }

    /// <summary>Looks up the sibling of <paramref name="styleName"/> with the requested weight
    /// and slant, first by naming convention, then by attribute scan (same family + size).</summary>
    public static bool TryResolve(
        StyleSheet sheet,
        string styleName,
        FontWeightToken weight,
        FontSlantToken slant,
        out CharacterStyleDef def)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        CharacterStyleDef source = sheet.GetCharacterStyle(styleName);
        string expected = VariantName(BaseName(styleName), weight, slant);
        CharacterStyleDef? byName = sheet.CharacterStyles.Find(s => s.Name == expected);
        if (byName is not null && byName.Weight == weight && byName.Slant == slant)
        {
            def = byName;
            return true;
        }

        CharacterStyleDef? byShape = sheet.CharacterStyles.Find(s =>
            s.FontFamily == source.FontFamily
            && Math.Abs(s.SizePt - source.SizePt) < 0.01f
            && s.ColorArgb == source.ColorArgb
            && s.Weight == weight
            && s.Slant == slant);
        if (byShape is not null)
        {
            def = byShape;
            return true;
        }

        def = null!;
        return false;
    }

    /// <summary>Describes the missing sibling so EnsureCharacterStyleCommand can add it.</summary>
    public static CharacterStyleDef Derive(CharacterStyleDef from, string name, FontWeightToken weight, FontSlantToken slant)
    {
        ArgumentNullException.ThrowIfNull(from);
        return new CharacterStyleDef
        {
            Name = name,
            FontFamily = from.FontFamily,
            SizePt = from.SizePt,
            ColorArgb = from.ColorArgb,
            Weight = weight,
            Slant = slant,
        };
    }
}
