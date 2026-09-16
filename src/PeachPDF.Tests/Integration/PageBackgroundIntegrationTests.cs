using PeachPDF;
using PeachPDF.PdfSharpCore;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// css-page-3 §3.1's page-box background (issue #1082): the <c>@page</c> box's own
    /// <c>background-*</c> properties, painted across the full physical sheet, one layer below the
    /// CSS2.1 §14.2 canvas (<c>&lt;html&gt;</c>/<c>&lt;body&gt;</c>) background <see cref="CanvasBackgroundIntegrationTests"/>
    /// already covers. Verified the same way that sibling file is: a solid-color full-page fill's exact
    /// operator sequence is unambiguous in the raw (uncompressed) content stream, so a regex match is
    /// real structural proof here, not the "content-stream substring" pitfall CLAUDE.md warns about for
    /// masks/gradients/patterns.
    /// </summary>
    public class PageBackgroundIntegrationTests
    {
        private static async Task<string> GetPdfText(string html, PageSize pageSize = PageSize.A4)
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = pageSize, CompressContentStreams = false };
            config.SetMargins(20);
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        // A4 in PDF points, per PageSizeConverter - the exact numbers a full-page-box "re" fill must use.
        private const string FullPageRectPattern = @"595(\.\d+)? 842(\.\d+)? re\s*\nf";

        [Fact]
        public async Task PageBackgroundColor_FillsWholeSheet_WhenNoCanvasBackground()
        {
            var pdfText = await GetPdfText(
                "<!DOCTYPE html><html><head><style>@page { background-color: rgb(255,0,0); }</style></head>" +
                "<body><p>short</p></body></html>");

            Assert.Matches(new Regex(@"1 0 0 rg[\s\S]{0,40}0 0 " + FullPageRectPattern), pdfText);
        }

        [Fact]
        public async Task NoPageBackgroundDeclared_NoPageBackgroundFill()
        {
            var pdfText = await GetPdfText(
                "<!DOCTYPE html><html><head></head><body><p>short</p></body></html>");

            Assert.DoesNotMatch(new Regex(@"rg\s*\n0 0 " + FullPageRectPattern), pdfText);
        }

        [Fact]
        public async Task PageBackground_PaintsBeforeCanvasBackground_SoAnOpaqueCanvasOccludesIt()
        {
            // css-page-3 §3.1's paint order is page background, THEN document canvas - an opaque canvas
            // fill covers the page background beneath it. Both fills are still emitted (this is a paint-
            // order library, not a visibility-culling one), so the real assertion is which one comes
            // first in the content stream.
            var pdfText = await GetPdfText(
                "<!DOCTYPE html><html><head><style>" +
                "@page { background-color: rgb(255,0,0); }" +
                "body { margin: 0; background-color: rgb(0,255,0); }" +
                "</style></head><body><p>short</p></body></html>");

            var pageFillMatch = new Regex(@"1 0 0 rg[\s\S]{0,40}0 0 " + FullPageRectPattern).Match(pdfText);
            var canvasFillMatch = new Regex(@"0 1 0 rg[\s\S]{0,40}0 0 " + FullPageRectPattern).Match(pdfText);

            Assert.True(pageFillMatch.Success, "expected the @page background fill to be present");
            Assert.True(canvasFillMatch.Success, "expected the canvas (body) background fill to be present");
            Assert.True(pageFillMatch.Index < canvasFillMatch.Index,
                "expected the @page background to be painted before (beneath) the canvas background");
        }

        [Fact]
        public async Task PageBackgroundImage_PaintsAGradientLayer_AcrossTheWholeSheet()
        {
            // Exercises the image/gradient-layer path (as opposed to background-color) - LayeredBackgroundPainter
            // parses, loads and paints it via the same CssImagePainter pipeline background-image already uses
            // for ordinary elements. The showcase (paged_media_page_and_margin_box_background) rasterizes this
            // same shape through both PDFium and MuPDF per CLAUDE.md's paint-verification convention; this is
            // the fast, structural regression guard for CI.
            var pdfText = await GetPdfText(
                "<!DOCTYPE html><html><head><style>" +
                "@page { background-image: linear-gradient(to bottom, red, blue); background-size: cover; }" +
                "</style></head><body><p>short</p></body></html>");

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task PageFirstBackground_OverridesBaseRule_OnlyOnPageOne()
        {
            var pdfText = await GetPdfText(
                "<!DOCTYPE html><html><head><style>" +
                "@page { background-color: rgb(0,0,255); }" +
                "@page :first { background-color: rgb(255,0,0); }" +
                "</style></head><body><p>page 1</p>" +
                "<p style='page-break-before: always'>page 2</p></body></html>");

            var redFillCount = new Regex(@"1 0 0 rg[\s\S]{0,40}0 0 " + FullPageRectPattern).Matches(pdfText).Count;
            var blueFillCount = new Regex(@"0 0 1 rg[\s\S]{0,40}0 0 " + FullPageRectPattern).Matches(pdfText).Count;

            Assert.Equal(1, redFillCount);
            Assert.Equal(1, blueFillCount);
        }
    }
}
