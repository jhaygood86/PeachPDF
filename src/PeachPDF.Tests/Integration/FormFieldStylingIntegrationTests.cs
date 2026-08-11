using System.IO;
using System.Text;
using System.Threading.Tasks;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.AcroForms;
using PeachPDF.PdfSharpCore.Pdf.Advanced;

namespace PeachPDF.Tests.Integration
{
    // Covers the interactive-forms follow-up: a field's border/background/padding/color/font are
    // real, author-overridable CSS (FormFieldAppearanceBuilder/FormFieldChrome), not the hardcoded
    // black-border/white-background/Helvetica-only look the feature originally shipped with. Per
    // CLAUDE.md's testing conventions, these assert structurally (stream length/operator presence,
    // or "two different declarations bake two different streams") rather than on decoded glyph text
    // or literal color-operator bytes - see InteractivePdfFormsPaintOrderTests's own remarks for why
    // real font embedding makes literal-text assertions unreliable here.
    public class FormFieldStylingIntegrationTests
    {
        [Fact]
        public async Task TextField_UnstyledInput_GetsDefaultBorderAndBackgroundFromUaStylesheet()
        {
            // Backward-compatibility check: an author who sets no border/background at all must still
            // see a field, not a blank box - PeachPDF's UA stylesheet supplies a plain default
            // (CssDefaults.DefaultStyleSheet's "input, select" rule) the same way every field looked
            // before per-field CSS styling existed.
            var withDefault = await BuildWidgetAppearanceStream("<input name='n' value='hi' />");
            var withNoChrome = await BuildWidgetAppearanceStream(
                "<input name='n' value='hi' style=\"border:none;background-color:transparent;padding:0\" />");

            Assert.True(withDefault.Length > withNoChrome.Length,
                "The UA default border/background must draw strictly more content than an author explicitly suppressing both.");
        }

        [Fact]
        public async Task TextField_CustomBorderColor_BakesADifferentAppearanceStreamThanDefault()
        {
            var defaultStream = await BuildWidgetAppearanceStream("<input name='n' value='hi' />");
            var customStream = await BuildWidgetAppearanceStream("<input name='n' value='hi' style=\"border-color:#8a2be2\" />");

            Assert.NotEqual(defaultStream, customStream);
        }

        [Fact]
        public async Task TextField_CustomBackgroundColor_BakesADifferentAppearanceStreamThanDefault()
        {
            var defaultStream = await BuildWidgetAppearanceStream("<input name='n' value='hi' />");
            var customStream = await BuildWidgetAppearanceStream("<input name='n' value='hi' style=\"background-color:#f5e9ff\" />");

            Assert.NotEqual(defaultStream, customStream);
        }

        [Fact]
        public async Task TextField_CustomPadding_MovesTheBakedTextPosition()
        {
            var smallPadding = await BuildWidgetAppearanceStream("<input name='n' value='hi' style=\"padding:1pt 2pt\" />");
            var largePadding = await BuildWidgetAppearanceStream("<input name='n' value='hi' style=\"padding:1pt 20pt\" />");

            Assert.NotEqual(smallPadding, largePadding);
        }

        [Fact]
        public async Task Checkbox_CustomColor_ChangesTheCheckMarkAppearanceStream()
        {
            var defaultStream = await BuildWidgetAppearanceStream("<input type='checkbox' name='c' checked />");
            var customStream = await BuildWidgetAppearanceStream("<input type='checkbox' name='c' checked style=\"color:red\" />");

            Assert.NotEqual(defaultStream, customStream);
        }

        [Fact]
        public async Task TextField_CustomFontFamily_EmbedsARealFontProgram()
        {
            // Proof this went through PeachPDF's real font pipeline rather than the fixed Standard-14
            // Helvetica the feature originally always used - an embedded TrueType/OpenType font
            // carries its own /FontFile2 (or /FontFile3 for CFF/OpenType-CFF) program in the PDF.
            var pdfText = await RenderToPdfText(
                "<input name='n' value='hi' style=\"font: italic 12pt Georgia, serif\" />", compress: false);

            Assert.Contains("/FontFile2", pdfText);
        }

        [Fact]
        public async Task TextField_CustomFontFamily_StillUsesHelveticaForTheLiveEditDefaultAppearance()
        {
            // Deliberate scope boundary (see docs/html-css-support.md's "Field appearance and
            // styling" section): "/DA" governs a reader's own live-edit re-render and always uses the
            // PDF standard Helvetica font, regardless of the field's own font-family - an embedded,
            // subsetted font would have no glyph ready for a character typed after generation.
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, EnableInteractivePdfForms = true };
            var result = await new PdfGenerator().GeneratePdf(
                "<html><body><input name='n' value='hi' style=\"font: italic 12pt Georgia, serif\" /></body></html>", config);

            var field = Assert.IsType<PdfTextField>(Assert.Single(Fields(result.PdfDocument)));

            Assert.Contains("/Helv", field.DefaultAppearance);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        static async Task<string> BuildWidgetAppearanceStream(string inputHtml)
        {
            var pdfText = await RenderToPdfText(inputHtml, compress: false);
            var header = "% PeachPDF.PdfSharpCore.Pdf.Advanced.PdfFormXObject";
            var headerIndex = pdfText.IndexOf(header, System.StringComparison.Ordinal);
            Assert.True(headerIndex >= 0, "No widget appearance PdfFormXObject found.");

            var streamIndex = pdfText.IndexOf("stream", headerIndex, System.StringComparison.Ordinal);
            var endIndex = pdfText.IndexOf("endstream", streamIndex, System.StringComparison.Ordinal);
            return pdfText.Substring(streamIndex, endIndex - streamIndex);
        }

        static async Task<string> RenderToPdfText(string bodyHtml, bool compress)
        {
            var html = $"<!DOCTYPE html><html><body>{bodyHtml}</body></html>";
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                CompressContentStreams = compress,
                EnableInteractivePdfForms = true,
            };

            var doc = await new PdfGenerator().GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        static System.Collections.Generic.List<PdfAcroField> Fields(PdfDocument document)
        {
            var array = document.Catalog.AcroForm.Fields;
            var result = new System.Collections.Generic.List<PdfAcroField>();
            foreach (var item in array)
            {
                var dict = item is PdfReference iref ? iref.Value : item;
                if (dict is PdfAcroField field)
                    result.Add(field);
            }
            return result;
        }
    }
}
