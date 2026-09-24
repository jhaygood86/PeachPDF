using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Raster;
using PeachPDF.Raster.Filters;

namespace PeachPDF.Html.Core.Paint
{
    internal sealed partial class FragmentPainter
    {
        /// <summary>
        /// Paints a blurred <em>outer</em> <c>box-shadow</c> layer as a real Gaussian blur of the shadow shape, in a bitmap.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see href="https://www.w3.org/TR/css-backgrounds-3/#shadow-blur">CSS Backgrounds 3 §7.2</see>: the blur radius
        /// is twice the standard deviation of the Gaussian ("a Gaussian blur with a standard deviation equal to half the
        /// blur radius"), so the bitmap is grown by three deviations (1.5 x the radius) on every side.
        /// </para>
        /// <para>
        /// The shadow is then knocked out under the element's own border box, as
        /// <see href="https://www.w3.org/TR/css-backgrounds-3/#shadow-shape">§7.1.1</see> requires ("the shadow is only
        /// drawn outside the border edge"): the concentric-fill approximation this replaces painted through a
        /// transparent box. The knock-out follows the box's own rounded corners.
        /// </para>
        /// </remarks>
        /// <returns>false, having painted nothing, when <paramref name="g"/> cannot rasterize - the caller then uses the vector approximation.</returns>
        private static bool TryPaintBlurredOutsetShadow(RGraphics g, CssBox box, RRect borderBox, RRect shadowRect,
            BorderRadii shadowRadii, double blur, RColor color)
        {
            var margin = 1.5 * blur;
            var bounds = Intersect(Inflate(shadowRect, margin), Inflate(g.GetClip(), margin));
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return true;

            using var scope = g.BeginRasterSurface(bounds);
            if (scope is null)
                return false;

            var rg = scope.Graphics;
            using (var brush = rg.GetSolidBrush(color))
            {
                if (shadowRadii.IsRounded)
                {
                    using var path = BuildLayerRoundRect(rg, shadowRect, shadowRadii, 0);
                    rg.DrawPath(brush, path);
                }
                else
                {
                    rg.DrawRectangle(brush, shadowRect.X, shadowRect.Y, shadowRect.Width, shadowRect.Height);
                }
            }

            var sigma = blur / 2;
            GaussianBlur.Apply(scope.Surface, sigma * scope.Surface.PixelsPerUnitX, sigma * scope.Surface.PixelsPerUnitY);

            if (box.IsRounded)
            {
                using var boxPath = BuildLayerRoundRect(rg, borderBox, ShadowCornerRadii(box, borderBox, spread: 0), 0);
                rg.Erase(boxPath);
            }
            else
            {
                rg.EraseRectangle(borderBox);
            }

            g.DrawRaster(scope.Surface);
            return true;
        }

        /// <summary>
        /// Paints a blurred <em>inner</em> <c>box-shadow</c> layer as a real Gaussian blur, in a bitmap: everything outside
        /// the offset, spread-adjusted padding box is shadow-coloured, blurred, and drawn under the padding-box clip the
        /// caller has already pushed.
        /// </summary>
        /// <remarks>
        /// The bitmap is grown past the padding box by three deviations because the blurred value at its edge depends on
        /// shadow-coloured pixels outside it. The lit hole keeps the padding edge's rounded corners
        /// (<paramref name="paddingRadii"/>, reduced by the spread), which the four-rectangle vector approximation cannot.
        /// </remarks>
        /// <returns>false, having painted nothing, when <paramref name="g"/> cannot rasterize.</returns>
        private static bool TryPaintBlurredInsetShadow(RGraphics g, RRect paddingBox, BorderRadii paddingRadii,
            RRect inner, double blur, double spread, RColor color)
        {
            var margin = 1.5 * blur;
            var bounds = Intersect(Inflate(paddingBox, margin), Inflate(g.GetClip(), margin));
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return true;

            using var scope = g.BeginRasterSurface(bounds);
            if (scope is null)
                return false;

            var rg = scope.Graphics;

            // Shadow-coloured region: a rectangle larger than the bitmap, with the lit hole punched out (even-odd).
            var everything = Inflate(bounds, margin + 1);
            var ppp = rg.PixelsPerPoint;
            using var ring = rg.GetGraphicsPath();
            ring.Start(everything.Left / ppp, everything.Top / ppp);
            ring.LineTo(everything.Right / ppp, everything.Top / ppp);
            ring.LineTo(everything.Right / ppp, everything.Bottom / ppp);
            ring.LineTo(everything.Left / ppp, everything.Bottom / ppp);
            ring.CloseFigure();

            if (inner.Width > 0 && inner.Height > 0)
            {
                var holeRadii = AdjustRadii(paddingRadii, -spread);
                if (holeRadii.IsRounded)
                {
                    using var hole = BuildLayerRoundRect(rg, inner, holeRadii, 0);
                    ring.AddPath(hole);
                }
                else
                {
                    ring.AddMove(inner.Left / ppp, inner.Top / ppp);
                    ring.LineTo(inner.Right / ppp, inner.Top / ppp);
                    ring.LineTo(inner.Right / ppp, inner.Bottom / ppp);
                    ring.LineTo(inner.Left / ppp, inner.Bottom / ppp);
                    ring.CloseFigure();
                }
            }

            ring.FillMode = RFillMode.EvenOdd;
            using (var brush = rg.GetSolidBrush(color))
                rg.DrawPath(brush, ring);

            var sigma = blur / 2;
            GaussianBlur.Apply(scope.Surface, sigma * scope.Surface.PixelsPerUnitX, sigma * scope.Surface.PixelsPerUnitY);

            g.DrawRaster(scope.Surface);
            return true;
        }
    }
}
