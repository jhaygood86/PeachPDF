using Microsoft.Graphics.Canvas;
using System;
using System.Threading;
using System.Collections.Generic;

namespace PeachDrawing.Win2D;

/// <summary>
/// The GPU bitmaps of text runs ready to draw: a run's coverage already turned into pixels of one text colour (and, for LCD text over a flat
/// background, of that background too), so drawing a run again is one <c>DrawImage</c> with no blending on the CPU, no pixel array and no new
/// bitmap. Kept by the render context, which owns and releases them; a device that changes or is lost empties it.
/// </summary>
internal sealed class Win2dTextBitmapCache(Win2dResourceCounts counts)
{
    private const long Budget = 64L * 1024 * 1024;

    /// <summary>A run's mask, its text colour, which of the two layers the bitmap is for (stripe alphas, or ink times alpha), the stripe order and the DPI.</summary>
    internal readonly record struct Key(Win2dTextMaskCache.Key Mask, uint Ink, bool InkLayer, bool Bgr, float Dpi);

    private sealed record Entry(Key Key, CanvasBitmap Bitmap, long Bytes);

    private readonly Lock _gate = new();
    private readonly Dictionary<Key, LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _order = new();
    private object? _device;
    private long _bytes;

    internal long ByteCount => _bytes;

    internal int Hits { get; private set; }

    internal int Misses { get; private set; }

    internal int Count
    {
        get
        {
            lock (_gate)
                return _entries.Count;
        }
    }

    /// <summary>The bitmap for <paramref name="key"/>, made by <paramref name="make"/> the first time it is asked for. Used at once by the caller, before anything else is added.</summary>
    internal CanvasBitmap GetOrAdd(object device, Key key, Func<CanvasBitmap> make, long bytes)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_device, device))
            {
                // Bitmaps belong to the device that made them.
                ClearLocked();
                _device = device;
            }

            if (_entries.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                Hits++;
                return node.Value.Bitmap;
            }

            Misses++;
            var bitmap = make();
            counts.TextBitmap(1);
            _entries[key] = _order.AddFirst(new Entry(key, bitmap, bytes));
            _bytes += bytes;
            while (_bytes > Budget && _order.Last is { } oldest && !ReferenceEquals(oldest.Value.Bitmap, bitmap))
            {
                _order.RemoveLast();
                _entries.Remove(oldest.Value.Key);
                _bytes -= oldest.Value.Bytes;
                oldest.Value.Bitmap.Dispose();
                counts.TextBitmap(-1);
            }

            return bitmap;
        }
    }

    /// <summary>Releases every bitmap.</summary>
    internal void Clear()
    {
        lock (_gate)
            ClearLocked();
    }

    private void ClearLocked()
    {
        foreach (var entry in _order)
        {
            try
            {
                entry.Bitmap.Dispose();
            }
            catch (Exception ex) when (ex is ObjectDisposedException or System.Runtime.InteropServices.COMException)
            {
                // Already gone with a lost device.
            }

            counts.TextBitmap(-1);
        }

        _order.Clear();
        _entries.Clear();
        _bytes = 0;
        _device = null;
    }
}
