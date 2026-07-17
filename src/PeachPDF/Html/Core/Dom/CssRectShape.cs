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

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// Represents a word inside an inline box for a vector-drawn list-item marker shape
    /// (disc/circle/square) - the replaced-element counterpart of <see cref="CssRectImage"/>/
    /// <see cref="CssRectSvg"/>, but with no image/document backing it at all: it exists purely to
    /// claim space in inline flow/line-breaking, sized by <see cref="CssBoxMarker"/>'s own
    /// measurement. The actual shape is vector-drawn directly by <see cref="CssBoxMarker"/>'s own
    /// paint logic against its resolved <c>Bounds</c>, not through this word's <c>Rectangle</c>.
    /// </summary>
    internal sealed class CssRectShape : CssRect
    {
        /// <summary>
        /// Creates a new BoxWord which represents a vector-drawn marker shape
        /// </summary>
        /// <param name="owner">the CSS box owner of the word</param>
        public CssRectShape(CssBox owner)
            : base(owner)
        { }

        /// <summary>
        /// Gets if the word represents an image (a marker shape is treated as an atomic replaced
        /// element for inline flow/line-breaking purposes, same as a raster image - and, via
        /// <see cref="CssBox.PaintWords"/>'s existing <c>IsImage</c> skip, is never drawn by the
        /// generic per-word text-painting loop).
        /// </summary>
        public override bool IsImage => true;

        /// <summary>
        /// Represents this word for debugging purposes
        /// </summary>
        public override string ToString()
        {
            return "Shape";
        }
    }
}
