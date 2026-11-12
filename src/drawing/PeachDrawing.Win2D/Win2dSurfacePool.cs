using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using Windows.Graphics.DirectX;

namespace PeachDrawing.Win2D;

/// <summary>
/// The render targets geometry clips draw into (see <c>Win2dCanvas.BeginSurfaceClip</c>), kept by the render context between paints. A view paints hundreds of
/// rounded cards per scroll step, and a target made and released for each was most of the churn on the device. What is kept is small: a few targets, and a
/// total of a few megabytes, the least recently returned going first.
/// </summary>
internal sealed class Win2dSurfacePool(Win2dResourceCounts counts)
{
    // What the pool keeps, in bytes: enough for the clips of a screen of cards, little beside what the page itself takes.
    private const long Budget = 24L * 1024 * 1024;

    private readonly object _gate = new();
    private readonly List<CanvasRenderTarget> _free = [];
    private long _bytes;

    /// <summary>
    /// A target for a clip that needs at least <paramref name="width"/> by <paramref name="height"/> pixels at <paramref name="dpi"/> on <paramref name="device"/>:
    /// the smallest kept one that is big enough, otherwise a new one a little over (rounded up to 64 pixels, at most <paramref name="maxWidth"/> by
    /// <paramref name="maxHeight"/>) so that the next clip a bit larger can use it. Counted as in use until it is given back.
    /// </summary>
    internal CanvasRenderTarget Take(CanvasDevice device, float dpi, DirectXPixelFormat format, int width, int height, int maxWidth, int maxHeight)
    {
        lock (_gate)
        {
            var best = -1;
            var bestPixels = long.MaxValue;
            for (var i = 0; i < _free.Count; i++)
            {
                var candidate = _free[i];
                var pixels = (long)candidate.SizeInPixels.Width * candidate.SizeInPixels.Height;
                if (candidate.Dpi == dpi && candidate.SizeInPixels.Width >= width && candidate.SizeInPixels.Height >= height && candidate.Format == format
                    && ReferenceEquals(candidate.Device, device) && pixels < bestPixels)
                {
                    (best, bestPixels) = (i, pixels);
                }
            }

            if (best >= 0)
            {
                var found = _free[best];
                _free.RemoveAt(best);
                _bytes -= Bytes(found);
                counts.ClipSurface(1);
                return found;
            }
        }

        var pixelsWide = Math.Min(maxWidth, (width + 63) & ~63);
        var pixelsHigh = Math.Min(maxHeight, (height + 63) & ~63);
        var target = new CanvasRenderTarget(device, pixelsWide * 96f / dpi, pixelsHigh * 96f / dpi, dpi, format, CanvasAlphaMode.Ignore);
        counts.ClipSurface(1);
        Win2dPaintStatistics.SurfaceMade((long)pixelsWide * pixelsHigh * 4);
        return target;
    }

    /// <summary>Gives a target back: kept for the next clip, or released when the pool is over its budget.</summary>
    internal void Return(CanvasRenderTarget target)
    {
        counts.ClipSurface(-1);
        var released = new List<CanvasRenderTarget>();
        lock (_gate)
        {
            _free.Add(target);
            _bytes += Bytes(target);
            while (_bytes > Budget && _free.Count > 0)
            {
                // The one returned first has gone the longest without being asked for.
                var oldest = _free[0];
                _free.RemoveAt(0);
                _bytes -= Bytes(oldest);
                released.Add(oldest);
            }
        }

        foreach (var gone in released)
            Release(gone);
    }

    /// <summary>Releases every kept target.</summary>
    internal void Clear()
    {
        List<CanvasRenderTarget> all;
        lock (_gate)
        {
            all = [.. _free];
            _free.Clear();
            _bytes = 0;
        }

        foreach (var target in all)
            Release(target);
    }

    private static long Bytes(CanvasRenderTarget target) => (long)target.SizeInPixels.Width * target.SizeInPixels.Height * 4;

    private static void Release(CanvasRenderTarget target)
    {
        try
        {
            target.Dispose();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or System.Runtime.InteropServices.COMException)
        {
            // Already gone with a lost device.
        }
    }
}
