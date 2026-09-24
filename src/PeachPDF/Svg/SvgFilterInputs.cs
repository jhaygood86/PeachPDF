using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;

namespace PeachPDF.Svg
{
    /// <summary>
    /// What a raster filter evaluation needs from the renderer beyond the element's own <c>SourceGraphic</c>: the inputs that are painted
    /// rather than computed - <c>FillPaint</c>/<c>StrokePaint</c>, the pixels behind the element (<c>BackgroundImage</c>) and
    /// <c>feImage</c>'s content. One instance serves one filter evaluation; the evaluator asks for each input at most once.
    /// </summary>
    internal abstract class SvgFilterInputs
    {
        /// <summary>The filtered element's <c>fill</c> (or <c>stroke</c>) paint, which <c>FillPaint</c> (<c>StrokePaint</c>) fills the filter region with.</summary>
        public abstract SvgPaint PaintOf(bool stroke);

        /// <summary>Paints <see cref="PaintOf"/> - a gradient or pattern - over <paramref name="region"/> (user space) into <paramref name="g"/>.</summary>
        public abstract void PaintPaint(RGraphics g, bool stroke, RRect region);

        /// <summary>
        /// Paints an <c>feImage</c>'s content into <paramref name="g"/>: the image fitted into <paramref name="subregion"/>, or the referenced
        /// element in the filtered element's user space translated by (<paramref name="offsetX"/>, <paramref name="offsetY"/>).
        /// </summary>
        public abstract void PaintImage(RGraphics g, FeImage image, RRect subregion, double offsetX, double offsetY);

        /// <summary>Paints what lies behind the filtered element inside <paramref name="region"/> into <paramref name="g"/>; false when there is nothing to paint (the input is then transparent).</summary>
        public abstract bool PaintBackdrop(RGraphics g, RRect region);
    }
}
