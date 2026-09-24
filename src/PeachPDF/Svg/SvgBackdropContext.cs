using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;

namespace PeachPDF.Svg
{
    /// <summary>Paints the page content lying behind an inline SVG, for the SVG's <c>BackgroundImage</c>.</summary>
    internal interface ISvgPageBackdrop
    {
        /// <summary>
        /// Paints what was on the page before the SVG's own content into <paramref name="g"/>, a raster graphics whose user space is the
        /// page's layout space. False when there is nothing to paint (the page layer is then simply absent).
        /// </summary>
        bool Paint(RGraphics g);
    }

    /// <summary>
    /// What an SVG whose filters read <c>BackgroundImage</c> needs while it is painted, carried on the <see cref="RGraphics"/> it paints
    /// to (<see cref="RGraphics.SvgBackdrop"/>). The backdrop of a filtered element is everything painted before it inside the current
    /// isolation group: the page behind an inline SVG, then the SVG's own earlier content. Neither is kept anywhere while painting, so
    /// both are repainted on demand into a bitmap - the SVG's by walking its scene graph again from the root and stopping at the element.
    /// </summary>
    /// <remarks>
    /// Only the graphics that paints the document's root elements carries one; a group's isolated tile does not, which is exactly right,
    /// because an isolation group's backdrop starts empty.
    /// </remarks>
    internal sealed class SvgBackdropContext(SvgDocument document, ISvgPageBackdrop? page)
    {
        public SvgDocument Document { get; } = document;

        /// <summary>The page layer, or null for an SVG that is not inline in an HTML page.</summary>
        public ISvgPageBackdrop? Page { get; } = page;

        /// <summary>The transform in effect where the document is placed, before its viewBox mapping.</summary>
        public RMatrix Frame { get; set; }

        /// <summary>The rectangle, in <see cref="Frame"/>'s space, the document is clipped to.</summary>
        public RRect ViewportRect { get; set; }

        /// <summary>The viewBox-to-viewport mapping the document's root elements are painted under.</summary>
        public RMatrix ViewBoxMatrix { get; set; }

        /// <summary>The user-space size root elements resolve percentages against.</summary>
        public (double Width, double Height) Viewport { get; set; }

        /// <summary>How many backdrop repaints enclose this one; bounds a chain of filters that read each other's backdrop.</summary>
        public int Depth { get; init; }

        /// <summary>Set on a repaint: the element at which painting stops, because it and everything after it is not backdrop.</summary>
        public SvgElement? StopElement { get; init; }

        /// <summary>True once <see cref="StopElement"/> was reached; from then on nothing more is painted.</summary>
        public bool Stopped { get; private set; }

        /// <summary>Whether <paramref name="element"/> must not be painted by this repaint (it is the stop element, or comes after it).</summary>
        public bool ShouldSkip(SvgElement element)
        {
            if (Stopped)
                return true;

            if (StopElement is null || !ReferenceEquals(element, StopElement))
                return false;

            Stopped = true;
            return true;
        }
    }
}
