using System.Runtime.InteropServices;
using HarfBuzzSharp;
using SkiaSharp;

namespace TrestleBoard.Layout.Fonts;

public enum FontWeight
{
    Regular = 400,
    Bold = 700,
}

public enum FontStyleSlant
{
    Normal,
    Italic,
}

public readonly record struct FontKey(string Family, FontWeight Weight, FontStyleSlant Slant);

public readonly record struct FontMetrics(
    float AscentPt,
    float DescentPt,
    float LeadingPt,
    float AverageCharWidthPt);

/// <summary>
/// Owns the SKTypeface and the HarfBuzz Font/Face built from the SAME byte buffer, which is
/// what guarantees the shaper and the renderer agree glyph-for-glyph with zero re-shaping.
/// </summary>
public sealed class ResolvedFont : IDisposable
{
    /// <summary>Fixed HarfBuzz design scale; shaping output is integers in these units.</summary>
    public const int HbScale = 512;

    private readonly GCHandle _pin;
    private readonly Blob _blob;
    private bool _disposed;

    internal ResolvedFont(FontKey key, byte[] fontBytes)
    {
        Key = key;
        _pin = GCHandle.Alloc(fontBytes, GCHandleType.Pinned);
        _blob = new Blob(_pin.AddrOfPinnedObject(), fontBytes.Length, MemoryMode.ReadOnly);
        HbFace = new Face(_blob, 0);
        HbFont = new Font(HbFace);
        HbFont.SetScale(HbScale, HbScale);
        Typeface = SKTypeface.FromData(SKData.CreateCopy(fontBytes))
            ?? throw new InvalidOperationException($"SkiaSharp rejected font bytes for {key}.");
        UnitsPerEm = HbFace.UnitsPerEm;
    }

    public FontKey Key { get; }

    public int UnitsPerEm { get; }

    public SKTypeface Typeface { get; }

    public Font HbFont { get; }

    public Face HbFace { get; }

    public FontMetrics GetMetrics(float sizePt)
    {
        using var skFont = new SKFont(Typeface, sizePt);
        SKFontMetrics m = skFont.Metrics;
        float avg = 0f;
        if (HbFont.TryGetGlyph('n', out uint glyph))
        {
            avg = HbFont.GetHorizontalGlyphAdvance(glyph) * (sizePt / HbScale);
        }

        return new FontMetrics(-m.Ascent, m.Descent, m.Leading, avg);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        HbFont.Dispose();
        HbFace.Dispose();
        _blob.Dispose();
        Typeface.Dispose();
        _pin.Free();
    }
}

public sealed class FontStore : IDisposable
{
    private readonly Lock _lock = new();
    private readonly Dictionary<FontKey, byte[]> _bytes = new();
    private readonly Dictionary<FontKey, ResolvedFont> _resolved = new();
    private bool _disposed;

    public void Register(FontKey key, ReadOnlyMemory<byte> fontBytes)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _bytes[key] = fontBytes.ToArray();
        }
    }

    public ResolvedFont Resolve(FontKey key)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_resolved.TryGetValue(key, out ResolvedFont? font))
            {
                return font;
            }

            if (!_bytes.TryGetValue(key, out byte[]? bytes))
            {
                throw new KeyNotFoundException($"Font not registered: {key}. Bundled fonts only — no system fallback.");
            }

            font = new ResolvedFont(key, bytes);
            _resolved[key] = font;
            return font;
        }
    }

    public bool TryResolve(FontKey key, out ResolvedFont? font)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_resolved.TryGetValue(key, out font))
            {
                return true;
            }

            if (!_bytes.TryGetValue(key, out byte[]? bytes))
            {
                font = null;
                return false;
            }

            font = new ResolvedFont(key, bytes);
            _resolved[key] = font;
            return true;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (ResolvedFont font in _resolved.Values)
            {
                font.Dispose();
            }

            _resolved.Clear();
            _bytes.Clear();
        }
    }
}

public static class BundledFonts
{
    public const string BodyFamily = "Source Serif 4";
    public const string SansFamily = "Source Sans 3";
    public const string DisplayFamily = "Cinzel";

    private static readonly (FontKey Key, string Resource)[] Faces =
    [
        (new FontKey(BodyFamily, FontWeight.Regular, FontStyleSlant.Normal), "SourceSerif4-Regular.ttf"),
        (new FontKey(BodyFamily, FontWeight.Bold, FontStyleSlant.Normal), "SourceSerif4-Bold.ttf"),
        (new FontKey(BodyFamily, FontWeight.Regular, FontStyleSlant.Italic), "SourceSerif4-It.ttf"),
        (new FontKey(BodyFamily, FontWeight.Bold, FontStyleSlant.Italic), "SourceSerif4-BoldIt.ttf"),
        (new FontKey(SansFamily, FontWeight.Regular, FontStyleSlant.Normal), "SourceSans3-Regular.ttf"),
        (new FontKey(SansFamily, FontWeight.Bold, FontStyleSlant.Normal), "SourceSans3-Bold.ttf"),
        (new FontKey(DisplayFamily, FontWeight.Regular, FontStyleSlant.Normal), "Cinzel-Regular.ttf"),
    ];

    /// <summary>Loads the embedded OFL faces into a new store. Caller owns the store's lifetime.</summary>
    public static FontStore CreateDefaultStore()
    {
        var store = new FontStore();
        foreach ((FontKey key, string resource) in Faces)
        {
            store.Register(key, LoadResource(resource));
        }

        return store;
    }

    private static byte[] LoadResource(string name)
    {
        string logicalName = "TrestleBoard.Layout.Fonts." + name;
        using Stream stream = typeof(BundledFonts).Assembly.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException($"Embedded font resource missing: {logicalName}");
        using var buffer = new MemoryStream(capacity: (int)stream.Length);
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
