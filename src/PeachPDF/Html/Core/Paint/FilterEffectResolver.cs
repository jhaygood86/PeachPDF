using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Parse;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace PeachPDF.Html.Core.Paint
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
        }

        internal static Resolved Resolve(IReadOnlyList<FilterGrammar.FilterFunction> functions)
        {
            var opacityMultiplier = 1.0;
            var matrix = ColorMatrix.Identity;
            var hasMatrix = false;

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

                    // blur()/grayscale()/hue-rotate()/saturate()/sepia(): documented no-ops (ColorMatrix's
                    // remarks) - no native PDF mechanism performs a cross-channel color mix over
                    // already-composited content, so these contribute nothing rather than being
                    // approximated. drop-shadow() is handled by FragmentPainter.PaintFilterDropShadows.
                }
            }

            return new Resolved { OpacityMultiplier = opacityMultiplier, ColorMatrix = matrix, HasColorMatrix = hasMatrix };
        }

        /// <summary>A single-argument filter function's amount, defaulting to 1 (100%) - Filter Effects 1
        /// §3's own default for every function in this switch - when the author omitted the argument.</summary>
        private static double Amount(FilterGrammar.FilterFunction function) =>
            function.Arguments.Count == 0 ? 1.0 : CssValueParser.ParseNumber(function.Arguments[0], 1.0);

        /// <summary>Filter Effects 1 §3.2: equivalent to <c>feComponentTransfer type="linear"</c> with
        /// <c>slope = amount</c>, <c>intercept = 0</c>, applied to each of R/G/B (never A).</summary>
        private static ColorMatrix BrightnessMatrix(double amount)
        {
            var a = (float)amount;
            return new ColorMatrix(Matrix4x4.CreateScale(a, a, a), Vector4.Zero);
        }

        /// <summary>Filter Effects 1 §3.3: equivalent to <c>feComponentTransfer type="linear"</c> with
        /// <c>slope = amount</c>, <c>intercept = -(0.5 * amount) + 0.5</c>, applied to each of R/G/B.</summary>
        private static ColorMatrix ContrastMatrix(double amount)
        {
            var a = (float)amount;
            var intercept = 0.5f - 0.5f * a;
            return new ColorMatrix(Matrix4x4.CreateScale(a, a, a), new Vector4(intercept, intercept, intercept, 0f));
        }

        /// <summary>Filter Effects 1 §3.7: equivalent to <c>feComponentTransfer type="table"</c> with
        /// <c>tableValues = "amount 1-amount"</c>, which is the linear function <c>slope = 1 - 2*amount</c>,
        /// <c>intercept = amount</c>, applied to each of R/G/B.</summary>
        private static ColorMatrix InvertMatrix(double amount)
        {
            var a = (float)amount;
            var slope = 1f - 2f * a;
            return new ColorMatrix(Matrix4x4.CreateScale(slope, slope, slope), new Vector4(a, a, a, 0f));
        }
    }
}
