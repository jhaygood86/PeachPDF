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

namespace PeachPDF.Svg
{
    /// <summary>
    /// The alignment component of a parsed <c>preserveAspectRatio</c> value - which edge/center of the
    /// viewBox is aligned to the corresponding edge/center of the viewport. <see cref="None"/> means
    /// "stretch to fill, ignoring aspect ratio" (independent x/y scale).
    /// </summary>
    internal enum SvgAlign
    {
        None,
        XMinYMin,
        XMidYMin,
        XMaxYMin,
        XMinYMid,
        XMidYMid,
        XMaxYMid,
        XMinYMax,
        XMidYMax,
        XMaxYMax,
    }

    /// <summary>A parsed <c>preserveAspectRatio</c> value. The "defer" keyword is not tracked - it only matters for a referenced/embedded external SVG resource's own opinion, which this renderer always defers to anyway.</summary>
    internal readonly record struct SvgPreserveAspectRatio(SvgAlign Align, bool Slice)
    {
        /// <summary>The SVG/CSS default: <c>xMidYMid meet</c>.</summary>
        public static readonly SvgPreserveAspectRatio Default = new(SvgAlign.XMidYMid, false);
    }
}
