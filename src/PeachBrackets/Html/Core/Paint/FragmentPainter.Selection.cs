using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Parse;
using PeachBrackets.Html.TextGeometry;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Html.Paint
{
    /// <summary>
    /// The selection half of the painter: a selected word's highlight and its text in the selected colour. Reads only what layout already
    /// placed - the word's rectangle and the character edges <see cref="SelectionModel"/> recorded - so selecting never changes a measurement.
    /// </summary>
    internal sealed partial class FragmentPainter
    {
        /// <summary>
        /// The highlights a word is in, lowest priority first (the selection last): each with what it fills behind the text and the colour it gives
        /// the text, or null when there are none and the word is drawn as usual.
        /// </summary>
        private List<(SelectionModel.Part Part, PaintColor? Background, PaintColor? Foreground)>? HighlightLayersOf(CssRect word)
        {
            List<(SelectionModel.Part, PaintColor?, PaintColor?)>? layers = null;

            foreach (var layer in container.HighlightLayers)
            {
                if (!layer.Model.TryGet(word, out var part)) continue;

                var style = DomParser.ResolveHighlightStyle(container, word.OwnerBox, layer.PseudoName);
                if (style is null) continue;

                (layers ??= []).Add((part, style.ActualBackgroundColor, ForegroundOf(style)));
            }

            if (container.SelectionModel is { } selection && selection.TryGet(word, out var selected))
            {
                var style = DomParser.ResolveSelectionStyle(container, word.OwnerBox);
                (layers ??= []).Add(style is null
                    ? (selected, container.SelectionBackground, container.SelectionForeground)
                    : (selected, style.ActualBackgroundColor, ForegroundOf(style)));
            }

            return layers;
        }

        // A colour the rules did not change is the text's own: the style was seeded from the element, so only a different one is an override.
        private static PaintColor? ForegroundOf(CssBox style)
        {
            var own = style.ParentBox?.ActualColor;
            return own is { } ownColor && ownColor.Equals(style.ActualColor) ? null : style.ActualColor;
        }

        /// <summary>
        /// Paints a word that is in one or more highlights: each highlight's background behind the text (and behind the space before the word, when
        /// that is covered too), lowest priority first, then the word. Where a highlight changes the text colour the word is drawn once per stretch
        /// of one colour, clipped to it, the highest-priority highlight covering a stretch winning - a PDF written this way holds that word's text more
        /// than once.
        /// </summary>
        private void PaintHighlightedWord(Canvas g, CssBox box, CssRect word, Rect rect, string text, string? logicalText, double? separatorEdge,
            List<(SelectionModel.Part Part, PaintColor? Background, PaintColor? Foreground)> layers)
        {
            var ppp = container.RenderContext.LayoutUnitsPerPoint;

            if (!_textOnly)
            {
                foreach (var (part, background, _) in layers)
                {
                    if (background is not { A: > 0 } fill) continue;

                    using var brush = g.GetSolidBrush(fill);
                    if (part.HasCharacters)
                        g.DrawRectangle(brush, rect.X + (part.Lo * ppp), rect.Y, (part.Hi - part.Lo) * ppp, rect.Height);

                    if (part.GapSelected)
                        g.DrawRectangle(brush, rect.X + (part.GapLo * ppp), rect.Y, (part.GapHi - part.GapLo) * ppp, rect.Height);
                }
            }

            var size = new Size(word.Width, word.Height);
            var colored = layers.Where(l => l.Foreground is not null && l.Part.HasCharacters).ToList();
            if (colored.Count == 0)
            {
                DrawWordGlyphs(g, box, word, rect, text, size, logicalText: logicalText,
                    precededBySeparator: word.PrecededByWordSeparator, separatorEdge: separatorEdge);
                return;
            }

            // The stretches the word divides into at every edge of a highlight that changes the colour. The clips reach well past the word's rectangle
            // on every side no highlight ends at, so glyph overhang is not cut.
            var reach = rect.Height * 2;
            var edges = new SortedSet<double> { rect.X, rect.Right };
            foreach (var layer in colored)
            {
                edges.Add(Math.Clamp(rect.X + (layer.Part.Lo * ppp), rect.X, rect.Right));
                edges.Add(Math.Clamp(rect.X + (layer.Part.Hi * ppp), rect.X, rect.Right));
            }

            var points = edges.ToList();
            for (var i = 0; i + 1 < points.Count; i++)
            {
                var left = points[i];
                var right = points[i + 1];
                if (right <= left) continue;

                var middle = (left + right) / 2;
                PaintColor? color = null;
                foreach (var layer in colored)
                {
                    if (middle >= rect.X + (layer.Part.Lo * ppp) && middle < rect.X + (layer.Part.Hi * ppp))
                        color = layer.Foreground;
                }

                var clipLeft = i == 0 ? left - reach : left;
                var clipRight = i + 2 == points.Count ? right + reach : right;
                DrawClipped(g, box, word, rect, text, size, logicalText, separatorEdge, color,
                    new Rect(clipLeft, rect.Y - reach, clipRight - clipLeft, rect.Height + (reach * 2)));
            }
        }

        private static void DrawClipped(Canvas g, CssBox box, CssRect word, Rect rect, string text, Size size, string? logicalText, double? separatorEdge, PaintColor? color, Rect clip)
        {
            if (clip.Width <= 0)
                return;

            g.PushClip(clip);
            try
            {
                DrawWordGlyphs(g, box, word, rect, text, size, logicalText: logicalText,
                    precededBySeparator: word.PrecededByWordSeparator, separatorEdge: separatorEdge, colorOverride: color);
            }
            finally
            {
                g.PopClip();
            }
        }
    }
}
