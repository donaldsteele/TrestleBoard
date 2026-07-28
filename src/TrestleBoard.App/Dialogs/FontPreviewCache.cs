using Avalonia.Media.Imaging;
using TrestleBoard.Layout.Fonts;
using TrestleBoard.Rendering;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// Rasterised preview lines, kept for as long as the window that asked for them (PLAN.md M20 (j)).
/// <para>
/// Before M20 every keystroke in the font window's search box rebuilt every row, and every row
/// re-shaped and re-rasterised two lines of text through HarfBuzz and Skia. Twenty families, two
/// previews each, per keystroke. Nothing about the pixels had changed: the same family at the same
/// size showing the same words is the same PNG.
/// </para>
/// <para>
/// The key is (family, size, text, foreground, background). Weight and slant are not in it because
/// this window only ever previews the Regular face — if that stops being true the key gains a
/// <see cref="FontKey"/> and the cache keeps working. Colours ARE in the key: they arrive as
/// parameters (never from a static), so a window opened in the dark theme must not be handed the
/// light theme's bitmaps.
/// </para>
/// <para>
/// The bitmaps are NOT disposed when the window closes. A render pass can still be queued against
/// the rows the window drew, and a disposed <see cref="Bitmap"/> under one of those is a crash in
/// the compositor; the cache is bounded by the bundled catalogue, so the collector is the right
/// owner once the window is gone.
/// </para>
/// <para>
/// One <see cref="Bitmap"/> instance is shared by every <c>Image</c> that asks for the same key.
/// That is safe — Avalonia treats an image source as immutable — and it is the point: the role
/// list and the font list overlap heavily.
/// </para>
/// </summary>
internal sealed class FontPreviewCache
{
    private readonly Dictionary<PreviewKey, Bitmap> _bitmaps = [];
    private readonly FontStore _fonts;

    public FontPreviewCache(FontStore fonts)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        _fonts = fonts;
    }

    /// <summary>How many asks were answered from memory, for the M20 tests.</summary>
    public int Hits { get; private set; }

    /// <summary>How many asks had to go through HarfBuzz and Skia, for the M20 tests.</summary>
    public int Misses { get; private set; }

    public Bitmap Get(string family, float sizePt, string text, uint foreground, uint background)
    {
        ArgumentException.ThrowIfNullOrEmpty(family);
        ArgumentNullException.ThrowIfNull(text);

        var key = new PreviewKey(family, sizePt, text, foreground, background);
        if (_bitmaps.TryGetValue(key, out Bitmap? cached))
        {
            Hits++;
            return cached;
        }

        Misses++;
        FontPreview preview = FontPreviewRenderer.Render(
            _fonts,
            new FontKey(family, FontWeight.Regular, FontStyleSlant.Normal),
            sizePt,
            text,
            foreground,
            background,
            scale: 1.5f);
        using var stream = new MemoryStream(preview.Png);
        var bitmap = new Bitmap(stream);
        _bitmaps[key] = bitmap;
        return bitmap;
    }

    private readonly record struct PreviewKey(
        string Family, float SizePt, string Text, uint Foreground, uint Background);
}
