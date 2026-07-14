// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
// 
// - Sun Tsu,
// "The Art of War"

using PeachPDF.Html.Adapters.Entities;

namespace PeachPDF.Html.Adapters
{
    /// <summary>
    /// Adapter for platform specific pen objects - used to draw graphics (lines, rectangles and paths) 
    /// </summary>
    internal abstract class RPen
    {
        /// <summary>
        /// Gets or sets the width of this Pen, in units of the Graphics object used for drawing.
        /// </summary>
        public abstract double Width { get; set; }

        /// <summary>
        /// Gets or sets the style used for dashed lines drawn with this Pen.
        /// </summary>
        public abstract RDashStyle DashStyle { set; }

        /// <summary>
        /// Gets or sets the miter limit used when joining sharp corners of a stroked path.
        /// </summary>
        public abstract double MiterLimit { get; set; }

        /// <summary>
        /// Sets how the ends of an unclosed subpath are drawn.
        /// </summary>
        public abstract RLineCap LineCap { set; }

        /// <summary>
        /// Sets how consecutive stroked segments are joined.
        /// </summary>
        public abstract RLineJoin LineJoin { set; }

        /// <summary>
        /// Sets an explicit numeric dash pattern (e.g. from SVG's <c>stroke-dasharray</c>/
        /// <c>stroke-dashoffset</c>), overriding <see cref="DashStyle"/> with a custom pattern.
        /// <paramref name="pattern"/> and <paramref name="offset"/> are in the same absolute units as
        /// <see cref="Width"/> - implementations are responsible for any unit-normalization their
        /// underlying pen API requires (e.g. a GDI+-style dash array expressed as multiples of pen
        /// width rather than absolute lengths). An empty <paramref name="pattern"/> reverts to a solid
        /// line.
        /// </summary>
        public abstract void SetDashPattern(double[] pattern, double offset);
    }
}