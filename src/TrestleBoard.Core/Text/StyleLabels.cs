namespace TrestleBoard.Core.Text;

/// <summary>
/// Plain-language names for the style roles a document actually has (PLAN.md §6, M14).
/// <para>
/// The committee must never be shown <c>body-bold-italic</c>. A lookup rather than a
/// <c>DisplayName</c> field on <see cref="Model.CharacterStyleDef"/> is deliberate: a new field
/// is a model change, a model change is a migration, and there is nothing here worth migrating
/// for. The fallback is honest rather than clever — it shows the raw name and says so.
/// </para>
/// </summary>
public static class StyleLabels
{
    private static readonly Dictionary<string, string> Known = new(StringComparer.Ordinal)
    {
        ["body"] = "Body text",
        ["heading"] = "Headings",
        ["subheading"] = "Smaller headings",
        ["caption"] = "Photo captions",
        ["quote"] = "Quotations",
        ["table"] = "Tables",
        ["table-header"] = "Table headings",
        ["display"] = "Cover title",
    };

    /// <summary>
    /// The label for a style role. Pass a base name; a variant name is reduced to its base
    /// first, because "Body text" is the answer for <c>body-bold-italic</c> too.
    /// </summary>
    public static string Describe(string styleName)
    {
        ArgumentException.ThrowIfNullOrEmpty(styleName);
        string baseName = CharacterStyleResolver.BaseName(styleName);
        if (Known.TryGetValue(baseName, out string? label))
        {
            return label;
        }

        // A template or an older document may carry a role this build has never heard of.
        // Showing the raw name beats inventing prose that might be wrong.
        return $"Style: {baseName}";
    }

    /// <summary>True when this build has a hand-written label for the role.</summary>
    public static bool IsKnown(string styleName) =>
        Known.ContainsKey(CharacterStyleResolver.BaseName(styleName));

    /// <summary>Every role this build can label, for tests and for the styles window's ordering.</summary>
    public static IReadOnlyCollection<string> KnownRoles => Known.Keys;
}
