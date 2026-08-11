using System;
using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Paint;
using PeachPDF.Html.Core.Paint.Content;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.Advanced;

namespace PeachPDF.Html.Core.Handlers
{
    /// <summary>
    /// Builds the "/AP /N" (and, for a two-state field, "/AP /D") appearance-stream
    /// <see cref="PdfFormXObject"/>s for interactive PDF form fields, through PeachPDF's own real
    /// rendering pipeline (<see cref="GraphicsAdapter"/> over a fresh <see cref="XForm"/>, exactly
    /// the recipe <c>GraphicsAdapter.CreateTile</c> already uses for SVG mask/pattern tiles) rather
    /// than hand-written content-stream operators - the field's real resolved CSS border/background
    /// (<see cref="FormFieldChrome"/>, shared with the flag-on static page painter) and its real
    /// resolved font/color/padding (<see cref="CssBox.ActualFont"/>/<see cref="CssBox.ActualColor"/>)
    /// paint the same way any other box's would, including real font embedding (not just the PDF
    /// standard fonts) for whatever glyphs the field's own text needs.
    /// </summary>
    /// <remarks>
    /// All local geometry here is in "layout space" - the same device-pixel-scaled units
    /// <see cref="CssBox"/>'s own <c>Actual*</c> properties are already in, not yet divided down to
    /// true PDF points. <see cref="CreateForm"/> builds its local rect by scaling the field's real
    /// (already-in-points) width/height back UP by <c>pixelsPerPoint</c> before handing it to
    /// <see cref="FormFieldChrome"/>/<see cref="BordersDrawHandler"/>/<see cref="FragmentPainter"/> -
    /// each of those, like every other paint call in PeachPDF, divides back down by the
    /// <see cref="GraphicsAdapter"/>'s own <c>pixelsPerPoint</c> exactly once when it actually draws,
    /// so passing the SAME <c>pixelsPerPoint</c> the box was laid out with is what keeps a field's
    /// border/padding/font sized consistently with the rest of the box, including under
    /// <c>ShrinkToFit</c>/a non-default <c>PixelsPerInch</c> (see <see cref="CssBox.ActualFont"/>'s
    /// own doc comment for the same convention on the font side).
    /// </remarks>
    internal static class FormFieldAppearanceBuilder
    {
        /// <summary>
        /// Creates the shared "/Helv" Type1 Helvetica font resource dictionary. Not used by any
        /// appearance stream this class builds (those use the field's own real, possibly embedded
        /// font) - only by each field's "/DA" default appearance string, which governs how a reader
        /// regenerates the field's look once a user actually edits it. "/DA" deliberately keeps using
        /// a PDF standard font rather than the field's own real one: PeachPDF (like most PDF
        /// generators) subsets an embedded font down to only the glyphs the document actually drew,
        /// so a user typing a character that never appeared in the baked appearance would have no
        /// glyph to render with the real font, whereas Helvetica's WinAnsi coverage is complete and
        /// needs no embedding at all.
        /// </summary>
        internal static PdfDictionary GetOrCreateHelveticaFontResource(PdfDocument document, ref PdfDictionary? cache)
        {
            if (cache != null)
                return cache;

            var font = new PdfDictionary(document);
            font.Elements.SetName("/Type", "/Font");
            font.Elements.SetName("/Subtype", "/Type1");
            font.Elements.SetName("/BaseFont", "/Helvetica");
            font.Elements.SetName("/Encoding", "/WinAnsiEncoding");
            document.Internals.AddObject(font);

            cache = font;
            return font;
        }

        static PdfFormXObject CreateForm(PdfDocument document, RAdapter adapter, double pixelsPerPoint,
            double widthPt, double heightPt, Action<RGraphics, RRect> draw)
        {
            var form = new XForm(document, new XSize(widthPt, heightPt));
            var formGraphics = XGraphics.FromForm(form);
            using (var g = new GraphicsAdapter(adapter, formGraphics, pixelsPerPoint, releaseGraphics: true))
            {
                draw(g, LayoutRect(widthPt, heightPt, pixelsPerPoint));
            }
            return form.PdfForm;
        }

        static RRect LayoutRect(double widthPt, double heightPt, double pixelsPerPoint) =>
            new(0, 0, widthPt * pixelsPerPoint, heightPt * pixelsPerPoint);

        static RRect ContentRect(CssBox box, RRect rect) => new(
            rect.X + box.ActualBorderLeftWidth + box.ActualPaddingLeft,
            rect.Y + box.ActualBorderTopWidth + box.ActualPaddingTop,
            Math.Max(0, rect.Width - box.ActualBorderLeftWidth - box.ActualBorderRightWidth - box.ActualPaddingLeft - box.ActualPaddingRight),
            Math.Max(0, rect.Height - box.ActualBorderTopWidth - box.ActualBorderBottomWidth - box.ActualPaddingTop - box.ActualPaddingBottom));

        /// <summary>
        /// Resolves the font a text/select appearance actually draws with - the box's own real
        /// <see cref="CssBox.ActualFont"/> (family/weight/style/size, all author-controlled via
        /// ordinary CSS) normally, or the same family/weight/style at a size fitted to the field's
        /// content-box height when <c>-peachpdf-pdf-form-field-auto-font-size</c> requested it (via
        /// <see cref="CssBox.GetActualFontAtSize"/> - only the size differs).
        /// </summary>
        static RFont ResolveTextFont(CssBox box, RRect contentRect, double pixelsPerPoint, bool autoFontSize)
        {
            if (!autoFontSize) return box.ActualFont;

            var contentHeightPt = contentRect.Height / pixelsPerPoint;
            var fitSizePt = Math.Clamp(contentHeightPt * 0.6, 4, 12);
            return box.GetActualFontAtSize(fitSizePt);
        }

        /// <summary>
        /// A single-line text (or combo-box) appearance: the field's real CSS border/background,
        /// then the value/label in the field's real font/color, left aligned and vertically centered
        /// in the content box. When <paramref name="combCells"/> is set, each character is instead
        /// centered in its own evenly divided cell (ISO 32000-1 §12.7.4.3's "comb" field), with
        /// divider lines between cells drawn in the field's own border color/width.
        /// </summary>
        internal static PdfFormXObject BuildTextAppearance(PdfDocument document, RAdapter adapter, double pixelsPerPoint,
            CssBox box, double widthPt, double heightPt, string text, bool autoFontSize, int? combCells,
            out double resolvedFontSizePt)
        {
            text ??= string.Empty;

            var layoutRect = LayoutRect(widthPt, heightPt, pixelsPerPoint);
            var contentRect = ContentRect(box, layoutRect);
            var font = ResolveTextFont(box, contentRect, pixelsPerPoint, autoFontSize);
            resolvedFontSizePt = font.Size;

            return CreateForm(document, adapter, pixelsPerPoint, widthPt, heightPt, (g, rect) =>
            {
                FormFieldChrome.PaintBorderAndBackground(g, box, rect);

                if (contentRect is not { Width: > 0, Height: > 0 }) return;

                if (combCells is > 0)
                    DrawComb(g, box, rect, contentRect, font, text, combCells.Value);
                else
                    DrawSingleLine(g, box, contentRect, font, text);
            });
        }

        static void DrawSingleLine(RGraphics g, CssBox box, RRect contentRect, RFont font, string text)
        {
            var y = contentRect.Y + Math.Max((contentRect.Height - font.Height) / 2, 0);
            // letter-spacing is deliberately not read here: CssBox.ActualLetterSpacing is only
            // populated by the normal word-measurement pass (DerivedStyle.MeasureLetterSpacing),
            // which a form-field box's own replaced-element sizing (CssBoxFormField.MeasureWordsSize)
            // never runs - reading it here would hit its NaN-sentinel default instead.
            g.DrawString(text, font, box.ActualColor, new RPoint(contentRect.X, y),
                new RSize(contentRect.Width, font.Height), fontPalette: box.ActualFontPalette, features: box.ActualTextShapingFeatures);
        }

        static void DrawComb(RGraphics g, CssBox box, RRect fieldRect, RRect contentRect, RFont font, string text, int cells)
        {
            var cellWidth = contentRect.Width / cells;
            var y = contentRect.Y + Math.Max((contentRect.Height - font.Height) / 2, 0);

            for (var i = 0; i < cells && i < text.Length; i++)
            {
                var ch = text[i].ToString();
                var chWidth = g.MeasureString(ch, font).Width;
                var x = contentRect.X + i * cellWidth + Math.Max((cellWidth - chWidth) / 2, 0);
                g.DrawString(ch, font, box.ActualColor, new RPoint(x, y), new RSize(cellWidth, font.Height));
            }

            if (cells <= 1 || box.ActualBorderLeftWidth <= 0) return;

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

        /// <summary>A checkbox/radio "off" appearance: the field's border/background chrome only.</summary>
        internal static PdfFormXObject BuildCheckboxOffAppearance(PdfDocument document, RAdapter adapter, double pixelsPerPoint, CssBox box, double widthPt, double heightPt) =>
            CreateForm(document, adapter, pixelsPerPoint, widthPt, heightPt, (g, rect) => FormFieldChrome.PaintBorderAndBackground(g, box, rect));

        /// <summary>A checkbox "on" appearance: border/background chrome plus the check mark, in the field's real resolved text color.</summary>
        internal static PdfFormXObject BuildCheckboxOnAppearance(PdfDocument document, RAdapter adapter, double pixelsPerPoint, CssBox box, double widthPt, double heightPt) =>
            CreateForm(document, adapter, pixelsPerPoint, widthPt, heightPt, (g, rect) =>
            {
                FormFieldChrome.PaintBorderAndBackground(g, box, rect);
                FormFieldChrome.PaintCheckboxGlyph(g, box, rect, isChecked: true);
            });

        /// <summary>A radio button "off" appearance: the field's circular background/ring only.</summary>
        internal static PdfFormXObject BuildRadioOffAppearance(PdfDocument document, RAdapter adapter, double pixelsPerPoint, CssBox box, double widthPt, double heightPt) =>
            CreateForm(document, adapter, pixelsPerPoint, widthPt, heightPt, (g, rect) => FormFieldChrome.PaintRadioBackground(g, box, rect));

        /// <summary>A radio button "on" appearance: circular background/ring plus the filled inner dot, in the field's real resolved text color.</summary>
        internal static PdfFormXObject BuildRadioOnAppearance(PdfDocument document, RAdapter adapter, double pixelsPerPoint, CssBox box, double widthPt, double heightPt) =>
            CreateForm(document, adapter, pixelsPerPoint, widthPt, heightPt, (g, rect) =>
            {
                FormFieldChrome.PaintRadioBackground(g, box, rect);
                FormFieldChrome.PaintRadioGlyph(g, box, rect, isChecked: true);
            });
    }
}
