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
    private readonly Dictionary<FontKey, Lazy<byte[]>> _bytes = new();
    private readonly Dictionary<FontKey, ResolvedFont> _resolved = new();
    private bool _disposed;

    /// <summary>Registers face bytes that are already in hand. Kept for tests and callers
    /// that build a store from a byte[] they own.</summary>
    public void Register(FontKey key, ReadOnlyMemory<byte> fontBytes)
    {
        byte[] copy = fontBytes.ToArray();
        Register(key, () => copy);
    }

    /// <summary>
    /// Registers a face whose bytes are fetched on first use. With 59 bundled faces, eagerly
    /// materialising every one costs ~4 MB of managed arrays at startup to serve the three or
    /// four a newsletter actually uses.
    /// </summary>
    public void Register(FontKey key, Func<byte[]> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _bytes[key] = new Lazy<byte[]>(provider, LazyThreadSafetyMode.ExecutionAndPublication);
        }
    }

    /// <summary>Every face this store can resolve, whether or not it has been materialised.</summary>
    public IReadOnlyCollection<FontKey> RegisteredKeys
    {
        get
        {
            lock (_lock)
            {
                return _bytes.Keys.ToArray();
            }
        }
    }

    /// <summary>
    /// The family to fall back to when a document names a face this build does not bundle.
    /// <para>
    /// Null — the engine default — is M7's intended loud failure: fixtures, snapshots, widget
    /// goldens and PDF export must never silently paper over a missing face. The app sets this
    /// to the body family, because there "the newsletter must still open" outranks "fail
    /// loudly", and warns the user once at open time instead (see DocumentFontAudit).
    /// </para>
    /// Never a system font, under any setting — that is what PLAN.md §1 forbids.
    /// </summary>
    public string? SubstituteFamily { get; set; }

    /// <summary>
    /// The key whose bytes would actually be used for <paramref name="requested"/>. Equal to
    /// the request when the exact face is bundled; otherwise the nearest face this store will
    /// degrade to, or null when it will refuse.
    /// </summary>
    public FontKey? ResolveKey(FontKey requested)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return FindKey(requested);
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

            FontKey? actual = FindKey(key);
            if (actual is null)
            {
                throw new KeyNotFoundException($"Font not registered: {key}. Bundled fonts only — no system fallback.");
            }

            font = Materialise(actual.Value);
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

            FontKey? actual = FindKey(key);
            if (actual is null)
            {
                font = null;
                return false;
            }

            font = Materialise(actual.Value);
            _resolved[key] = font;
            return true;
        }
    }

    /// <summary>Caller must hold the lock. Never allocates a ResolvedFont.</summary>
    private FontKey? FindKey(FontKey requested)
    {
        foreach (FontKey candidate in Ladder(requested.Family, requested))
        {
            if (_bytes.ContainsKey(candidate))
            {
                return candidate;
            }
        }

        string? substitute = SubstituteFamily;
        if (substitute is null || string.Equals(substitute, requested.Family, StringComparison.Ordinal))
        {
            return null;
        }

        foreach (FontKey candidate in Ladder(substitute, requested))
        {
            if (_bytes.ContainsKey(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Nearest-face degradation within one family: exact, then drop the slant, then drop the
    /// weight, then plain. Generalises the ladder WidgetStyleResolver already walks.
    /// </summary>
    private static IEnumerable<FontKey> Ladder(string family, FontKey requested)
    {
        yield return new FontKey(family, requested.Weight, requested.Slant);
        if (requested.Slant != FontStyleSlant.Normal)
        {
            yield return new FontKey(family, requested.Weight, FontStyleSlant.Normal);
        }

        if (requested.Weight != FontWeight.Regular)
        {
            yield return new FontKey(family, FontWeight.Regular, requested.Slant);
            yield return new FontKey(family, FontWeight.Regular, FontStyleSlant.Normal);
        }
    }

    /// <summary>Caller must hold the lock.</summary>
    private ResolvedFont Materialise(FontKey actual)
    {
        if (_resolved.TryGetValue(actual, out ResolvedFont? existing))
        {
            return existing;
        }

        var font = new ResolvedFont(actual, _bytes[actual].Value);
        _resolved[actual] = font;
        return font;
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

    /// <summary>Resource name of the concatenated OFL text that ships inside the installer.</summary>
    public const string LicenceResource = "THIRD-PARTY-FONTS.txt";

    /// <summary>
    /// Loads every embedded OFL face into a new store. Caller owns the store's lifetime.
    /// <para>
    /// Registration is lazy: 59 faces is ~4 MB of managed arrays, and a newsletter touches
    /// three or four of them. Which faces exist is <see cref="BundledFontCatalog"/>'s business.
    /// </para>
    /// </summary>
    public static FontStore CreateDefaultStore()
    {
        var store = new FontStore();
        foreach (BundledFace face in BundledFontCatalog.Faces)
        {
            string resource = face.Resource;
            store.Register(face.Key, () => LoadResource(resource));
        }

        return store;
    }

    /// <summary>The bundled families' licence text, as shown by Help → About → Fonts and licences.</summary>
    public static string ReadLicenceText()
    {
        using Stream stream = OpenResource(LicenceResource);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static byte[] LoadResource(string name)
    {
        using Stream stream = OpenResource(name);
        using var buffer = new MemoryStream(capacity: (int)stream.Length);
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static Stream OpenResource(string name)
    {
        string logicalName = "TrestleBoard.Layout.Fonts." + name;
        return typeof(BundledFonts).Assembly.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException($"Embedded font resource missing: {logicalName}");
    }
}
