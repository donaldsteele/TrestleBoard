using TrestleBoard.Core.Model;

namespace TrestleBoard.Core.Commands;

/// <summary>
/// Run-list text surgery for paragraph-relative character offsets (the coordinate system Layout's
/// SourceSpan uses). Canonical form invariant: no two adjacent runs share a CharacterStyleRef —
/// Insert and Delete both preserve it.
/// </summary>
public static class StoryText
{
    /// <summary>Extracts the paragraph's full text (debug/tests).</summary>
    public static string GetText(StoryParagraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        return string.Concat(paragraph.Runs.Select(r => r.Text));
    }

    /// <summary>
    /// Inserts text at a paragraph-relative offset. The text adopts the style of the run
    /// containing the offset (the left neighbor at a boundary); an empty paragraph gains a
    /// run with the paragraph-default style (null ref).
    /// </summary>
    public static void Insert(StoryParagraph paragraph, int offset, string text)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, paragraph.Length);
        if (text.Length == 0)
        {
            return;
        }

        if (paragraph.Runs.Count == 0)
        {
            paragraph.Runs.Add(new StoryRun { Text = text });
            return;
        }

        (int runIndex, int local) = Locate(paragraph, offset);
        StoryRun run = paragraph.Runs[runIndex];
        run.Text = run.Text.Insert(local, text);
    }

    /// <summary>
    /// Deletes [offset, offset+length), merging same-style neighbors that become adjacent so the
    /// canonical form holds. Range must lie within the paragraph.
    /// </summary>
    public static void Delete(StoryParagraph paragraph, int offset, int length)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset + length, paragraph.Length);
        if (length == 0)
        {
            return;
        }

        int remaining = length;
        (int runIndex, int local) = Locate(paragraph, offset, preferRight: true);
        while (remaining > 0)
        {
            StoryRun run = paragraph.Runs[runIndex];
            int take = Math.Min(remaining, run.Text.Length - local);
            run.Text = run.Text.Remove(local, take);
            remaining -= take;
            if (run.Text.Length == 0)
            {
                paragraph.Runs.RemoveAt(runIndex);
            }
            else
            {
                runIndex++;
            }

            local = 0;
        }

        MergeAdjacent(paragraph, Math.Max(0, runIndex - 1));
    }

    /// <summary>
    /// Maps a paragraph-relative offset to (runIndex, offsetInRun). At a run boundary the left
    /// run wins (insertion adopts left style) unless <paramref name="preferRight"/>.
    /// </summary>
    public static (int RunIndex, int Local) Locate(StoryParagraph paragraph, int offset, bool preferRight = false)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        int consumed = 0;
        for (int i = 0; i < paragraph.Runs.Count; i++)
        {
            int len = paragraph.Runs[i].Text.Length;
            int local = offset - consumed;
            if (local < len || (!preferRight && local == len))
            {
                return (i, local);
            }

            consumed += len;
        }

        throw new ArgumentOutOfRangeException(nameof(offset), $"Offset {offset} outside paragraph of length {consumed}.");
    }

    private static void MergeAdjacent(StoryParagraph paragraph, int startIndex)
    {
        for (int i = Math.Max(1, startIndex); i < paragraph.Runs.Count;)
        {
            StoryRun left = paragraph.Runs[i - 1];
            StoryRun right = paragraph.Runs[i];
            if (string.Equals(left.CharacterStyleRef, right.CharacterStyleRef, StringComparison.Ordinal))
            {
                left.Text += right.Text;
                paragraph.Runs.RemoveAt(i);
            }
            else
            {
                i++;
            }
        }
    }
}
