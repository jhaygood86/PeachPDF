using PeachDrawing.Text.Shaping;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Raster.Filters;
using PeachDrawing.Text.Internal.Text;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Paint
{
    internal sealed partial class FragmentPainter
    {
        /// <summary>Parsed <c>text-shadow</c> values by authored text - a page repeats the same value on every word it applies to.</summary>
        private static readonly ConcurrentDictionary<string, IReadOnlyList<TextShadowGrammar.ShadowLayer>> TextShadowCache = new();

        private static IReadOnlyList<TextShadowGrammar.ShadowLayer> ParseTextShadow(string value)
        {
            if (TextShadowCache.TryGetValue(value, out var cached))
                return cached;

            List<TextShadowGrammar.ShadowLayer>? layers;
            using (var pooledTokens = CssValueParser.GetCssTokensPooled(value))
            {
                List<Token> tokens = pooledTokens;
                layers = TextShadowGrammar.TryParse(tokens);
            }

            IReadOnlyList<TextShadowGrammar.ShadowLayer> result = layers ?? [];
            if (TextShadowCache.Count > 256)
                TextShadowCache.Clear();

            TextShadowCache[value] = result;
            return result;
        }

        /// <summary>
        /// Paints the <c>text-shadow</c> layers of one horizontal word run, underneath the run itself
        /// (<see href="https://www.w3.org/TR/css-text-decor-3/#text-shadow-property">CSS Text Decoration 3 §4.1</see>: the
        /// first shadow is on top, so they are painted last to first).
        /// </summary>
        /// <remarks>
        /// A shadow with no blur is just the text drawn again at an offset, in the shadow colour, and stays vector. A
        /// blurred one is drawn into a bitmap and Gaussian-blurred (the blur radius is twice the standard deviation, as for
        /// <c>box-shadow</c>) and drawn beneath the text. Only the shadow is a bitmap, so the text itself stays vector,
        /// selectable and tagged. A graphics that cannot rasterize draws a blurred shadow as nothing, which is what an
        /// unsupported <c>text-shadow</c> always did.
        /// </remarks>
        private static void PaintTextShadows(RGraphics g, CssBox styleSource, RFont font, string text, RPoint point, RSize size,
            ShapeSettings features, string? logicalText)
        {
            // Shadows are shapes, not text: nothing for an invisible-text pass to supply.
            if (g.InvisibleText)
                return;

            var value = styleSource.TextShadow;
            if (string.IsNullOrEmpty(value) || value.Equals(Keywords.None, StringComparison.OrdinalIgnoreCase))
                return;

            var layers = ParseTextShadow(value);
            for (var i = layers.Count - 1; i >= 0; i--)
            {
                var layer = layers[i];
                var color = ResolveShadowColor(styleSource, layer.Color);
                if (color.A == 0)
                    continue;

                var dx = CssValueParser.ParseLength(layer.OffsetX, 0, styleSource);
                var dy = CssValueParser.ParseLength(layer.OffsetY, 0, styleSource);
                var blur = Math.Max(0, CssValueParser.ParseLength(layer.Blur, 0, styleSource));
                var shadowPoint = new RPoint(point.X + dx, point.Y + dy);

                if (blur <= 0)
                {
                    g.DrawString(text, font, color, shadowPoint, size, styleSource.ActualLetterSpacing, styleSource.ActualFontPalette, features, logicalText);
                    continue;
                }

                // Glyph ink can overhang the word's own box a little (an italic's slant, a wide swash); the padding keeps it.
                var overhang = size.Height * 0.25;
                var margin = 1.5 * blur + overhang;
                var run = new RRect(shadowPoint.X, shadowPoint.Y, size.Width, size.Height);
                var bounds = Intersect(Inflate(run, margin), Inflate(g.GetClip(), margin));
                if (bounds.Width <= 0 || bounds.Height <= 0)
                    continue;

                using var scope = g.BeginRasterSurface(bounds);
                if (scope is null)
                    continue;

                scope.Graphics.DrawString(text, font, color, shadowPoint, size, styleSource.ActualLetterSpacing,
                    styleSource.ActualFontPalette, features, logicalText);

                var sigma = blur / 2;
                GaussianBlur.Apply(scope.Surface, sigma * scope.Surface.PixelsPerUnitX, sigma * scope.Surface.PixelsPerUnitY);
                g.DrawRaster(scope.Surface);
            }
        }
    }
}
