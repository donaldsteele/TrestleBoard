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
    /// <summary>
    /// Insertion order IS the order the font window lists the roles in (PLAN.md M20 (h)).
    /// <para>
    /// Before M20 the window sorted by raw style name, so the list read "Body text, Cover title,
    /// Headings, Photo captions…" — alphabetical in a name the user is never shown, which is the
    /// same as random to them. This order is the one a page is read in: the cover title, then the
    /// headings, then the words under them, then the things that hang off the page.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> Known = new(StringComparer.Ordinal)
    {
        ["display"] = "Cover title",
        ["heading"] = "Headings",
        ["subheading"] = "Smaller headings",
        ["body"] = "Body text",
        ["quote"] = "Quotations",
        ["caption"] = "Photo captions",
        ["table-header"] = "Table headings",
        ["table"] = "Tables",
    };

    private static readonly string[] Order = [.. Known.Keys];

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

    /// <summary>
    /// The roles in the order a page is read in — the order the font window lists them in.
    /// </summary>
    public static IReadOnlyList<string> DeclaredOrder => Order;

    /// <summary>
    /// Where a role sits in <see cref="DeclaredOrder"/>. A role this build has never heard of
    /// sorts after every known one rather than being hidden or guessed at; ties between unknown
    /// roles are broken by the caller, ordinally, so the list is still stable.
    /// </summary>
    public static int OrderOf(string styleName)
    {
        ArgumentException.ThrowIfNullOrEmpty(styleName);
        int index = Array.IndexOf(Order, CharacterStyleResolver.BaseName(styleName));
        return index < 0 ? Order.Length : index;
    }
}
