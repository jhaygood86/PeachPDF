using PeachDrawing.Abstractions;
using System;
using System.Numerics;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Composites overlapping same-colored pattern strokes once, after their opaque shapes have been
    /// painted into a Form XObject. Clipping strokes at a shared corner still double-blends edge
    /// pixels in PDF rasterizers when the clip boundary falls between device pixels.
    /// </summary>
    internal static class PatternedStrokeOpacity
    {
        internal static void Paint(
            Canvas g, Rect bounds, PaintColor color, Action<Canvas, PaintColor> paint)
        {
            // A tight tile also works when the caller has no page-sized clip (e.g. a form-field
            // appearance). Leave one point of breathing room for antialiasing at the outer edge.
            var padding = g.PixelsPerPoint;
            var tileRect = Rect.FromLTRB(
                bounds.Left - padding, bounds.Top - padding,
                bounds.Right + padding, bounds.Bottom + padding);
            if (g.CreateTile(tileRect.Width, tileRect.Height) is not { } tile)
            {
                paint(g, color);
                return;
            }

            var opaque = color.IsCmyk
                ? PaintColor.FromCmyk(byte.MaxValue, color.C, color.M, color.Y, color.K)
                : PaintColor.FromArgb(byte.MaxValue, color.R, color.G, color.B);

            using (tile.Image)
            {
                using (tile.Graphics)
                {
                    tile.Graphics.PushTransform(new Matrix3x2(
                        1, 0, 0, 1, (float)-tileRect.Left, (float)-tileRect.Top));
                    paint(tile.Graphics, opaque);
                }

                g.DrawImageWithOpacity(tile.Image, tileRect, color.A / (double)byte.MaxValue);
            }
        }
    }
}
