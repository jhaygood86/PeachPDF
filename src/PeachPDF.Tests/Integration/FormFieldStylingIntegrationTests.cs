using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.AcroForms;
using PeachPDF.PdfSharpCore.Pdf.Advanced;
using PeachPDF.PdfSharpCore.Pdf.Annotations;

namespace PeachPDF.Tests.Integration
{
    // Covers the interactive-forms follow-up: a field's border/background/padding/color/font are
    // real, author-overridable CSS (FormFieldAppearanceBuilder/FormFieldChrome), not the hardcoded
    // black-border/white-background/Helvetica-only look the feature originally shipped with. Per
    // CLAUDE.md's testing conventions, these assert structurally (stream length/byte comparison, or
    // a specific PDF key's presence) rather than on decoded glyph text or literal color-operator
    // bytes - see InteractivePdfFormsPaintOrderTests's own remarks for why real font embedding makes
    // literal-text assertions unreliable here. Reads the appearance stream bytes directly off the
    // live PdfDocument object graph (PdfFormXObject.Stream.Value) rather than round-tripping through
    // Save()+re-parsing the saved file's own human-readable "% TypeName" object comments - those
    // comments are a PdfWriterLayout.Verbose debug aid, not a stable, cross-platform-proven API, and
    // string-searching for them was observed to be unreliable on Linux/macOS CI.
    public class FormFieldStylingIntegrationTests
    {
        [Fact]
        public async Task TextField_UnstyledInput_GetsDefaultBorderAndBackgroundFromUaStylesheet()
        {
            // Backward-compatibility check: an author who sets no border/background at all must still
            // see a field, not a blank box - PeachPDF's UA stylesheet supplies a plain default
            // (CssDefaults.DefaultStyleSheet's "input, select" rule) the same way every field looked
            // before per-field CSS styling existed.
            var withDefault = await BuildWidgetAppearanceStreamBytes("<input name='n' value='hi' />");
            var withNoChrome = await BuildWidgetAppearanceStreamBytes(
                "<input name='n' value='hi' style=\"border:none;background-color:transparent;padding:0\" />");

            Assert.True(withDefault.Length > withNoChrome.Length,
                "The UA default border/background must draw strictly more content than an author explicitly suppressing both.");
        }

        [Fact]
        public async Task TextField_CustomBorderColor_BakesADifferentAppearanceStreamThanDefault()
        {
            var defaultStream = await BuildWidgetAppearanceStreamBytes("<input name='n' value='hi' />");
            var customStream = await BuildWidgetAppearanceStreamBytes("<input name='n' value='hi' style=\"border-color:#8a2be2\" />");

            Assert.NotEqual(defaultStream, customStream);
        }

        [Fact]
        public async Task TextField_CustomBackgroundColor_BakesADifferentAppearanceStreamThanDefault()
        {
            var defaultStream = await BuildWidgetAppearanceStreamBytes("<input name='n' value='hi' />");
            var customStream = await BuildWidgetAppearanceStreamBytes("<input name='n' value='hi' style=\"background-color:#f5e9ff\" />");

            Assert.NotEqual(defaultStream, customStream);
        }

        [Fact]
        public async Task TextField_CustomPadding_MovesTheBakedTextPosition()
        {
            var smallPadding = await BuildWidgetAppearanceStreamBytes("<input name='n' value='hi' style=\"padding:1pt 2pt\" />");
            var largePadding = await BuildWidgetAppearanceStreamBytes("<input name='n' value='hi' style=\"padding:1pt 20pt\" />");

            Assert.NotEqual(smallPadding, largePadding);
        }

        [Fact]
        public async Task Checkbox_CustomColor_ChangesTheCheckMarkAppearanceStream()
        {
            var defaultStream = await BuildWidgetAppearanceStreamBytes("<input type='checkbox' name='c' checked />");
            var customStream = await BuildWidgetAppearanceStreamBytes("<input type='checkbox' name='c' checked style=\"color:red\" />");

            Assert.NotEqual(defaultStream, customStream);
        }

        [Fact]
        public async Task TextField_CustomFontFamily_EmbedsARealFontProgram()
        {
            // Proof this went through PeachPDF's real font pipeline rather than the fixed Standard-14
            // Helvetica the feature originally always used - an embedded TrueType/OpenType font
            // carries its own /FontFile2 (or /FontFile3 for CFF/OpenType-CFF) program in the PDF,
            // referenced from the appearance's own /Resources /Font entry. Font subsetting/embedding
            // is a deferred PrepareForSave step (unlike the appearance's own drawing content, already
            // populated once GeneratePdf returns) - Save() has to actually run once to populate it,
            // but the assertion below still reads the resulting object graph directly rather than
            // re-parsing the saved bytes.
            var document = await RenderAsync("<input name='n' value='hi' style=\"font: italic 12pt Georgia, serif\" />");
            document.Save(Stream.Null);

            var appearance = ResolveNormalAppearance(Assert.Single(Fields(document)));
            var fontDict = appearance.Elements.GetDictionary(PdfFormXObject.Keys.Resources).Elements.GetDictionary("/Font");
            var embeddedFont = Assert.Single(fontDict.Elements.Values.OfType<PdfReference>().Select(r => r.Value).OfType<PdfDictionary>());
            var descendantFonts = embeddedFont.Elements.GetArray("/DescendantFonts");
            var descendantFont = (PdfDictionary)((PdfReference)descendantFonts.Elements[0]).Value;
            var fontDescriptor = descendantFont.Elements.GetDictionary("/FontDescriptor");

            Assert.True(fontDescriptor.Elements.ContainsKey("/FontFile2") || fontDescriptor.Elements.ContainsKey("/FontFile3"),
                "A custom font-family must embed a real font program.");
        }

        [Fact]
        public async Task TextField_CustomFontFamily_StillUsesHelveticaForTheLiveEditDefaultAppearance()
        {
            // Deliberate scope boundary (see docs/html-css-support.md's "Field appearance and
            // styling" section): "/DA" governs a reader's own live-edit re-render and always uses the
            // PDF standard Helvetica font, regardless of the field's own font-family - an embedded,
            // subsetted font would have no glyph ready for a character typed after generation.
            var document = await RenderAsync("<input name='n' value='hi' style=\"font: italic 12pt Georgia, serif\" />");
            var field = Assert.IsType<PdfTextField>(Assert.Single(Fields(document)));

            Assert.Contains("/Helv", field.DefaultAppearance);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        static async Task<byte[]> BuildWidgetAppearanceStreamBytes(string inputHtml)
        {
            var document = await RenderAsync(inputHtml);
            var appearance = ResolveNormalAppearance(Assert.Single(Fields(document)));
            return appearance.Stream.Value;
        }

        /// <summary>
        /// Resolves a field's "/AP /N" appearance - either a direct reference to one
        /// <see cref="PdfFormXObject"/> (text/select fields), or, for a two-state field
        /// (checkbox/radio), the state dictionary's entry matching the field's own current
        /// <see cref="PdfAcroField.AppearanceState"/>.
        /// </summary>
        static PdfFormXObject ResolveNormalAppearance(PdfAcroField field)
        {
            var ap = field.Elements.GetDictionary(PdfAnnotation.Keys.AP);
            if (ap.Elements["/N"] is PdfReference { Value: PdfFormXObject direct })
                return direct;

            var states = ap.Elements.GetDictionary("/N");
            return (PdfFormXObject)states.Elements.GetReference(field.AppearanceState!).Value;
        }

        static async Task<PdfDocument> RenderAsync(string bodyHtml)
        {
            var html = $"<!DOCTYPE html><html><body>{bodyHtml}</body></html>";
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                // Raw, uncompressed appearance-stream bytes make the byte-comparison assertions
                // above meaningful (two different declarations produce two different operator
                // sequences) rather than comparing two independently-Flate-compressed blobs.
                CompressContentStreams = false,
                EnableInteractivePdfForms = true,
            };

            var result = await new PdfGenerator().GeneratePdf(html, config);
            return result.PdfDocument;
        }

        static List<PdfAcroField> Fields(PdfDocument document)
        {
            var array = document.Catalog.AcroForm.Fields;
            var result = new List<PdfAcroField>();
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
