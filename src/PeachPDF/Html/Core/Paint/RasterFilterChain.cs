using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Raster;
using PeachPDF.Raster.Filters;
using System;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Paint
{
    /// <summary>
    /// Runs a CSS <c>filter</c> function list over a rendered <see cref="RasterSurface"/>, in authored order
    /// (Filter Effects 1 §3): the path taken when the list holds something PDF cannot express as vector content -
    /// see <see cref="FilterEffectResolver.Resolved.RequiresRaster"/>.
    /// </summary>
    internal static class RasterFilterChain
    {
        /// <summary>
        /// Applies <paramref name="functions"/> to <paramref name="surface"/> and then the element's own
        /// <paramref name="groupOpacity"/> (CSS <c>opacity</c> runs after <c>filter</c>). <c>drop-shadow()</c> is skipped:
        /// it is painted with the element's other decorations (<c>FragmentPainter.PaintFilterDropShadows</c>).
        /// </summary>
        /// <remarks>
        /// Consecutive colour functions are composed into one matrix and applied in a single pass; a <c>blur()</c>
        /// flushes the pending matrix first so the order the author wrote is the order it runs in.
        /// </remarks>
        public static void Apply(RasterSurface surface, IReadOnlyList<FilterGrammar.FilterFunction> functions, CssBox box, double groupOpacity)
        {
            // Consecutive colour functions run as one pass over the pixels, each clamped before the next (Filter Effects 1);
            // a blur, or a drop-shadow, in between flushes the pending ones so the written order is the order they run in.
            var pending = new List<ColorMatrix>();

            void Queue(ColorMatrix next) => pending.Add(next);

            void Flush()
            {
                ColorMatrixFilter.ApplyInPlace(surface, pending);
                pending.Clear();
            }

            foreach (var function in functions)
            {
                switch (function.Name)
                {
                    case "blur":
                        if (FilterEffectResolver.IsZeroLength(function))
                            break;

                        Flush();
                        var sigma = CssValueParser.ParseLength(function.Arguments[0], 0, box);
                        GaussianBlur.Apply(surface, sigma * surface.PixelsPerUnitX, sigma * surface.PixelsPerUnitY);
                        break;

                    case "opacity":
                        Queue(FilterEffectResolver.OpacityMatrix(FilterEffectResolver.Amount(function)));
                        break;

                    case "drop-shadow":
                        Flush();
                        ApplyDropShadow(surface, function, box);
                        break;

                    default:
                        if (FilterEffectResolver.TryGetMatrix(function) is { } matrix)
                            Queue(matrix);

                        break;
                }
            }

            if (groupOpacity < 1.0)
                Queue(FilterEffectResolver.OpacityMatrix(groupOpacity));

            Flush();
        }

        /// <summary>
        /// <c>drop-shadow(x y std-deviation color)</c> (Filter Effects 1 §8.3): "Values are interpreted as for box-shadow
        /// but with the optional 3rd length value being the standard deviation instead of blur radius", so unlike
        /// <c>box-shadow</c> the blur value is the standard deviation itself.
        /// </summary>
        private static void ApplyDropShadow(RasterSurface surface, FilterGrammar.FilterFunction function, CssBox box)
        {
            var colorText = function.Arguments[3];
            var color = string.IsNullOrEmpty(colorText) || colorText.Equals(Keywords.CurrentColor, StringComparison.OrdinalIgnoreCase)
                ? box.ActualColor
                : box.HtmlContainer!.CssParser.ParseColor(colorText);
            if (color.A == 0)
                return;

            var dx = CssValueParser.ParseLength(function.Arguments[0], 0, box) * surface.PixelsPerUnitX;
            var dy = CssValueParser.ParseLength(function.Arguments[1], 0, box) * surface.PixelsPerUnitY;
            var sigma = Math.Max(0, CssValueParser.ParseLength(function.Arguments[2], 0, box));

            // Premultiply the shadow colour by its own alpha.
            var a = color.A;
            DropShadow.Apply(surface, (int)Math.Round(dx), (int)Math.Round(dy),
                sigma * surface.PixelsPerUnitX, sigma * surface.PixelsPerUnitY,
                (byte)PixelKernels.Div255(color.R * a), (byte)PixelKernels.Div255(color.G * a), (byte)PixelKernels.Div255(color.B * a), a);
        }

        /// <summary>
        /// How far, in layout units, a filter list can spread an element's ink beyond its own bounds: three
        /// standard deviations of every <c>blur()</c>, which is where a Gaussian has lost all but 0.3% of its weight.
        /// </summary>
        public static double InkMargin(IReadOnlyList<FilterGrammar.FilterFunction> functions, CssBox box)
        {
            double margin = 0;
            foreach (var function in functions)
            {
                if (function.Name == "blur" && !FilterEffectResolver.IsZeroLength(function))
                    margin += 3 * Math.Max(0, CssValueParser.ParseLength(function.Arguments[0], 0, box));
            }

            return margin;
        }
    }
}
