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

using PeachBrackets.Html.Dom;

namespace PeachBrackets.Svg
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
        /// <see cref="Resolve"/> for sizing a replaced element's box: a document with a size in one axis only
        /// (a <c>width</c> and no <c>height</c>, or the reverse, and no <c>viewBox</c> to derive it from) takes
        /// the default object size for the other axis, 300x150 CSS px, as CSS Images 3 §5.1 gives an object with
        /// no size of its own. <c>HasRatio</c> is false for that case: nothing links its axes, so a box sized
        /// along one must leave the other at its default rather than scale it.
        /// </summary>
        public static (double? Width, double? Height, bool HasRatio) ResolveForBox(SvgDocument? document)
        {
            if (document is null)
                return (null, null, false);

            // Only what the document itself states is a size: its width and height attributes. A viewBox is a coordinate system and an aspect ratio,
            // not a size, so a document with a viewBox and no width or height has no natural size at all (CSS Images 3 section 5.1, SVG 2 section 8.1; the
            // box then takes the available width, see MeasureIntrinsicSize's ratioOnly) - taking the viewBox's own extent for one laid a 1792-unit
            // wide icon out 1792 px wide in a 20px link.
            double? width = document.Width is > 0 ? document.Width : null;
            double? height = document.Height is > 0 ? document.Height : null;
            var viewBoxRatio = ViewBoxRatio(document);

            // Neither: a ratio alone takes the available width (see RatioOnly); with no ratio either it is the default object size, 300x150 CSS px.
            if (width is null && height is null)
                return viewBoxRatio is not null
                    ? (null, null, true)
                    : (CssLayoutEngine.DefaultObjectWidth, CssLayoutEngine.DefaultObjectHeight, false);

            // A size in one axis: the other follows the viewBox's ratio when there is one (width="80" with a 2:1 viewBox is 80x40), and is otherwise
            // the default object size.
            if (height is null)
                height = viewBoxRatio is { } r ? width / r : CssLayoutEngine.DefaultObjectHeight;
            else if (width is null)
                width = viewBoxRatio is { } q ? height * q : CssLayoutEngine.DefaultObjectWidth;

            var hasRatio = document.Width is > 0 && document.Height is > 0 || viewBoxRatio is not null;
            return (width, height, hasRatio);
        }

        /// <summary>
        /// The natural size and aspect ratio of <paramref name="document"/> as a <i>background image</i> sees them (CSS Images 3 section 5.3): only what the
        /// document states is a size, and an axis it does not state is left null, because the default object size of a background is its positioning area,
        /// not the 300x150 a replaced box falls back to (<see cref="ResolveForBox"/>). The ratio comes from <c>width</c> and <c>height</c> when both are
        /// given, otherwise from the <c>viewBox</c>, which also derives the missing axis from the one that is stated.
        /// </summary>
        public static (double? Width, double? Height, double? Ratio) ResolveForBackground(SvgDocument? document)
        {
            if (document is null)
                return (null, null, null);

            double? width = document.Width is > 0 ? document.Width : null;
            double? height = document.Height is > 0 ? document.Height : null;
            var viewBoxRatio = ViewBoxRatio(document);

            if (width is not null && height is not null)
                return (width, height, width / height);

            if (width is not null)
                return (width, viewBoxRatio is { } r ? width / r : null, viewBoxRatio);

            if (height is not null)
                return (viewBoxRatio is { } q ? height * q : null, height, viewBoxRatio);

            return (null, null, viewBoxRatio);
        }

        /// <summary>
        /// The aspect ratio (width over height) of a document that has one but no size of its own: a <c>viewBox</c> and neither <c>width</c> nor
        /// <c>height</c>. Null for any other document. Such a document sizes like a block-level replaced element with a ratio and no natural size
        /// (CSS 2.1 section 10.3.2): it takes the width its containing block has available, and its height follows the ratio.
        /// </summary>
        public static double? RatioOnly(SvgDocument? document) =>
            document is { Width: not > 0, Height: not > 0 } ? ViewBoxRatio(document) : null;

        private static double? ViewBoxRatio(SvgDocument document) =>
            document.ViewBox is { Width: > 0, Height: > 0 } viewBox ? viewBox.Width / viewBox.Height : null;

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

            // A size in either axis gives the other the default object size (see ResolveForBox).
            return document.Width is > 0 || document.Height is > 0;
        }
    }
}
