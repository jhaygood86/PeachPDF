using PeachDrawing.Core;
using System;

namespace PeachDrawing;

public sealed partial class RasterCanvas
{
    /// <summary>
    /// Removes the pixels inside <paramref name="path"/> (in user space, like every path this class draws): each pixel is
    /// scaled by one minus the path's anti-aliased coverage, so an edge that is half inside keeps half its ink. This is
    /// the "destination-out" operation an outer <c>box-shadow</c> needs - the shadow is not painted under its own box
    /// (CSS Backgrounds 3 §7.1.1) - which no <see cref="Canvas"/> primitive expresses.
    /// </summary>
    public void Erase(GraphicsPath path)
    {
        var clip = _clips.Peek();
        if (clip.Bounds.IsEmpty)
            return;

        var flat = FlatPath.From(path, 0.1 / DeviceScale);
        var polygon = new PolygonSet();
        polygon.AddTransformed(flat.Contours, UserToDevice);

        var sink = new EraseSink(this, clip);
        ScanlineRasterizer.Fill(polygon, path.FillMode == FillMode.EvenOdd, clip.Bounds, ref sink, AntiAlias);
    }

    /// <summary>Removes the pixels inside the layout-space rectangle <paramref name="rect"/> (see <see cref="Erase(GraphicsPath)"/>).</summary>
    public void EraseRectangle(Rect rect)
    {
        var clip = _clips.Peek();
        if (clip.Bounds.IsEmpty)
            return;

        var polygon = new PolygonSet();
        AddDeviceRect(polygon, ToUser(rect), UserToDevice);

        var sink = new EraseSink(this, clip);
        ScanlineRasterizer.Fill(polygon, evenOdd: false, clip.Bounds, ref sink, AntiAlias);
    }

    private readonly struct EraseSink(RasterCanvas owner, ClipState clip) : ICoverageSink
    {
        public void Span(int y, int x0, ReadOnlySpan<byte> coverage)
        {
            var bounds = clip.Bounds;
            if (y < bounds.Top || y >= bounds.Bottom)
                return;

            var start = Math.Max(x0, bounds.Left);
            var end = Math.Min(x0 + coverage.Length, bounds.Right);
            var count = end - start;
            if (count <= 0)
                return;

            var work = owner.RentCoverageScratch(count);
            coverage.Slice(start - x0, count).CopyTo(work);
            if (clip.Mask is { } mask)
                PixelKernels.MultiplyCoverage(work, mask.AsSpan((y - bounds.Top) * bounds.Width + (start - bounds.Left), count));

            PixelKernels.EraseSpan(owner._surface.Row(y).Slice(start * 4, count * 4), work);
        }
    }
}
