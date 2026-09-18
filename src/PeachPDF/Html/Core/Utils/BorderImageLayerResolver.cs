using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
using System;
using System.Globalization;
using System.Linq;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>One resolved <c>border-image-slice</c> value, in the source image's own pixel space.</summary>
    internal readonly record struct BorderImageSlice(double Top, double Right, double Bottom, double Left, bool Fill);

    /// <summary>One resolved 4-sided <c>border-image-width</c>/<c>border-image-outset</c> value, in border-box pixels.</summary>
    internal readonly record struct BorderImageSides(double Top, double Right, double Bottom, double Left);

    /// <summary>
    /// Resolves <c>border-image-slice</c>/<c>-width</c>/<c>-outset</c>/<c>-repeat</c> - all stored on
    /// <see cref="CssBox"/> as their raw declared strings, like <c>background-size</c>/<c>background-repeat</c>
    /// already are - into concrete geometry/keywords at paint time, against the border-image-source's own
    /// natural size and the box's real border widths/border-box. Grammar/tokenization for each is shared with
    /// the CSS-OM layer's own <c>BorderImage*Property.TheConverter</c> fields (used to validate the declared
    /// string when it's cascaded onto <see cref="CssBox"/> in the first place) - this class does only the
    /// pixel arithmetic that can't happen until paint time, per this repo's "don't write two independent
    /// parsers for the same CSS value grammar" convention.
    /// </summary>
    internal static class BorderImageLayerResolver
    {
        /// <summary>
        /// Resolves a <c>border-image-slice</c> value (CSS Backgrounds and Borders 3 §13:
        /// <c>&lt;number-percentage&gt;{1,4} &amp;&amp; fill?</c>) against the source image's own natural
        /// size. A bare number is a pixel count in the source image's own space; a percentage is relative to
        /// that same natural dimension. Per spec, "If two opposite border-image-slice offsets are larger
        /// than the corresponding dimension of the image ... they are proportionally reduced" - applied here
        /// the same way <see cref="ResolveWidth"/> reduces opposite <c>border-image-width</c> values against
        /// the border-image area.
        /// </summary>
        /// <param name="sliceValue">The declared <c>border-image-slice</c> string, as cascaded onto the box.</param>
        /// <param name="naturalWidth">The source image's natural width, the basis for the left/right slices.</param>
        /// <param name="naturalHeight">The source image's natural height, the basis for the top/bottom slices.</param>
        /// <param name="numberUnit">
        /// How much of <paramref name="naturalWidth"/>/<paramref name="naturalHeight"/> one bare
        /// <c>&lt;number&gt;</c> is worth: 1 for a raster source, whose natural size is already counted in
        /// the same device pixels the spec's number counts, and <c>Length.PointsPerPx</c> for a vector or
        /// generated source, whose natural size is in layout points while a number is still a vector
        /// coordinate / CSS pixel. Percentages are unaffected - they are relative either way.
        /// </param>
        internal static BorderImageSlice ResolveSlice(string sliceValue, double naturalWidth, double naturalHeight,
            double numberUnit = 1)
        {
            var components = CssValueParser.SplitTopLevelWhitespace(sliceValue).ToArray();

            var fill = components.Any(c => c.Equals(Keywords.Fill, StringComparison.OrdinalIgnoreCase));
            var numeric = components.Where(c => !c.Equals(Keywords.Fill, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (numeric.Length == 0) numeric = ["100%"];

            var (top, right, bottom, left) = ExpandFourSided(numeric);

            var sliceTop = ResolveSliceComponent(top, naturalHeight, numberUnit);
            var sliceRight = ResolveSliceComponent(right, naturalWidth, numberUnit);
            var sliceBottom = ResolveSliceComponent(bottom, naturalHeight, numberUnit);
            var sliceLeft = ResolveSliceComponent(left, naturalWidth, numberUnit);

            if (sliceTop + sliceBottom > naturalHeight && naturalHeight > 0)
            {
                var scale = naturalHeight / (sliceTop + sliceBottom);
                sliceTop *= scale;
                sliceBottom *= scale;
            }

            if (sliceLeft + sliceRight > naturalWidth && naturalWidth > 0)
            {
                var scale = naturalWidth / (sliceLeft + sliceRight);
                sliceLeft *= scale;
                sliceRight *= scale;
            }

            return new BorderImageSlice(sliceTop, sliceRight, sliceBottom, sliceLeft, fill);
        }

        /// <summary>
        /// Resolves a <c>border-image-width</c> value (CSS Backgrounds and Borders 3 §13:
        /// <c>[&lt;length-percentage&gt; | &lt;number&gt; | auto]{1,4}</c>) into border-image-area pixels.
        /// <c>auto</c> falls back to the corresponding side's real used border width; a bare number is a
        /// multiple of it; a percentage is relative to <paramref name="areaWidth"/>/<paramref name="areaHeight"/>
        /// (the border-image area itself, i.e. the border box already extended by <c>border-image-outset</c>);
        /// a length resolves absolutely via <see cref="CssValueParser.ParseLength(string,double,CssBox)"/>.
        /// Per spec, opposite values whose sum would exceed the area's own dimension are proportionally
        /// reduced, mirroring <see cref="ResolveSlice"/>'s identical reduction on the source side.
        /// </summary>
        internal static BorderImageSides ResolveWidth(string widthValue,
            double borderTop, double borderRight, double borderBottom, double borderLeft,
            double areaWidth, double areaHeight, CssBox box)
        {
            var components = CssValueParser.SplitTopLevelWhitespace(widthValue).ToArray();
            if (components.Length == 0) components = [Keywords.Auto];

            var (top, right, bottom, left) = ExpandFourSided(components);

            var widthTop = ResolveWidthComponent(top, borderTop, areaHeight, box);
            var widthRight = ResolveWidthComponent(right, borderRight, areaWidth, box);
            var widthBottom = ResolveWidthComponent(bottom, borderBottom, areaHeight, box);
            var widthLeft = ResolveWidthComponent(left, borderLeft, areaWidth, box);

            if (widthTop + widthBottom > areaHeight && areaHeight > 0)
            {
                var scale = areaHeight / (widthTop + widthBottom);
                widthTop *= scale;
                widthBottom *= scale;
            }

            if (widthLeft + widthRight > areaWidth && areaWidth > 0)
            {
                var scale = areaWidth / (widthLeft + widthRight);
                widthLeft *= scale;
                widthRight *= scale;
            }

            return new BorderImageSides(widthTop, widthRight, widthBottom, widthLeft);
        }

        /// <summary>
        /// Resolves a <c>border-image-outset</c> value (CSS Backgrounds and Borders 3 §13:
        /// <c>[&lt;length&gt; | &lt;number&gt;]{1,4}</c>) into pixels the border-image area extends past the
        /// border box on each side. A bare number is a multiple of the corresponding side's real used border
        /// width; a length resolves absolutely. The CSS-OM's own <c>BorderImageOutsetProperty</c> converter
        /// also accepts a percentage - broader than this spec grammar, a pre-existing, already-tested
        /// deviation this method does not silently "fix" (see the accepted-gap note) - resolved here as a
        /// percentage of the same side's real border width, for a bounded, non-crashing result.
        /// </summary>
        internal static BorderImageSides ResolveOutset(string outsetValue,
            double borderTop, double borderRight, double borderBottom, double borderLeft, CssBox box)
        {
            var components = CssValueParser.SplitTopLevelWhitespace(outsetValue).ToArray();
            if (components.Length == 0) components = ["0"];

            var (top, right, bottom, left) = ExpandFourSided(components);

            return new BorderImageSides(
                ResolveOutsetComponent(top, borderTop, box),
                ResolveOutsetComponent(right, borderRight, box),
                ResolveOutsetComponent(bottom, borderBottom, box),
                ResolveOutsetComponent(left, borderLeft, box));
        }

        /// <summary>
        /// Resolves a <c>border-image-repeat</c> value (CSS Backgrounds and Borders 3 §13:
        /// <c>[stretch | repeat | round | space]{1,2}</c>) into its horizontal (first component) and
        /// vertical (second, or the first again if only one was declared) keyword.
        /// </summary>
        internal static (BorderRepeat Horizontal, BorderRepeat Vertical) ResolveRepeat(string repeatValue)
        {
            var components = CssValueParser.SplitTopLevelWhitespace(repeatValue).ToArray();
            var horizontal = ParseRepeatKeyword(components.ElementAtOrDefault(0));
            var vertical = components.Length > 1 ? ParseRepeatKeyword(components[1]) : horizontal;
            return (horizontal, vertical);
        }

        /// <summary>
        /// Defensively degrades a component this repo's own <see cref="BorderImageRepeatProperty"/>
        /// converter should already have rejected (so <see cref="Map.BorderRepeatModes"/> never actually
        /// misses) to <see cref="BorderRepeat.Stretch"/>, rather than throwing, if one ever reaches here.
        /// </summary>
        private static BorderRepeat ParseRepeatKeyword(string? component) =>
            component is not null && Map.BorderRepeatModes.TryGetValue(component, out var mode)
                ? mode
                : BorderRepeat.Stretch;

        private static double ResolveSliceComponent(string component, double naturalBasis, double numberUnit)
        {
            if (component.EndsWith('%'))
            {
                return TryParseInvariant(component.AsSpan(0, component.Length - 1), out var pct)
                    ? Math.Max(0, pct / 100.0 * naturalBasis)
                    : naturalBasis;
            }

            return TryParseInvariant(component, out var number) ? Math.Max(0, number * numberUnit) : 0;
        }

        private static double ResolveWidthComponent(string component, double usedBorderWidth, double areaBasis, CssBox box)
        {
            if (component.Equals(Keywords.Auto, StringComparison.OrdinalIgnoreCase))
                return usedBorderWidth;

            if (component.EndsWith('%'))
            {
                return TryParseInvariant(component.AsSpan(0, component.Length - 1), out var pct)
                    ? Math.Max(0, pct / 100.0 * areaBasis)
                    : 0;
            }

            if (TryParseInvariant(component, out var multiplier))
                return Math.Max(0, multiplier * usedBorderWidth);

            return Math.Max(0, CssValueParser.ParseLength(component, areaBasis, box));
        }

        private static double ResolveOutsetComponent(string component, double usedBorderWidth, CssBox box)
        {
            if (component.EndsWith('%'))
            {
                return TryParseInvariant(component.AsSpan(0, component.Length - 1), out var pct)
                    ? Math.Max(0, pct / 100.0 * usedBorderWidth)
                    : 0;
            }

            if (TryParseInvariant(component, out var multiplier))
                return Math.Max(0, multiplier * usedBorderWidth);

            return Math.Max(0, CssValueParser.ParseLength(component, usedBorderWidth, box));
        }

        private static bool TryParseInvariant(ReadOnlySpan<char> text, out double value) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        /// <summary>
        /// Expands a 1-4 component list into (top, right, bottom, left) per the standard CSS box-model
        /// cycling rule shared by <c>margin</c>/<c>padding</c>/<c>border-width</c>/etc., and by the CSS-OM's
        /// own <c>Periodic()</c> combinator these properties' real converters already use - this is the same
        /// expansion, re-derived here only because the value is stored as a raw string rather than four
        /// already-split property values.
        /// </summary>
        private static (string Top, string Right, string Bottom, string Left) ExpandFourSided(string[] components) =>
            components.Length switch
            {
                1 => (components[0], components[0], components[0], components[0]),
                2 => (components[0], components[1], components[0], components[1]),
                3 => (components[0], components[1], components[2], components[1]),
                _ => (components[0], components[1], components[2], components[3])
            };
    }
}
