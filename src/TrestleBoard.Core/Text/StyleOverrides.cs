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
            // The decimal point becomes "p", not nothing. Slug() strips every non-alphanumeric
            // character, so 8.5 pt and 85 pt both slugged to "85" and collided — and
            // TextEditorController.UseFontJustHere looks the derived style up BY NAME without
            // checking its size, so asking for a banner at 85 pt after an 8.5 pt override existed
            // silently gave you 8.5 pt (review §14.2).
            return name + "-" + SizeSlug(sizePt);
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

    /// <summary>
    /// A point size as a name fragment, with the decimal point kept as "p": 8.5 → "8p5", 85 → "85".
    /// Distinguishable, and still made only of characters a style name may contain.
    /// </summary>
    public static string SizeSlug(float sizePt) =>
        Slug(sizePt.ToString("0.##", CultureInfo.InvariantCulture).Replace(".", "p", StringComparison.Ordinal));

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

/// <summary>
/// Naming for a paragraph style that differs from its role only in which way the writing is lined
/// up (PLAN.md §11 M96).
///
/// <para>The same <c>~</c> convention <see cref="StyleOverrides"/> uses for fonts, and for the same
/// reason: alignment rides on a DERIVED style applied by reference, so nothing carries direct
/// formatting and "Paragraph style ▸" still tells the truth about what a paragraph is based on.</para>
///
/// <para><b>Left is the absence of an override, not an override to left.</b> Every style in the
/// templates is already left-aligned, so a paragraph put back to left goes back to naming its role
/// — which keeps the canonical form small and means a document that never centres anything looks
/// exactly as it did before this existed.</para>
/// </summary>
public static class ParagraphAlignmentNames
{
    /// <summary>The style name for a role lined up this way, or the role itself for left.</summary>
    public static string NameFor(string roleName, TextAlignment alignment)
    {
        ArgumentException.ThrowIfNullOrEmpty(roleName);

        string role = RoleOf(roleName);
        return alignment switch
        {
            TextAlignment.Left => role,
            TextAlignment.Center => $"{role}{StyleOverrides.Separator}centred",
            TextAlignment.Right => $"{role}{StyleOverrides.Separator}right",
            _ => throw new ArgumentOutOfRangeException(nameof(alignment)),
        };
    }

    /// <summary>The role a possibly-aligned paragraph style derives from.</summary>
    public static string RoleOf(string styleName)
    {
        ArgumentException.ThrowIfNullOrEmpty(styleName);

        int cut = styleName.IndexOf(StyleOverrides.Separator, StringComparison.Ordinal);
        return cut < 0 ? styleName : styleName[..cut];
    }

    /// <summary>
    /// A copy of <paramref name="role"/> lined up the other way, under its derived name. Everything
    /// else — the character style it points at, its spacing, its indent — is carried across, so
    /// centring a heading does not quietly change how much air is around it.
    /// </summary>
    public static ParagraphStyleDef Derive(ParagraphStyleDef role, TextAlignment alignment)
    {
        ArgumentNullException.ThrowIfNull(role);

        return new ParagraphStyleDef
        {
            Name = NameFor(role.Name, alignment),
            CharacterStyleRef = role.CharacterStyleRef,
            LineSpacing = role.LineSpacing,
            SpaceBeforePt = role.SpaceBeforePt,
            SpaceAfterPt = role.SpaceAfterPt,
            FirstLineIndentPt = role.FirstLineIndentPt,
            Align = alignment,
            ExtraProperties = role.ExtraProperties is null
                ? null
                : new Dictionary<string, System.Text.Json.JsonElement>(role.ExtraProperties),
        };
    }
}
