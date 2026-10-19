using PeachDrawing.Core;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Paint.Content;
using System;

namespace PeachBrackets.Html.Annotations
{
    /// <summary>
    /// Paints how a form control looks, onto any <see cref="Canvas"/>: the control's real resolved CSS border and background
    /// (<see cref="FormFieldChrome"/>, shared with the page painter) and its real resolved font, color and padding, so it draws the way any
    /// other box's content would, including real font embedding for whatever glyphs its text needs. A consumer that makes controls
    /// interactive (a PDF writer making an AcroForm widget) draws each state it needs - the text, the checked and unchecked box - once,
    /// into whatever surface holds that state.
    /// </summary>
    /// <remarks>
    /// All geometry is in the layout space <see cref="CssBox"/>'s own <c>Actual*</c> properties are in: the control's size in points
    /// scaled up by <c>pixelsPerPoint</c>. Each paint call takes the size in points and does that scaling itself, then draws onto a canvas
    /// whose own pixels-per-point is the same, so the control's border, padding and font stay sized consistently with the rest of the
    /// box, including under <c>ShrinkToFit</c> or a non-default <c>PixelsPerInch</c>.
    /// </remarks>
    internal sealed class FormFieldAppearance(CssBox box, double pixelsPerPoint)
    {
        /// <summary>The pixels per point the control was laid out with, which the canvas passed to the paint calls must use too.</summary>
        public double PixelsPerPoint { get; } = pixelsPerPoint;

        private Rect LayoutRect(Size sizePt) => new(0, 0, sizePt.Width * PixelsPerPoint, sizePt.Height * PixelsPerPoint);

        private Rect ContentRect(Rect rect) => new(
            rect.X + box.ActualBorderLeftWidth + box.ActualPaddingLeft,
            rect.Y + box.ActualBorderTopWidth + box.ActualPaddingTop,
            Math.Max(0, rect.Width - box.ActualBorderLeftWidth - box.ActualBorderRightWidth - box.ActualPaddingLeft - box.ActualPaddingRight),
            Math.Max(0, rect.Height - box.ActualBorderTopWidth - box.ActualBorderBottomWidth - box.ActualPaddingTop - box.ActualPaddingBottom));

        /// <summary>
        /// The font a text or select appearance draws with: the box's own <see cref="CssBox.ActualFont"/> normally, or the same
        /// family, weight and style at a size fitted to the field's content-box height when
        /// <c>-peachpdf-pdf-form-field-auto-font-size</c> asked for it. A placeholder takes the independently cascaded
        /// <c>::placeholder</c> style.
        /// </summary>
        private Font ResolveTextFont(CssBox textStyle, Rect contentRect, bool autoFontSize)
        {
            if (!autoFontSize) return textStyle.ActualFont;

            var contentHeightPt = contentRect.Height / PixelsPerPoint;
            var fitSizePt = Math.Clamp(contentHeightPt * 0.6, 4, 12);
            return textStyle.GetActualFontAtSize(fitSizePt);
        }

        /// <summary>The size in points of the font a text appearance of this size draws with.</summary>
        /// <param name="sizePt">the control's size, in points</param>
        /// <param name="autoFontSize">whether the font is fitted to the control's height</param>
        /// <param name="isPlaceholder">whether the text is the placeholder hint rather than a value</param>
        public double TextFontSizePt(Size sizePt, bool autoFontSize, bool isPlaceholder)
        {
            var textStyle = isPlaceholder ? box.ResolvedPlaceholderStyle ?? box : box;
            return ResolveTextFont(textStyle, ContentRect(LayoutRect(sizePt)), autoFontSize).Size;
        }

        /// <summary>
        /// Paints a single-line text (or combo-box) appearance: the border and background, then the value in the field's real font and
        /// color, left aligned and vertically centered in the content box. With <paramref name="combCells"/>, each character is instead
        /// centered in its own evenly divided cell (ISO 32000-1 §12.7.4.3's "comb" field), with divider lines between cells in the
        /// field's own border color and width.
        /// </summary>
        /// <remarks>
        /// <paramref name="isPlaceholder"/> says the text is a placeholder hint rather than the value, in which case font, color and
        /// opacity come from the independently cascaded <c>::placeholder</c> style. The text goes between
        /// <see cref="Canvas.BeginVariableText"/> and <see cref="Canvas.EndVariableText"/>, which is what makes a drawn hint safe: a
        /// reader regenerating the field replaces the whole sequence, so the hint disappears on the first keystroke instead of sitting
        /// behind what is typed.
        /// </remarks>
        /// <param name="g">the canvas to paint onto, laid out at <see cref="PixelsPerPoint"/></param>
        /// <param name="sizePt">the control's size, in points</param>
        /// <param name="text">what to draw</param>
        /// <param name="autoFontSize">whether the font is fitted to the control's height</param>
        /// <param name="combCells">the number of comb cells, or null for none</param>
        /// <param name="isPlaceholder">whether <paramref name="text"/> is the placeholder hint</param>
        public void PaintText(Canvas g, Size sizePt, string text, bool autoFontSize, int? combCells, bool isPlaceholder)
        {
            text ??= string.Empty;

            var rect = LayoutRect(sizePt);
            var contentRect = ContentRect(rect);
            var textStyle = isPlaceholder ? box.ResolvedPlaceholderStyle ?? box : box;
            var font = ResolveTextFont(textStyle, contentRect, autoFontSize);

            // Everything a reader must KEEP when the user edits the field goes before the variable text sequence below and after its end.
            // A comb field's cell dividers are chrome too, so they are drawn here with the border rather than alongside the characters
            // they separate.
            FormFieldChrome.PaintBorderAndBackground(g, box, rect);

            var drawable = contentRect is { Width: > 0, Height: > 0 };

            if (drawable && combCells is > 0)
                DrawCombDividers(g, rect, contentRect, combCells.Value);

            // ...and the value itself - the only part a reader regenerates - goes inside. Opened unconditionally: the sequence is what
            // gives the reader a region to replace, so a field with an empty value needs one just as much as a filled one, and so does a
            // field whose padding and border leave no content box to draw into at all. Returning early for either would reintroduce
            // exactly the append-instead-of-replace bug this sequence exists to prevent, in the two cases hardest to notice before
            // someone actually types into the field.
            g.BeginVariableText();

            if (drawable)
            {
                var color = isPlaceholder
                    ? ApplyOpacity(textStyle.ActualColor, textStyle.ActualOpacity)
                    : box.ActualColor;

                if (combCells is > 0)
                    DrawCombCharacters(g, color, contentRect, font, text, combCells.Value);
                else
                    DrawSingleLine(g, textStyle, color, contentRect, font, text);
            }

            g.EndVariableText();
        }

        /// <summary>Paints a checkbox: the border and background, plus the check mark in the field's real text color when it is checked.</summary>
        /// <param name="g">the canvas to paint onto, laid out at <see cref="PixelsPerPoint"/></param>
        /// <param name="sizePt">the control's size, in points</param>
        /// <param name="isChecked">whether to draw the check mark</param>
        public void PaintCheckbox(Canvas g, Size sizePt, bool isChecked)
        {
            var rect = LayoutRect(sizePt);
            FormFieldChrome.PaintBorderAndBackground(g, box, rect);
            if (isChecked)
                FormFieldChrome.PaintCheckboxGlyph(g, box, rect, isChecked: true);
        }

        /// <summary>Paints a radio button: the circular background and ring, plus the filled inner dot in the field's real text color when it is selected.</summary>
        /// <param name="g">the canvas to paint onto, laid out at <see cref="PixelsPerPoint"/></param>
        /// <param name="sizePt">the control's size, in points</param>
        /// <param name="isSelected">whether to draw the inner dot</param>
        public void PaintRadio(Canvas g, Size sizePt, bool isSelected)
        {
            var rect = LayoutRect(sizePt);
            FormFieldChrome.PaintRadioBackground(g, box, rect);
            if (isSelected)
                FormFieldChrome.PaintRadioGlyph(g, box, rect, isChecked: true);
        }

        /// <summary>
        /// Applies a text style's CSS <c>opacity</c> to its color. A placeholder contains only this text, so multiplying the fill alpha is
        /// visually equivalent to compositing a separate opacity group while letting the normal graphics path enforce a conformance
        /// level's transparency rules for an author-specified translucent <c>::placeholder</c> style.
        /// </summary>
        private static PaintColor ApplyOpacity(PaintColor color, double opacity)
        {
            var alpha = (int)Math.Round(color.A * Math.Clamp(opacity, 0, 1));
            return PaintColor.FromArgb(alpha, color.R, color.G, color.B);
        }

        private static void DrawSingleLine(Canvas g, CssBox box, PaintColor color, Rect contentRect, Font font, string text)
        {
            var y = contentRect.Y + Math.Max((contentRect.Height - font.Height) / 2, 0);
            // letter-spacing is deliberately not read here: CssBox.ActualLetterSpacing is only populated by the normal word-measurement
            // pass (DerivedStyle.MeasureLetterSpacing), which a form-field box's own replaced-element sizing
            // (CssBoxFormField.MeasureWordsSize) never runs - reading it here would hit its NaN-sentinel default instead.
            g.DrawString(text, font, color, new PaintPoint(contentRect.X, y),
                new Size(contentRect.Width, font.Height), fontPalette: box.ActualFontPalette, features: box.ActualTextShapingFeatures);
        }

        /// <summary>A comb field's cell divider lines, in the field's own border color and width.</summary>
        private void DrawCombDividers(Canvas g, Rect fieldRect, Rect contentRect, int cells)
        {
            if (cells <= 1 || box.ActualBorderLeftWidth <= 0) return;

            var cellWidth = contentRect.Width / cells;
            var pen = g.GetPen(box.ActualBorderLeftColor);
            pen.Width = box.ActualBorderLeftWidth;
            var top = fieldRect.Y + box.ActualBorderTopWidth;
            var bottom = fieldRect.Y + fieldRect.Height - box.ActualBorderBottomWidth;
            for (var i = 1; i < cells; i++)
            {
                var x = fieldRect.X + box.ActualBorderLeftWidth + i * cellWidth;
                g.DrawLine(pen, x, top, x, bottom);
            }
        }

        /// <summary>Each character centered in its own evenly divided cell.</summary>
        private static void DrawCombCharacters(Canvas g, PaintColor color, Rect contentRect, Font font, string text, int cells)
        {
            var cellWidth = contentRect.Width / cells;
            var y = contentRect.Y + Math.Max((contentRect.Height - font.Height) / 2, 0);

            for (var i = 0; i < cells && i < text.Length; i++)
            {
                var ch = text[i].ToString();
                var chWidth = g.MeasureString(ch, font).Width;
                var x = contentRect.X + i * cellWidth + Math.Max((cellWidth - chWidth) / 2, 0);
                g.DrawString(ch, font, color, new PaintPoint(x, y), new Size(cellWidth, font.Height));
            }
        }
    }
}
