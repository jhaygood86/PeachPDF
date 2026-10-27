using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Parse;
using PeachBrackets.Html.TextGeometry;
using PeachDrawing.Core;
using System;

namespace PeachBrackets.Html.Paint
{
    /// <summary>
    /// The selection half of the painter: a selected word's highlight and its text in the selected colour. Reads only what layout already
    /// placed - the word's rectangle and the character edges <see cref="SelectionModel"/> recorded - so selecting never changes a measurement.
    /// </summary>
    internal sealed partial class FragmentPainter
    {
        /// <summary>
        /// Paints a word with a selected part: the highlight behind it (and behind the space before it, when that is selected too), then the
        /// word. When the selection changes the text colour the word is drawn once for the part outside the selection and once, in the selected
        /// colour, clipped to the selected part - a PDF written this way holds that word's text more than once.
        /// </summary>
        private void PaintSelectedWord(Canvas g, CssBox box, CssRect word, Rect rect, string text, string? logicalText, double? separatorEdge, SelectionModel.Part part)
        {
            var ppp = container.RenderContext.LayoutUnitsPerPoint;
            var style = DomParser.ResolveSelectionStyle(container, word.OwnerBox);

            PaintColor? background;
            PaintColor? foreground;
            if (style is null)
            {
                background = container.SelectionBackground;
                foreground = container.SelectionForeground;
            }
            else
            {
                background = style.ActualBackgroundColor;

                // A colour the rules did not change is the text's own: the style was seeded from the element, so only a different one is an override.
                var own = style.ParentBox?.ActualColor;
                foreground = own is { } ownColor && ownColor.Equals(style.ActualColor) ? null : style.ActualColor;
            }

            if (!_textOnly && background is { A: > 0 } fill)
            {
                using var brush = g.GetSolidBrush(fill);
                if (part.HasCharacters)
                    g.DrawRectangle(brush, rect.X + (part.Lo * ppp), rect.Y, (part.Hi - part.Lo) * ppp, rect.Height);

                if (part.GapSelected)
                    g.DrawRectangle(brush, rect.X + (part.GapLo * ppp), rect.Y, (part.GapHi - part.GapLo) * ppp, rect.Height);
            }

            var size = new Size(word.Width, word.Height);
            if (foreground is not { } selectedColor || !part.HasCharacters)
            {
                DrawWordGlyphs(g, box, word, rect, text, size, logicalText: logicalText,
                    precededBySeparator: word.PrecededByWordSeparator, separatorEdge: separatorEdge);
                return;
            }

            // The clips reach well past the word's rectangle on every side the selection does not end at, so glyph overhang is not cut.
            var reach = rect.Height * 2;
            var selectedLeft = rect.X + (part.Lo * ppp);
            var selectedRight = rect.X + (part.Hi * ppp);

            DrawClipped(g, box, word, rect, text, size, logicalText, separatorEdge, null, new Rect(rect.X - reach, rect.Y - reach, selectedLeft - rect.X + reach, rect.Height + (reach * 2)));
            DrawClipped(g, box, word, rect, text, size, logicalText, separatorEdge, selectedColor, new Rect(selectedLeft, rect.Y - reach, selectedRight - selectedLeft, rect.Height + (reach * 2)));
            DrawClipped(g, box, word, rect, text, size, logicalText, separatorEdge, null, new Rect(selectedRight, rect.Y - reach, rect.Right - selectedRight + reach, rect.Height + (reach * 2)));
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
