using Avalonia.Controls;
using Avalonia.Input.Platform;
using TrestleBoard.Editing;

namespace TrestleBoard.App.Canvas;

/// <summary>App-side clipboard adapter over Avalonia's async clipboard (docs/M4-spec.md §5.6).</summary>
public sealed class AvaloniaTextClipboard(TopLevel topLevel) : ITextClipboard
{
    private IClipboard? Clipboard => topLevel.Clipboard;

    public async Task<string?> GetTextAsync() =>
        Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;

    public async Task SetTextAsync(string text)
    {
        if (Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }
}
