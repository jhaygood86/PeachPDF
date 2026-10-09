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
    /// Resolves an <see cref="SvgDocument"/>'s own natural size (independent of any CSS box sizing),
    /// for feeding into <c>CssLayoutEngine.MeasureIntrinsicSize</c> - shared by
    /// <c>CssBoxSvg.MeasureWordsSize</c> (inline <c>&lt;svg&gt;</c>) and
    /// <c>CssBoxImage.MeasureWordsSize</c> (<c>&lt;img src="x.svg"&gt;</c>).
    /// </summary>
    internal static class SvgIntrinsicSize
    {
        public static (double? Width, double? Height) Resolve(SvgDocument? document)
        {
            if (document is null)
                return (null, null);

            var width = document.Width ?? document.ViewBox?.Width;
            var height = document.Height ?? document.ViewBox?.Height;

            return (width, height);
        }

        /// <summary>
        /// Whether <paramref name="document"/> has a natural size in the given axis, as opposed to only a
        /// preferred aspect ratio: its own <c>width</c>/<c>height</c> attribute, or the other one plus a
        /// <c>viewBox</c> to derive it from. A <c>viewBox</c>-only document has a ratio but no natural size
        /// (<see cref="Resolve"/> still sizes it from the viewBox so it has something to lay out at), which
        /// is what a grid needs to know before letting it keep that size under <c>justify-self: normal</c>.
        /// </summary>
        public static bool HasNaturalSize(SvgDocument? document, bool horizontal)
        {
            if (document is null)
                return false;

            var own = horizontal ? document.Width : document.Height;
            var other = horizontal ? document.Height : document.Width;

            return own is > 0 || (other is > 0 && document.ViewBox is { Width: > 0, Height: > 0 });
        }
    }
}
