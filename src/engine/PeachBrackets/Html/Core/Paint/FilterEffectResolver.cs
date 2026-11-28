using PeachBrackets.Css;
using PeachDrawing.Core;
using PeachBrackets.Html.Parse;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace PeachBrackets.Html.Paint
{
    /// <summary>
    /// Resolves a box's parsed <c>filter</c> function list (<see cref="FilterGrammar.FilterFunction"/>,
    /// from <c>CssBox.ActualFilterFunctions</c>) into the two paint-time effects PDF can actually express -
    /// an opacity multiplier and a single composed <see cref="ColorMatrix"/> - per the Filter Effects Level
    /// 1 §3 per-function equivalences. <c>drop-shadow()</c> is deliberately excluded here: it adds shadow
    /// geometry rather than recoloring the box's own content, so it is resolved and painted separately by
    /// <c>FragmentPainter.PaintFilterDropShadows</c> at the point box-shadow's own outset layers paint.
    /// </summary>
    internal static class FilterEffectResolver
    {
        /// <summary>
        /// <see cref="OpacityMultiplier"/> composes multiplicatively with the box's own CSS <c>opacity</c>
        /// (Filter Effects 1 §3.1's <c>opacity()</c> is defined as an ordinary alpha-scaling step, and the
        /// <c>opacity</c> property is a separate, later alpha-scaling step in the same rendering model - CSS
        /// Positioned Layout 3's stacking-context processing runs filter, then clip-path, then mask, then
        /// opacity - so two sequential alpha scales are just two factors of one product, not a case where
        /// one must be preferred over the other). <see cref="HasColorMatrix"/> is false (and
        /// <see cref="ColorMatrix"/> is <see cref="ColorMatrix.Identity"/>) whenever the filter list has no
        /// channel-independent function at all, so a caller can skip the extra tile/composite entirely.
        /// </summary>
        internal readonly struct Resolved
        {
            public double OpacityMultiplier { get; init; }
            public ColorMatrix ColorMatrix { get; init; }
            public bool HasColorMatrix { get; init; }

            /// <summary>
            /// True when the list holds a function PDF cannot express as vector content - <c>blur()</c> or one of the
            /// cross-channel colour functions (<c>grayscale()</c>, <c>sepia()</c>, <c>saturate()</c>,
            /// <c>hue-rotate()</c>) that actually changes something. The painter then renders the element into a
            /// bitmap and applies <see cref="Functions"/> to it in order, instead of using
            /// <see cref="OpacityMultiplier"/>/<see cref="ColorMatrix"/>, which only carry the vector-expressible part.
            /// </summary>
            public bool RequiresRaster { get; init; }

            /// <summary>The whole ordered function list, for the raster path (order matters: <c>blur() grayscale()</c> is not <c>grayscale() blur()</c> under clamping).</summary>
            public IReadOnlyList<FilterGrammar.FilterFunction> Functions { get; init; }
        }

        internal static Resolved Resolve(IReadOnlyList<FilterGrammar.FilterFunction> functions)
        {
            var opacityMultiplier = 1.0;
            var matrix = ColorMatrix.Identity;
            var hasMatrix = false;
            var requiresRaster = false;

            foreach (var function in functions)
            {
                switch (function.Name)
                {
                    case "opacity":
                        // Clamped to [0, 1] like the `opacity` property itself (Filter Effects 1 §3.1) -
                        // grammar already rejected negative values, this only clamps the >1 case.
                        opacityMultiplier *= Math.Clamp(Amount(function), 0.0, 1.0);
                        break;

                    case "brightness":
                        matrix = matrix.Compose(BrightnessMatrix(Amount(function)));
                        hasMatrix = true;
                        break;

                    case "contrast":
                        matrix = matrix.Compose(ContrastMatrix(Amount(function)));
                        hasMatrix = true;
                        break;

                    case "invert":
                        matrix = matrix.Compose(InvertMatrix(Math.Clamp(Amount(function), 0.0, 1.0)));
                        hasMatrix = true;
                        break;

                    // blur()/grayscale()/hue-rotate()/saturate()/sepia(): no native PDF mechanism performs a blur or
                    // a cross-channel colour mix over already-composited content, so the vector path leaves them out
                    // and the painter renders the element to a bitmap instead (RequiresRaster) when one of them
                    // actually does something, and so is drop-shadow(). A graphics that cannot rasterize paints drop-shadow() with the border-box approximation in FragmentPainter.PaintFilterDropShadows.
                    case "blur":
                        requiresRaster |= !IsZeroLength(function);
                        break;

                    case "grayscale":
                    case "sepia":
                    case "saturate":
                    case "hue-rotate":
                        requiresRaster |= !IsIdentity(TryGetMatrix(function));
                        break;

                    // drop-shadow() follows the real alpha shape of what was painted (glyphs, a PNG's transparent
                    // corners), which only pixels can give; the vector fallback shadows the border box instead.
                    case "drop-shadow":
                        requiresRaster = true;
                        break;

                    // A reference to an SVG filter runs over pixels (the document may have no such filter, which RasterFilterChain finds out).
                    case FilterGrammar.Reference:
                        requiresRaster = true;
                        break;
                }
            }

            return new Resolved
            {
                OpacityMultiplier = opacityMultiplier,
                ColorMatrix = matrix,
                HasColorMatrix = hasMatrix,
                RequiresRaster = requiresRaster,
                Functions = functions,
            };
        }

        /// <summary>True for <c>blur()</c> with no argument or a zero-length one (a no-op).</summary>
        internal static bool IsZeroLength(FilterGrammar.FilterFunction function)
        {
            if (function.Arguments.Count == 0)
                return true;

            var text = function.Arguments[0].AsSpan().Trim();
            if (text.Length > 0 && text[0] is '+' or '-')
                text = text[1..];

            var end = 0;
            while (end < text.Length && (char.IsAsciiDigit(text[end]) || text[end] == '.'))
                end++;

            return end > 0 && double.TryParse(text[..end], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value) && value == 0;
        }

        private static bool IsIdentity(ColorMatrix? matrix) =>
            matrix is null || (matrix.Value.Linear == Matrix4x4.Identity && matrix.Value.Offset == Vector4.Zero);

        /// <summary>
        /// The colour matrix a matrix-style filter function stands for, per Filter Effects 1 §3, or null for a
        /// function that is not one (<c>blur()</c>, <c>drop-shadow()</c>, <c>opacity()</c>). Every function that has
        /// one is included, not just the cross-channel ones, so the raster path can run a whole list through one
        /// routine in the authored order.
        /// </summary>
        internal static ColorMatrix? TryGetMatrix(FilterGrammar.FilterFunction function) => function.Name switch
        {
            "brightness" => BrightnessMatrix(Amount(function)),
            "contrast" => ContrastMatrix(Amount(function)),
            "invert" => InvertMatrix(Math.Clamp(Amount(function), 0.0, 1.0)),
            "grayscale" => GrayscaleMatrix(Math.Clamp(Amount(function), 0.0, 1.0)),
            "sepia" => SepiaMatrix(Math.Clamp(Amount(function), 0.0, 1.0)),
            "saturate" => SaturateMatrix(Amount(function)),
            "hue-rotate" => HueRotateMatrix(HueRotateRadians(function)),
            _ => null,
        };

        /// <summary>A matrix that multiplies alpha by <paramref name="amount"/> (Filter Effects 1 §3.1, <c>opacity()</c>).</summary>
        internal static ColorMatrix OpacityMatrix(double amount) => ColorMatrix.Opacity(amount);

        private static double HueRotateRadians(FilterGrammar.FilterFunction function)
        {
            if (function.Arguments.Count == 0)
                return 0;

            return Angle.TryParse(function.Arguments[0], out var angle) ? angle.ToRadian() : 0;
        }

        private static ColorMatrix GrayscaleMatrix(double amount) => ColorMatrix.Grayscale(amount);

        private static ColorMatrix SepiaMatrix(double amount) => ColorMatrix.Sepia(amount);

        internal static ColorMatrix SaturateMatrix(double amount) => ColorMatrix.Saturate(amount);

        internal static ColorMatrix HueRotateMatrix(double radians) => ColorMatrix.HueRotate(radians);

        /// <summary>A single-argument filter function's amount, defaulting to 1 (100%) - Filter Effects 1
        /// §3's own default for every function in this switch - when the author omitted the argument.</summary>
        internal static double Amount(FilterGrammar.FilterFunction function) =>
            function.Arguments.Count == 0 ? 1.0 : CssValueParser.ParseNumber(function.Arguments[0], 1.0);

        internal static ColorMatrix BrightnessMatrix(double amount) => ColorMatrix.Brightness(amount);

        internal static ColorMatrix ContrastMatrix(double amount) => ColorMatrix.Contrast(amount);

        internal static ColorMatrix InvertMatrix(double amount) => ColorMatrix.Invert(amount);
    }
}
