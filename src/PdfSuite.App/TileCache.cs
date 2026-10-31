using Avalonia.Media.Imaging;

namespace PdfSuite.App;

/// <summary>Identifies a tile. Preview tiles cover a whole page and use X = Y = -1.</summary>
internal readonly record struct TileKey(int Page, long ScaleKey, int X, int Y)
{
    public bool IsPreview => X < 0;

    /// <summary>Scales are compared at 1/10,000 pixel per point.</summary>
    public static long ToScaleKey(double pixelsPerPoint) => (long)Math.Round(pixelsPerPoint * 10_000);
}

/// <summary>
/// Least-recently-used tile bitmaps under a byte budget. Tiles drawn in the current frame are
/// never evicted, so the cache can exceed its budget briefly rather than flicker.
/// </summary>
internal sealed class TileCache(long capacityBytes)
{
    private readonly Dictionary<TileKey, LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _byRecency = new();

    public long Bytes { get; private set; }

    public int Count => _entries.Count;

    public bool TryGet(TileKey key, long frame, out WriteableBitmap bitmap)
    {
        if (_entries.TryGetValue(key, out LinkedListNode<Entry>? node))
        {
            node.Value.LastUsedFrame = frame;
            _byRecency.Remove(node);
            _byRecency.AddLast(node);
            bitmap = node.Value.Bitmap;
            return true;
        }

        bitmap = null!;
        return false;
    }

    public bool Contains(TileKey key) => _entries.ContainsKey(key);

    public void Add(TileKey key, WriteableBitmap bitmap, long bytes, long frame)
    {
        if (_entries.Remove(key, out LinkedListNode<Entry>? existing))
        {
            Remove(existing);
        }

        var node = _byRecency.AddLast(new Entry(key, bitmap, bytes) { LastUsedFrame = frame });
        _entries[key] = node;
        Bytes += bytes;

        while (Bytes > capacityBytes && _byRecency.First is { } oldest && oldest.Value.LastUsedFrame < frame)
        {
            _entries.Remove(oldest.Value.Key);
            Remove(oldest);
        }
    }

    public void Clear()
    {
        foreach (Entry entry in _byRecency)
        {
            entry.Bitmap.Dispose();
        }

        _entries.Clear();
        _byRecency.Clear();
        Bytes = 0;
    }

    private void Remove(LinkedListNode<Entry> node)
    {
        _byRecency.Remove(node);
        Bytes -= node.Value.Bytes;
        node.Value.Bitmap.Dispose();
    }

    private sealed class Entry(TileKey key, WriteableBitmap bitmap, long bytes)
    {
        public TileKey Key { get; } = key;

        public WriteableBitmap Bitmap { get; } = bitmap;

        public long Bytes { get; } = bytes;

        public long LastUsedFrame { get; set; }
    }
}
