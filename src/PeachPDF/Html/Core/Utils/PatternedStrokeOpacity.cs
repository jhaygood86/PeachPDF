using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using System;

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
            RGraphics g, RRect bounds, RColor color, Action<RGraphics, RColor> paint)
        {
            // A tight tile also works when the caller has no page-sized clip (e.g. a form-field
            // appearance). Leave one point of breathing room for antialiasing at the outer edge.
            var padding = g.PixelsPerPoint;
            var tileRect = RRect.FromLTRB(
                bounds.Left - padding, bounds.Top - padding,
                bounds.Right + padding, bounds.Bottom + padding);
            if (g.CreateTile(tileRect.Width, tileRect.Height) is not { } tile)
            {
                paint(g, color);
                return;
            }

            var opaque = color.IsCmyk
                ? RColor.FromCmyk(byte.MaxValue, color.C, color.M, color.Y, color.K)
                : RColor.FromArgb(byte.MaxValue, color.R, color.G, color.B);

            using (tile.Image)
            {
                using (tile.Graphics)
                {
                    tile.Graphics.PushTransform(new RMatrix(
                        1, 0, 0, 1, -tileRect.Left, -tileRect.Top));
                    paint(tile.Graphics, opaque);
                }

                g.DrawImageWithOpacity(tile.Image, tileRect, color.A / (double)byte.MaxValue);
            }
        }
    }
}
