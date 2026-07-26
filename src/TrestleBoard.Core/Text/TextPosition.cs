namespace TrestleBoard.Core.Text;

/// <summary>
/// Which side of a line/segment boundary a caret belongs to (docs/M4-spec.md §2). An offset
/// that is both the end of one line/segment and the start of the next can display in two
/// places; affinity picks one. Leading = belongs to the text that FOLLOWS (draws at the start
/// of the next line/segment). Trailing = belongs to the text that PRECEDES.
/// </summary>
public enum TextAffinity
{
    Leading,
    Trailing,
}

/// <summary>A document coordinate: paragraph-relative UTF-16 offset inside a story.</summary>
public readonly record struct TextPosition(string StoryId, int ParagraphIndex, int Offset)
    : IComparable<TextPosition>
{
    public int CompareTo(TextPosition other)
    {
        int c = string.CompareOrdinal(StoryId, other.StoryId);
        if (c != 0)
        {
            return c;
        }

        c = ParagraphIndex.CompareTo(other.ParagraphIndex);
        return c != 0 ? c : Offset.CompareTo(other.Offset);
    }

    public static bool operator <(TextPosition a, TextPosition b) => a.CompareTo(b) < 0;

    public static bool operator <=(TextPosition a, TextPosition b) => a.CompareTo(b) <= 0;

    public static bool operator >(TextPosition a, TextPosition b) => a.CompareTo(b) > 0;

    public static bool operator >=(TextPosition a, TextPosition b) => a.CompareTo(b) >= 0;
}

/// <summary>A caret: a position plus display affinity. Affinity never participates in
/// document-coordinate comparisons — it is display-only (docs/M4-spec.md §2.1).</summary>
public readonly record struct CaretPosition(TextPosition Position, TextAffinity Affinity)
{
    public string StoryId => Position.StoryId;

    public int ParagraphIndex => Position.ParagraphIndex;

    public int Offset => Position.Offset;

    public static CaretPosition Leading(TextPosition p) => new(p, TextAffinity.Leading);

    public static CaretPosition Trailing(TextPosition p) => new(p, TextAffinity.Trailing);
}

/// <summary>A normalized document range (Start &lt;= End).</summary>
public readonly record struct TextRange(TextPosition Start, TextPosition End)
{
    public bool IsEmpty => Start == End;

    public string StoryId => Start.StoryId;

    public bool IsSingleParagraph => Start.ParagraphIndex == End.ParagraphIndex;
}

/// <summary>Anchor stays put; Extent follows the caret. Normalization happens in Range.</summary>
public readonly record struct TextSelection(CaretPosition Anchor, CaretPosition Extent)
{
    public bool IsEmpty => Anchor.Position == Extent.Position;

    public CaretPosition Caret => Extent;

    public string StoryId => Anchor.StoryId;

    public TextRange Range => Anchor.Position <= Extent.Position
        ? new TextRange(Anchor.Position, Extent.Position)
        : new TextRange(Extent.Position, Anchor.Position);

    public static TextSelection At(CaretPosition caret) => new(caret, caret);
}
