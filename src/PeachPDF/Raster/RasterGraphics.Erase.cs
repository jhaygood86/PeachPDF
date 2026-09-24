using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using System;

namespace PeachPDF.Raster;

internal sealed partial class RasterGraphics
{
    /// <summary>
    /// Removes the pixels inside <paramref name="path"/> (in user space, like every path this class draws): each pixel is
    /// scaled by one minus the path's anti-aliased coverage, so an edge that is half inside keeps half its ink. This is
    /// the "destination-out" operation an outer <c>box-shadow</c> needs - the shadow is not painted under its own box
    /// (CSS Backgrounds 3 §7.1.1) - which no <see cref="RGraphics"/> primitive expresses.
    /// </summary>
    public void Erase(RGraphicsPath path)
    {
        var clip = _clips.Peek();
        if (clip.Bounds.IsEmpty)
            return;

        var flat = FlatPath.From(((GraphicsPathAdapter)path).GraphicsPath, 0.1 / DeviceScale);
        var polygon = new PolygonSet();
        polygon.AddTransformed(flat.Contours, UserToDevice);

        var sink = new EraseSink(this, clip);
        ScanlineRasterizer.Fill(polygon, path.FillMode == RFillMode.EvenOdd, clip.Bounds, ref sink);
    }

    /// <summary>Removes the pixels inside the layout-space rectangle <paramref name="rect"/> (see <see cref="Erase(RGraphicsPath)"/>).</summary>
    public void EraseRectangle(RRect rect)
    {
        var clip = _clips.Peek();
        if (clip.Bounds.IsEmpty)
            return;

        var polygon = new PolygonSet();
        AddDeviceRect(polygon, ToUser(rect), UserToDevice);

        var sink = new EraseSink(this, clip);
        ScanlineRasterizer.Fill(polygon, evenOdd: false, clip.Bounds, ref sink);
    }

    private readonly struct EraseSink(RasterGraphics owner, ClipState clip) : ICoverageSink
    {
        public void Span(int y, int x0, ReadOnlySpan<byte> coverage)
        {
            var bounds = clip.Bounds;
            if (y < bounds.Top || y >= bounds.Bottom)
                return;

            var start = Math.Max(x0, bounds.Left);
            var end = Math.Min(x0 + coverage.Length, bounds.Right);
            var row = owner._surface.Row(y);

            for (var x = start; x < end; x++)
            {
                int c = coverage[x - x0];
                if (c == 0)
                    continue;

                if (clip.Mask is { } mask)
                    c = PixelKernels.Div255(c * mask[(y - bounds.Top) * bounds.Width + (x - bounds.Left)]);

                var keep = 255 - c;
                var p = x * 4;
                row[p] = (byte)PixelKernels.Div255(row[p] * keep);
                row[p + 1] = (byte)PixelKernels.Div255(row[p + 1] * keep);
                row[p + 2] = (byte)PixelKernels.Div255(row[p + 2] * keep);
                row[p + 3] = (byte)PixelKernels.Div255(row[p + 3] * keep);
            }
        }
    }
}
