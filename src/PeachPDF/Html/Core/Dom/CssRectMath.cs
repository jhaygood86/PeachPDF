namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// Represents a word inside an inline box for a <c>&lt;math&gt;</c> element - the replaced-element
    /// counterpart of <see cref="CssRectImage"/>/<see cref="CssRectSvg"/>, sized from a laid-out
    /// <c>MathML.MathBox</c>'s computed inline size/ascent/descent instead of a raster image's pixel
    /// size or an SVG document's intrinsic viewBox.
    /// </summary>
    internal sealed class CssRectMath : CssRect
    {
        public CssRectMath(CssBox owner)
            : base(owner)
        { }

        /// <summary>
        /// Gets if the word represents an image (a <c>&lt;math&gt;</c> formula is treated as an atomic
        /// replaced element for inline flow/line-breaking/min-max-width purposes, same as a raster
        /// image or inline SVG).
        /// </summary>
        public override bool IsImage => true;

        /// <summary>
        /// See <see cref="CssRectImage.IsSpaces"/>'s identical override/doc comment - a math formula is
        /// never "just spaces" either.
        /// </summary>
        public override bool IsSpaces => false;

        public override string ToString()
        {
            return "Math";
        }
    }
}
