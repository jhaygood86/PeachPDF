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
        /// <summary>One highlight over a word: the part it covers, what it fills, and the parts of its style the painter reads.</summary>
        /// <param name="Part">what it covers of the word</param>
        /// <param name="Background">the fill behind the text</param>
        /// <param name="Foreground">the text colour it sets, or null when the rules left the element's own</param>
        /// <param name="Style">its resolved style, whose parent is the element the text is in</param>
        private readonly record struct HighlightPaint(SelectionModel.Part Part, PaintColor? Background, PaintColor? Foreground, CssBox? Style)
        {
            /// <summary>Whether its <c>text-shadow</c> or text stroke differs from the element's own, so the word is drawn with it over this part.</summary>
            public bool ChangesEffects => Style is { ParentBox: { } own } style
                && (!string.Equals(style.TextShadow, own.TextShadow, StringComparison.Ordinal)
                    || style.ActualTextStrokeWidth != own.ActualTextStrokeWidth
                    || !style.ActualTextStrokeColor.Equals(own.ActualTextStrokeColor));

            /// <summary>Whether it declares a <c>text-decoration</c> the element does not already have.</summary>
            public bool AddsDecoration => Style is { ParentBox: { } own } style
                && !string.IsNullOrEmpty(style.TextDecorationLine) && style.TextDecorationLine != Keywords.None
                && !string.Equals(style.TextDecorationLine, own.TextDecorationLine, StringComparison.Ordinal);
        }

        /// <summary>
        /// The highlights a word is in, lowest priority first (the selection last), or null when there are none and the word is drawn as usual.
        /// </summary>
        private List<HighlightPaint>? HighlightLayersOf(CssRect word)
        {
            List<HighlightPaint>? layers = null;

            foreach (var layer in container.HighlightLayers)
            {
                if (!layer.Model.TryGet(word, out var part)) continue;

                var style = DomParser.ResolveHighlightStyle(container, word.OwnerBox, layer.PseudoName);
                if (style is null) continue;

                (layers ??= []).Add(new HighlightPaint(part, style.ActualBackgroundColor, ForegroundOf(style), style));
            }

            if (container.SelectionModel is { } selection && selection.TryGet(word, out var selected))
            {
                var style = DomParser.ResolveSelectionStyle(container, word.OwnerBox);
                (layers ??= []).Add(style is null
                    ? new HighlightPaint(selected, container.SelectionBackground, container.SelectionForeground, null)
                    : new HighlightPaint(selected, style.ActualBackgroundColor, ForegroundOf(style), style));
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
        /// The top and height a highlight fills for a word: its whole line box, as browsers fill a selection, not only the word's own rectangle.
        /// Falls back to the word's rectangle for a word on no line.
        /// </summary>
        private static (double Top, double Height) FillBand(CssRect word, Rect rect)
        {
            if (word.Line is not { } line) return (rect.Y, rect.Height);

            var height = line.LineBottom - line.LineTop;
            return height > 0 ? (rect.Y + (line.LineTop - word.Top), height) : (rect.Y, rect.Height);
        }

        /// <summary>
        /// Paints a word that is in one or more highlights: each highlight's background behind the text (and behind the space before the word, when
        /// that is covered too), lowest priority first, then the word, then any text decoration a highlight adds. Where a highlight changes the text
        /// colour, shadow or stroke the word is drawn once per stretch of one look, clipped to it, the highest-priority highlight covering a stretch
        /// winning - a PDF written this way holds that word's text more than once.
        /// </summary>
        private void PaintHighlightedWord(Canvas g, CssBox box, CssRect word, Rect rect, string text, string? logicalText, double? separatorEdge,
            List<HighlightPaint> layers)
        {
            var ppp = container.RenderContext.LayoutUnitsPerPoint;

            if (!_textOnly)
            {
                var (top, height) = FillBand(word, rect);
                foreach (var layer in layers)
                {
                    if (layer.Background is not { A: > 0 } fill) continue;

                    using var brush = g.GetSolidBrush(fill);
                    if (layer.Part.HasCharacters)
                        g.DrawRectangle(brush, rect.X + (layer.Part.Lo * ppp), top, (layer.Part.Hi - layer.Part.Lo) * ppp, height);

                    if (layer.Part.GapSelected)
                        g.DrawRectangle(brush, rect.X + (layer.Part.GapLo * ppp), top, (layer.Part.GapHi - layer.Part.GapLo) * ppp, height);
                }
            }

            var size = new Size(word.Width, word.Height);
            var changing = layers.Where(l => l.Part.HasCharacters && (l.Foreground is not null || l.ChangesEffects)).ToList();
            if (changing.Count == 0)
            {
                DrawWordGlyphs(g, box, word, rect, text, size, logicalText: logicalText,
                    precededBySeparator: word.PrecededByWordSeparator, separatorEdge: separatorEdge);
            }
            else
            {
                // The stretches the word divides into at every edge of a highlight that changes how it is drawn. The clips reach well past the word's
                // rectangle on every side no highlight ends at, so glyph overhang is not cut.
                var reach = rect.Height * 2;
                var edges = new SortedSet<double> { rect.X, rect.Right };
                foreach (var layer in changing)
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
                    CssBox? effects = null;
                    foreach (var layer in changing)
                    {
                        if (middle < rect.X + (layer.Part.Lo * ppp) || middle >= rect.X + (layer.Part.Hi * ppp)) continue;

                        if (layer.Foreground is not null) color = layer.Foreground;
                        if (layer.ChangesEffects) effects = layer.Style;
                    }

                    var clipLeft = i == 0 ? left - reach : left;
                    var clipRight = i + 2 == points.Count ? right + reach : right;
                    DrawClipped(g, box, word, rect, text, size, logicalText, separatorEdge, color,
                        new Rect(clipLeft, rect.Y - reach, clipRight - clipLeft, rect.Height + (reach * 2)), effects);
                }
            }

            if (_textOnly) return;

            // A decoration the highlight adds runs under the part it covers, in the highlight's own line style and colour.
            foreach (var layer in layers)
            {
                if (!layer.AddsDecoration || !layer.Part.HasCharacters) continue;

                var span = new Rect(rect.X + (layer.Part.Lo * ppp), rect.Y, (layer.Part.Hi - layer.Part.Lo) * ppp, rect.Height);
                PaintDecoration(g, box, span, hasLeftEdge: false, hasRightEdge: false, firstLineStyle: layer.Style, ownDecorationArea: false);
            }
        }

        private static void DrawClipped(Canvas g, CssBox box, CssRect word, Rect rect, string text, Size size, string? logicalText, double? separatorEdge, PaintColor? color, Rect clip, CssBox? effects = null)
        {
            if (clip.Width <= 0)
                return;

            g.PushClip(clip);
            try
            {
                DrawWordGlyphs(g, box, word, rect, text, size, logicalText: logicalText,
                    precededBySeparator: word.PrecededByWordSeparator, separatorEdge: separatorEdge, colorOverride: color, effectsStyle: effects);
            }
            finally
            {
                g.PopClip();
            }
        }
    }
}
