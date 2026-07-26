using SkiaSharp;

namespace TrestleBoard.Imaging;

/// <summary>
/// Bounded LRU of rendered photos keyed by (asset, recipe hash, size bucket) — docs/M6-spec.md §5.
/// A slider drag re-renders once per distinct value and repaints from cache afterwards; evicted
/// images are disposed, so a long editing session does not leak decoded bitmaps.
///
/// Not thread-safe by design: it lives behind the render source, which is UI-thread owned.
/// </summary>
public sealed class RecipeCache(int capacity = 32) : IDisposable
{
    private readonly Dictionary<string, LinkedListNode<Entry>> _index = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> _order = new();
    private readonly int _capacity = capacity > 0
        ? capacity
        : throw new ArgumentOutOfRangeException(nameof(capacity));

    private bool _disposed;

    public int Count => _index.Count;

    /// <summary>Times a lookup was served from cache; the drag tests assert on it.</summary>
    public int HitCount { get; private set; }

    /// <summary>Times a lookup had to render.</summary>
    public int MissCount { get; private set; }

    public static string BuildKey(string assetRef, ImageRecipeSpec recipe, int maxPixelSize) =>
        $"{assetRef}|{recipe.CacheKeyPart()}|{maxPixelSize}";

    /// <summary>Returns the cached render, producing it with <paramref name="render"/> on a miss.
    /// The cache owns the returned image — callers draw it, they never dispose it.</summary>
    public SKImage GetOrAdd(string assetRef, ImageRecipeSpec recipe, int maxPixelSize, Func<SKImage> render)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(render);

        string key = BuildKey(assetRef, recipe, maxPixelSize);
        if (_index.TryGetValue(key, out LinkedListNode<Entry>? found))
        {
            HitCount++;
            _order.Remove(found);
            _order.AddFirst(found);
            return found.Value.Image;
        }

        MissCount++;
        SKImage image = render();
        LinkedListNode<Entry> node = _order.AddFirst(new Entry(key, image));
        _index[key] = node;
        Trim();
        return image;
    }

    /// <summary>Drops every render of one asset — used when its bytes are replaced.</summary>
    public void InvalidateAsset(string assetRef)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string prefix = assetRef + "|";
        foreach (string key in _index.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            LinkedListNode<Entry> node = _index[key];
            node.Value.Image.Dispose();
            _order.Remove(node);
            _index.Remove(key);
        }
    }

    public void Clear()
    {
        foreach (Entry entry in _order)
        {
            entry.Image.Dispose();
        }

        _order.Clear();
        _index.Clear();
    }

    private void Trim()
    {
        while (_index.Count > _capacity && _order.Last is { } last)
        {
            last.Value.Image.Dispose();
            _index.Remove(last.Value.Key);
            _order.RemoveLast();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Clear();
    }

    private readonly record struct Entry(string Key, SKImage Image);
}
