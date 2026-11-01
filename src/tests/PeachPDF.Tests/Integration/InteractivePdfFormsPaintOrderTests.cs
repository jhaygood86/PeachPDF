using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    // Painting-change coverage per CLAUDE.md: a passing content-stream-substring test is not proof of
    // correct rendering on its own (see CLAUDE.md's testing conventions), so the assertions here are
    // deliberately narrow - the off-by-default regression (no extra drawing at all when the flag is
    // off) and a coarse "the flag-on static look draws something extra" sanity check. The structural
    // correctness that actually matters (checked vs. unchecked /AS, radio group /Kids, select /Opt,
    // real positive widget /Rect geometry) is covered by InteractivePdfFormsAcroFormTests's assertions
    // against the real PdfDocument object graph, not by parsing content-stream text here.
    public class InteractivePdfFormsPaintOrderTests
    {
        [Fact]
        public async Task Select_FlagOff_DoesNotDrawOptionTextOntoThePage()
        {
            // Confirms CssBoxFormField.MeasureWordsSize's own doc comment: an <option>'s text is
            // never measured (the "replaced element" shortcut skips the base recursion into
            // children), so it never reaches a paintable fragment, whether or not interactive forms
            // are enabled - this is a layout-level fact, not something the flag gates.
            var pdfText = await RenderToPdfText("<select><option>Alpha</option><option>Bravo</option></select>", enableForms: false);

            Assert.DoesNotContain("Alpha", pdfText);
            Assert.DoesNotContain("Bravo", pdfText);
        }

        [Fact]
        public async Task EnableInteractivePdfForms_False_ProducesNoExtraPageContent()
        {
            // The single most important regression test for "off by default": the same HTML must
            // produce byte-for-byte identical page content whether or not FormFieldFragmentPainter
            // exists in the codebase - i.e. the flag being off must never reach it at all.
            const string html = "<input name='n' value='hi' /><input type='checkbox' checked /><input type='radio' checked /><select><option>A</option></select>";

            var withFlagOff = await RenderToPdfText(html, enableForms: false);

            Assert.DoesNotContain("/AcroForm", withFlagOff);
            // No field value text exists anywhere in the document when the flag is off - there's no
            // AcroForm widget appearance stream to bake it into, and (see FormFieldFragmentPainter's
            // own remarks) the static page-content painter deliberately never draws field value text
            // itself either, even when the flag is on.
            Assert.DoesNotContain("hi", withFlagOff);
        }

        [Fact]
        public async Task EnableInteractivePdfForms_True_ProducesMoreContentThanFlagOff()
        {
            const string html = "<input name='n' value='hi' /><input type='checkbox' checked /><input type='radio' checked /><select><option value='r'>Red</option></select>";

            var withFlagOff = await RenderToPdfText(html, enableForms: false);
            var withFlagOn = await RenderToPdfText(html, enableForms: true);

            Assert.True(withFlagOn.Length > withFlagOff.Length, "Enabling interactive forms must add real content (AcroForm objects, appearance streams, static chrome).");
            Assert.Contains("/AcroForm", withFlagOn);
        }

        [Fact]
        public async Task TextField_FlagOn_TextDrawsOnlyInTheWidgetAppearanceStream_NotThePageContentStream()
        {
            // The regression this guards against: the value used to be drawn a second time directly
            // into the page's own content stream (PdfContent), which - composited underneath the
            // widget's independent PdfFormXObject appearance stream - showed as visibly doubled,
            // offset text in a real reader the moment the field was focused for editing (a real
            // reader's own live-edit rendering doesn't necessarily re-cover page content first). See
            // FormFieldFragmentPainter's class remarks. Asserted structurally (a text-showing
            // operator's presence/absence), not via a literal substring: FormFieldAppearanceBuilder
            // draws through the field's own real, possibly embedded font, which shows text as
            // encoded glyph IDs rather than literal ASCII bytes - "PeachProbe" is never itself a
            // substring of the stream that draws it (see the next test for a value-sensitivity check
            // that doesn't rely on decoding those glyph IDs either). Reads bytes directly off the
            // live PdfDocument object graph rather than round-tripping through Save() and re-parsing
            // - see FormFieldStylingIntegrationTests's own remarks on why.
            var document = await RenderAsync("<input name='n' value='PeachProbe' />", enableForms: true);

            var pageContentBytes = GetPageContentBytes(document);
            var widgetAppearanceBytes = ResolveNormalAppearance(Assert.Single(Fields(document))).Decoded;

            var pageContentText = Encoding.Latin1.GetString(pageContentBytes);
            var widgetAppearanceText = Encoding.Latin1.GetString(widgetAppearanceBytes);

            Assert.DoesNotContain(" Tj", pageContentText);
            Assert.Contains(" Tj", widgetAppearanceText);
        }

        [Fact]
        public async Task TextField_FlagOn_DifferentValues_BakeDifferentWidgetAppearanceStreams()
        {
            // Structural proof that the field's real value drives the baked appearance (rather than,
            // say, always drawing the same placeholder run) - the two streams must differ, without
            // asserting on their literal bytes (see the previous test for why: real font embedding
            // shows text as encoded glyph IDs, not the source characters).
            var documentA = await RenderAsync("<input name='n' value='PeachProbe' />", enableForms: true);
            var documentB = await RenderAsync("<input name='n' value='OtherValue' />", enableForms: true);

            var streamA = ResolveNormalAppearance(Assert.Single(Fields(documentA))).Decoded;
            var streamB = ResolveNormalAppearance(Assert.Single(Fields(documentB))).Decoded;

            Assert.NotEqual(streamA, streamB);
        }

        [Fact]
        public async Task Select_FlagOn_DifferentSelectedOptions_BakeDifferentWidgetAppearanceStreams()
        {
            // Structural proof that the selected option's label drives the baked appearance - see
            // TextField_FlagOn_DifferentValues_BakeDifferentWidgetAppearanceStreams's own remarks for
            // why this compares whole streams rather than asserting on decoded glyph text.
            var documentRed = await RenderAsync(
                "<select><option value='r' selected>Red</option><option value='g'>Green</option></select>", enableForms: true);
            var documentGreen = await RenderAsync(
                "<select><option value='r'>Red</option><option value='g' selected>Green</option></select>", enableForms: true);

            var streamRed = ResolveNormalAppearance(Assert.Single(Fields(documentRed))).Decoded;
            var streamGreen = ResolveNormalAppearance(Assert.Single(Fields(documentGreen))).Decoded;

            Assert.NotEqual(streamRed, streamGreen);
        }

        // ─── Object-graph helpers (see the class remarks on why these don't round-trip through Save()) ─────────────────────

        static byte[] GetPageContentBytes(PdfFile document) =>
            document.ContentStreams(document.Pages[0]).SelectMany(s => s.Decoded).ToArray();

        /// <summary>
        /// Resolves a field's "/AP /N" appearance - either a direct reference to one
        /// <see cref="PdfFormXObject"/> (text/select fields), or, for a two-state field
        /// (checkbox/radio), the state dictionary's entry matching the field's own current
        /// <see cref="PdfAcroField.AppearanceState"/>.
        /// </summary>
        static PdfStreamObject ResolveNormalAppearance(FormField field) => field.NormalAppearance!;

        static IReadOnlyList<FormField> Fields(PdfFile document) => document.FormFields;

        static async Task<PdfFile> RenderAsync(string bodyHtml, bool enableForms)
        {
            var html = $"<!DOCTYPE html><html><body>{bodyHtml}</body></html>";
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                CompressContentStreams = false,
                EnableInteractivePdfForms = enableForms,
            };

            var result = await new PdfGenerator().GeneratePdf(html, config);
            return result.Inspect();
        }

        static async Task<string> RenderToPdfText(string bodyHtml, bool enableForms)
        {
            var document = await RenderAsync(bodyHtml, enableForms);
            return Encoding.Latin1.GetString(document.Bytes);
        }
    }
}
