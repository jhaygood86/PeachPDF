using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Svg;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// Paints a replaced element's content (a raster <see cref="RImage"/> or an <see cref="SvgDocument"/>)
    /// into its content box, honoring <c>object-fit</c> / <c>object-position</c>. Shared by every replaced
    /// box - <see cref="CssBoxImage"/>, <see cref="CssBoxObject"/>, <see cref="CssBoxSvg"/>,
    /// <see cref="CssBoxVideo"/> - so the fit/position logic lives in exactly one place.
    /// </summary>
    internal static class ReplacedContentRenderer
    {
        /// <summary>
        /// Draws <paramref name="image"/> (or <paramref name="svg"/>, whichever is non-null) into
        /// <paramref name="contentBox"/> using <paramref name="box"/>'s <c>object-fit</c>/<c>object-position</c>.
        /// <c>fill</c> (and the no-known-intrinsic-size case) draws to the content box unchanged; a fit
        /// that overflows (e.g. <c>cover</c>) is clipped to the content box.
        /// </summary>
        public static void Paint(RGraphics g, RRect contentBox, RImage? image, SvgDocument? svg, CssBoxProperties box)
        {
            if (contentBox is not { Width: > 0, Height: > 0 } || (image is null && svg is null))
                return;

            double naturalWidth = 0, naturalHeight = 0;
            if (svg is not null)
            {
                var (svgWidth, svgHeight) = SvgIntrinsicSize.Resolve(svg);
                naturalWidth = svgWidth ?? 0;
                naturalHeight = svgHeight ?? 0;
            }
            else if (image is not null)
            {
                naturalWidth = image.Width;
                naturalHeight = image.Height;
            }

            // Intrinsic pixels -> layout points, to match contentBox.
            naturalWidth *= CSS.Length.PointsPerPx;
            naturalHeight *= CSS.Length.PointsPerPx;

            var (destination, needsClip) = ObjectFitResolver.Compute(
                contentBox, naturalWidth, naturalHeight, box.ObjectFit, box.ObjectPosition, box);

            if (needsClip)
                g.PushClip(contentBox);

            if (svg is not null)
                SvgRenderer.RenderInto(g, svg, destination);
            else if (image is not null)
                g.DrawImage(image, destination);

            if (needsClip)
                g.PopClip();
        }
    }
}
