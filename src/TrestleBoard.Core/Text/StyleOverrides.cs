using System.Globalization;
using System.Text;
using TrestleBoard.Core.Model;

namespace TrestleBoard.Core.Text;

/// <summary>
/// Naming for "use a different font just here" (PLAN.md M14).
/// <para>
/// The override does NOT put direct formatting on runs. "Runs never carry direct formatting in
/// v1" is locked in <see cref="CharacterStyleResolver"/>'s header and docs/M4-spec.md; breaking
/// it would touch the resolver, the adapter, the serializer, the canonicaliser and every text
/// command. Instead a derived style is minted and applied by reference — the same machinery
/// bold and italic already use, so no new command type is needed.
/// </para>
/// <para>
/// The <c>~</c> separator is the load-bearing detail. <see cref="CharacterStyleResolver.BaseName"/>
/// strips only <c>-bold</c>/<c>-italic</c>, so <c>body~ebgaramond-bold</c> bases to
/// <c>body~ebgaramond</c>: the sibling machinery keeps working INSIDE an override, which is
/// exactly what is needed when the user bolds a word in an overridden span. And because the
/// resolver's attribute scan matches on font family, the override group cannot cross-match the
/// base group. Both properties fall out of the existing convention for free.
/// </para>
/// </summary>
public static class StyleOverrides
{
    public const char Separator = '~';

    /// <summary>
    /// The derived style name for an override of <paramref name="baseStyleName"/>. The size is
    /// only part of the name when it differs from the base role's size, so a family-only
    /// override of Body reuses one derived style across the whole document.
    /// </summary>
    public static string NameFor(string baseStyleName, string fontFamily, float sizePt, float baseSizePt)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseStyleName);
        ArgumentException.ThrowIfNullOrEmpty(fontFamily);
        string name = $"{CharacterStyleResolver.BaseName(baseStyleName)}{Separator}{Slug(fontFamily)}";
        if (Math.Abs(sizePt - baseSizePt) >= 0.01f)
        {
            return name + "-" + Slug(sizePt.ToString("0.##", CultureInfo.InvariantCulture));
        }

        return name;
    }

    /// <summary>True when the style name was minted as a text-level override.</summary>
    public static bool IsOverride(string styleName)
    {
        ArgumentException.ThrowIfNullOrEmpty(styleName);
        return styleName.Contains(Separator, StringComparison.Ordinal);
    }

    /// <summary>The role an override derives from, or the name unchanged when it is not one.</summary>
    public static string RoleOf(string styleName)
    {
        ArgumentException.ThrowIfNullOrEmpty(styleName);
        string baseName = CharacterStyleResolver.BaseName(styleName);
        int cut = baseName.IndexOf(Separator, StringComparison.Ordinal);
        return cut < 0 ? baseName : baseName[..cut];
    }

    /// <summary>
    /// Describes an override the way the action panel says it: "This text uses EB Garamond
    /// instead of the Body text font."
    /// </summary>
    public static string Describe(CharacterStyleDef style, CharacterStyleDef role)
    {
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(role);
        bool familyDiffers = !string.Equals(style.FontFamily, role.FontFamily, StringComparison.Ordinal);
        bool sizeDiffers = Math.Abs(style.SizePt - role.SizePt) >= 0.01f;
        string roleLabel = StyleLabels.Describe(role.Name);

        if (familyDiffers && sizeDiffers)
        {
            return $"This text uses {style.FontFamily} at {Size(style.SizePt)} instead of the "
                   + $"{roleLabel} font.";
        }

        if (familyDiffers)
        {
            return $"This text uses {style.FontFamily} instead of the {roleLabel} font.";
        }

        if (sizeDiffers)
        {
            return $"This text is {Size(style.SizePt)} instead of the {roleLabel} size.";
        }

        return $"This text uses its own copy of the {roleLabel} font.";
    }

    /// <summary>"11 pt", "8.5 pt" — never "11.0 pt".</summary>
    public static string Size(float sizePt) =>
        sizePt.ToString("0.##", CultureInfo.InvariantCulture) + " pt";

    /// <summary>Lowercases and strips everything that is not a letter or digit: "EB Garamond" → "ebgaramond".</summary>
    public static string Slug(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var slug = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                slug.Append(char.ToLowerInvariant(c));
            }
        }

        return slug.Length == 0 ? "x" : slug.ToString();
    }
}
