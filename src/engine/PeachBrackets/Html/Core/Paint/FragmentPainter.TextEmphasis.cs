using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachDrawing.Core;
using PeachDrawing.Text.Shaping;
using PeachDrawing.Text.Unicode;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PeachBrackets.Html.Paint
{
    internal sealed partial class FragmentPainter
    {
        /// <summary>
        /// Draws the <c>text-emphasis-style</c> marks of one horizontal word run: one mark per typographic character
        /// unit, centred on it, on the side <c>text-emphasis-position</c> names
        /// (<see href="https://www.w3.org/TR/css-text-decor-4/#text-emphasis-style-property">CSS Text Decoration 4 §4</see>).
        /// </summary>
        /// <remarks>
        /// A mark is a glyph at half the text's size, so it is drawn as text in the usual way and every backend gets it.
        /// Spaces and control characters get none. Layout reserves no room for the marks: a mark over the first line of a
        /// box with a tight <c>line-height</c> can overlap what is above it. Vertical text draws none yet.
        /// </remarks>
        private static void PaintTextEmphasis(Canvas g, CssBox styleSource, string text, PaintPoint point, Size size, ShapeSettings features, Font font,
            bool vertical = false)
        {
            var style = styleSource.TextEmphasisStyle;
            if (string.IsNullOrEmpty(style) || g.InvisibleText || text.Length == 0)
                return;

            if (!TextEmphasisGrammar.TryParseStyle(style, out var mark) || mark.Glyph() is not { } glyph)
                return;

            var over = true;
            var right = true;
            var position = styleSource.TextEmphasisPosition;
            if (!string.IsNullOrEmpty(position))
                TextEmphasisGrammar.TryParsePosition(position, out over, out right);

            // In a vertical writing mode the side is named by right/left, not over/under (CSS Text Decoration 4 section 4.3). A sideways run
            // is painted in its own rotated frame, whose over side faces the physical right.
            if (vertical)
                over = right;

            var markFont = styleSource.ActualFontForCodepoint(Rune.GetRuneAt(glyph, 0), 0.5);
            var markSize = g.MeasureString(glyph, markFont);
            var color = styleSource.ActualTextEmphasisColor;
            var palette = styleSource.ActualFontPalette;

            // Over: the mark's box ends where the text's em box begins. Under: it begins where it ends.
            var y = over ? point.Y - markSize.Height : point.Y + size.Height;

            var boundaries = Segmenter.FindGraphemeBoundaries(text);
            var edges = new List<int> { 0 };
            foreach (var b in boundaries)
            {
                if (b > edges[^1] && b <= text.Length)
                    edges.Add(b);
            }
            if (edges[^1] != text.Length)
                edges.Add(text.Length);

            var spacing = styleSource.ActualLetterSpacing;
            var previous = 0d;
            for (var i = 1; i < edges.Count; i++)
            {
                var end = edges[i];
                var width = g.MeasureString(text[..end], font, features).Width + spacing * i;
                var cell = text.AsSpan(edges[i - 1], end - edges[i - 1]);
                var start = previous;
                previous = width;

                if (cell.IsWhiteSpace() || IsControlOnly(cell))
                    continue;

                var x = point.X + start + (width - start - markSize.Width) / 2;
                // text-shadow applies to the marks too (CSS Text Decoration 4 section 4: they are text).
                PaintTextShadows(g, styleSource, markFont, glyph, new PaintPoint(x, y), markSize, default, null);
                g.DrawString(glyph, markFont, color, new PaintPoint(x, y), markSize, 0, palette, null);
            }
        }

        /// <summary>
        /// Draws the marks of an upright vertical run: one per character, centred on the character's cell down the column and set beside the
        /// glyph on the side <c>text-emphasis-position</c> names (<c>right</c> by default).
        /// </summary>
        private static void PaintUprightTextEmphasis(Canvas g, CssBox styleSource, IEnumerable<UprightGlyphPlacement> placements)
        {
            var style = styleSource.TextEmphasisStyle;
            if (string.IsNullOrEmpty(style) || g.InvisibleText)
                return;

            if (!TextEmphasisGrammar.TryParseStyle(style, out var mark) || mark.Glyph() is not { } glyph)
                return;

            var right = true;
            var position = styleSource.TextEmphasisPosition;
            if (!string.IsNullOrEmpty(position))
                TextEmphasisGrammar.TryParsePosition(position, out _, out right);

            var markFont = styleSource.ActualFontForCodepoint(Rune.GetRuneAt(glyph, 0), 0.5);
            var markSize = g.MeasureString(glyph, markFont);
            var color = styleSource.ActualTextEmphasisColor;
            var palette = styleSource.ActualFontPalette;

            foreach (var placement in placements)
            {
                if (placement.CharText.AsSpan().IsWhiteSpace() || IsControlOnly(placement.CharText))
                    continue;

                var x = right ? placement.X + placement.CharSize.Width : placement.X - markSize.Width;
                var y = placement.CellTop + (placement.Advance - markSize.Height) / 2;
                // text-shadow applies to the marks too (CSS Text Decoration 4 section 4: they are text).
                PaintTextShadows(g, styleSource, markFont, glyph, new PaintPoint(x, y), markSize, default, null);
                g.DrawString(glyph, markFont, color, new PaintPoint(x, y), markSize, 0, palette, null);
            }
        }

        private static bool IsControlOnly(ReadOnlySpan<char> cell)
        {
            foreach (var c in cell)
            {
                if (!char.IsControl(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format)
                    return false;
            }

            return true;
        }
    }
}
