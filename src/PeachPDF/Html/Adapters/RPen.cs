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
    }
}