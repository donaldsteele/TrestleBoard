using TrestleBoard.Core.Model;

namespace TrestleBoard.Core.Text;

/// <summary>
/// Finds (or describes) the (weight, slant, underline) sibling of a character style
/// (docs/M4-spec.md §6.1; underline added by M102 as M86's last deliverable).
/// Naming convention: "body", "body-bold", "body-italic", "body-bold-italic", and from M102
/// "body-underline", "body-bold-underline" and so on. Bold, italic and underline toggling
/// retargets runs at sibling styles — runs never carry direct formatting in v1.
///
/// <para><b>Underline is a third dimension rather than a "~" override</b> (which is how the "just
/// here" font and M99's colour ride). The difference that decided it: an override occupies the one
/// override slot, so underline-as-override could never combine with a colour, and a red underlined
/// heading is an ordinary thing to want. As a suffix it composes with bold and italic exactly as
/// they compose with each other, and costs one more parameter here.</para>
/// </summary>
public static class CharacterStyleResolver
{
    private const string BoldSuffix = "-bold";
    private const string ItalicSuffix = "-italic";
    private const string UnderlineSuffix = "-underline";

    /// <summary>Strips the -bold/-italic/-underline suffixes to recover the family base name.</summary>
    public static string BaseName(string styleName)
    {
        ArgumentException.ThrowIfNullOrEmpty(styleName);
        string name = styleName;

        // Stripped in the reverse of the order VariantName appends them, so every combination
        // unwinds. Underline goes on last, so it comes off first.
        if (name.EndsWith(UnderlineSuffix, StringComparison.Ordinal))
        {
            name = name[..^UnderlineSuffix.Length];
        }

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

    public static string VariantName(
        string baseName, FontWeightToken weight, FontSlantToken slant, bool underline = false)
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

        if (underline)
        {
            name += UnderlineSuffix;
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
        out CharacterStyleDef def,
        bool underline = false)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        CharacterStyleDef source = sheet.GetCharacterStyle(styleName);
        string expected = VariantName(BaseName(styleName), weight, slant, underline);
        CharacterStyleDef? byName = sheet.CharacterStyles.Find(s => s.Name == expected);
        if (byName is not null && byName.Weight == weight && byName.Slant == slant
            && byName.Underline == underline)
        {
            def = byName;
            return true;
        }

        // The attribute scan matches on underline too. Without it, asking for the underlined
        // sibling of "body" would happily return "body" itself — same family, same size, same
        // colour — and the underline would silently never appear.
        CharacterStyleDef? byShape = sheet.CharacterStyles.Find(s =>
            s.FontFamily == source.FontFamily
            && Math.Abs(s.SizePt - source.SizePt) < 0.01f
            && s.ColorArgb == source.ColorArgb
            && s.Weight == weight
            && s.Slant == slant
            && s.Underline == underline);
        if (byShape is not null)
        {
            def = byShape;
            return true;
        }

        def = null!;
        return false;
    }

    /// <summary>Describes the missing sibling so EnsureCharacterStyleCommand can add it.</summary>
    public static CharacterStyleDef Derive(
        CharacterStyleDef from,
        string name,
        FontWeightToken weight,
        FontSlantToken slant,
        bool? underline = null)
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

            // Null means "whatever it already was", so bolding underlined words keeps the
            // underline — the same way it already keeps the colour and the font.
            Underline = underline ?? from.Underline,
        };
    }
}
