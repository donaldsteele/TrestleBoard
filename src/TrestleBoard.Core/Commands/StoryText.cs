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

    /// <summary>
    /// Splits at a paragraph-relative offset into (head, tail). Run styles are preserved on
    /// both sides; both halves are canonical. The input paragraph is not mutated.
    /// </summary>
    public static (StoryParagraph Head, StoryParagraph Tail) SplitAt(StoryParagraph paragraph, int offset)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, paragraph.Length);

        StoryParagraph head = paragraph.Clone();
        head.Runs.Clear();
        StoryParagraph tail = paragraph.Clone();
        tail.Runs.Clear();

        int consumed = 0;
        foreach (StoryRun run in paragraph.Runs)
        {
            int runStart = consumed;
            int runEnd = consumed + run.Text.Length;
            if (runEnd <= offset)
            {
                head.Runs.Add(run.Clone());
            }
            else if (runStart >= offset)
            {
                tail.Runs.Add(run.Clone());
            }
            else
            {
                StoryRun left = run.Clone();
                left.Text = run.Text[..(offset - runStart)];
                head.Runs.Add(left);
                StoryRun right = run.Clone();
                right.Text = run.Text[(offset - runStart)..];
                tail.Runs.Add(right);
            }

            consumed = runEnd;
        }

        return (head, tail);
    }

    /// <summary>Restores the canonical form: drops empty runs, merges same-style neighbours.</summary>
    public static void Canonicalize(StoryParagraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        paragraph.Runs.RemoveAll(r => r.Text.Length == 0);
        MergeAdjacent(paragraph, 1);
    }

    /// <summary>
    /// Ensures a run boundary exists exactly at <paramref name="offset"/> and returns the index
    /// of the run starting there (Runs.Count when offset == paragraph length).
    /// </summary>
    public static int SplitRunAt(StoryParagraph paragraph, int offset)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, paragraph.Length);
        if (offset == paragraph.Length)
        {
            return paragraph.Runs.Count;
        }

        (int runIndex, int local) = Locate(paragraph, offset, preferRight: true);
        if (local == 0)
        {
            return runIndex;
        }

        StoryRun run = paragraph.Runs[runIndex];
        StoryRun right = run.Clone();
        right.Text = run.Text[local..];
        run.Text = run.Text[..local];
        paragraph.Runs.Insert(runIndex + 1, right);
        return runIndex + 1;
    }

    /// <summary>
    /// Applies (or clears, with null) a named character style over [offset, offset+length):
    /// split runs at both boundaries, assign, re-canonicalize (docs/M4-spec.md §5.2).
    /// </summary>
    public static void SetCharacterStyle(StoryParagraph paragraph, int offset, int length, string? styleRef)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset + length, paragraph.Length);
        if (length == 0)
        {
            return;
        }

        int first = SplitRunAt(paragraph, offset);
        int afterLast = SplitRunAt(paragraph, offset + length);
        for (int i = first; i < afterLast; i++)
        {
            paragraph.Runs[i].CharacterStyleRef = styleRef;
        }

        Canonicalize(paragraph);
    }

    /// <summary>Effective style ref at an offset (left-neighbour rule, as Insert uses).
    /// Null = paragraph default.</summary>
    public static string? StyleRefAt(StoryParagraph paragraph, int offset)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        if (paragraph.Runs.Count == 0)
        {
            return null;
        }

        offset = Math.Clamp(offset, 0, paragraph.Length);
        (int runIndex, _) = Locate(paragraph, offset);
        return paragraph.Runs[runIndex].CharacterStyleRef;
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
