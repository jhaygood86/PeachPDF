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
    /// Represents a word inside an inline box for an SVG element - the replaced-element counterpart of
    /// <see cref="CssRectImage"/>, sized from an <c>SvgDocument</c>'s intrinsic size instead of a
    /// raster <see cref="Adapters.RImage"/>'s pixel size.
    /// </summary>
    internal sealed class CssRectSvg : CssRect
    {
        /// <summary>
        /// Creates a new BoxWord which represents an SVG element
        /// </summary>
        /// <param name="owner">the CSS box owner of the word</param>
        public CssRectSvg(CssBox owner)
            : base(owner)
        { }

        /// <summary>
        /// Gets if the word represents an image (SVG is treated as an atomic replaced element for
        /// inline flow/line-breaking/min-max-width purposes, same as a raster image).
        /// </summary>
        public override bool IsImage => true;

        /// <summary>
        /// See <see cref="CssRectImage.IsSpaces"/>'s identical override/doc comment - an inline SVG is
        /// never "just spaces" either.
        /// </summary>
        public override bool IsSpaces => false;

        /// <summary>
        /// Represents this word for debugging purposes
        /// </summary>
        public override string ToString()
        {
            return "Svg";
        }
    }
}
