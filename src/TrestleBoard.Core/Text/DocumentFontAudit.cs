using TrestleBoard.Core.Model;

namespace TrestleBoard.Core.Text;

/// <summary>One font family a document names that this build does not bundle.</summary>
public sealed record UnknownFontUse(string FontFamily, IReadOnlyList<string> StyleNames);

/// <summary>
/// Finds the font families a document names that this build cannot draw (PLAN.md M14).
/// <para>
/// The known-family set arrives as a PARAMETER — Core references the BCL and nothing else, and
/// must never learn about Layout. This mirrors §5's roster projection rule: the pure function
/// lives low, the wiring lives in App.
/// </para>
/// <para>
/// Run this at OPEN, not at paint. Before M14 a document naming an unbundled family threw out
/// of the paint pass with no net; once fonts are choosable and different builds bundle different
/// families, that path becomes genuinely reachable.
/// </para>
/// </summary>
public static class DocumentFontAudit
{
    /// <summary>
    /// Every family the document's character styles name that is not in
    /// <paramref name="knownFamilies"/>, with the styles that name it. Ordered by family name so
    /// the warning text is stable.
    /// </summary>
    public static IReadOnlyList<UnknownFontUse> FindUnknownFamilies(
        Document document,
        IReadOnlyCollection<string> knownFamilies)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(knownFamilies);
        var known = new HashSet<string>(knownFamilies, StringComparer.Ordinal);

        return document.StyleSheet.CharacterStyles
            .Where(s => !known.Contains(s.FontFamily))
            .GroupBy(s => s.FontFamily, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new UnknownFontUse(
                g.Key,
                g.Select(s => s.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray()))
            .ToArray();
    }

    /// <summary>
    /// The plain-language warning ShowPackage puts up before the first paint, or null when the
    /// document is fine. Four sentences, in this order and for these reasons: what is wrong,
    /// what the app will do about it, that the newsletter itself is untouched, and that a newer
    /// TrestleBoard will render it properly.
    /// </summary>
    public static string? DescribeWarning(
        IReadOnlyList<UnknownFontUse> unknown,
        string substituteFamily)
    {
        ArgumentNullException.ThrowIfNull(unknown);
        ArgumentException.ThrowIfNullOrEmpty(substituteFamily);
        if (unknown.Count == 0)
        {
            return null;
        }

        string names = JoinNames(unknown.Select(u => u.FontFamily).ToArray());
        string opening = unknown.Count == 1
            ? $"This newsletter uses a font this copy of TrestleBoard does not have: {names}."
            : $"This newsletter uses fonts this copy of TrestleBoard does not have: {names}.";

        return opening
               + $" That text will be shown in {substituteFamily} instead."
               + " The newsletter itself is not changed — nothing is lost, and it will save back"
               + " exactly as it was."
               + " A newer version of TrestleBoard will show it properly.";
    }

    private static string JoinNames(string[] names) => names.Length switch
    {
        1 => names[0],
        2 => $"{names[0]} and {names[1]}",
        _ => string.Join(", ", names[..^1]) + " and " + names[^1],
    };
}
