namespace TrestleBoard.Editing;

/// <summary>Clipboard seam so the controller stays Avalonia-free; App supplies the adapter
/// over TopLevel.Clipboard, tests supply a fake (docs/M4-spec.md §5.6).</summary>
public interface ITextClipboard
{
    Task<string?> GetTextAsync();

    Task SetTextAsync(string text);
}

/// <summary>Page-point rect the view should scroll into view (caret moved, possibly onto
/// another page via a linked frame).</summary>
public sealed class CaretRevealEventArgs(int pageIndex, float leftPt, float topPt, float rightPt, float bottomPt)
    : EventArgs
{
    public int PageIndex { get; } = pageIndex;

    public float LeftPt { get; } = leftPt;

    public float TopPt { get; } = topPt;

    public float RightPt { get; } = rightPt;

    public float BottomPt { get; } = bottomPt;
}

public enum CaretMotion
{
    Left,
    Right,
    Up,
    Down,
    WordLeft,
    WordRight,
    LineStart,
    LineEnd,
    StoryStart,
    StoryEnd,
    PageUp,
    PageDown,
}
