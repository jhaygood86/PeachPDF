using PeachPDF;
using PeachPDF.PdfSharpCore;
using PeachPDF.Tests.TestSupport;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// CSS Backgrounds 3 §2.11.2 canvas background: <c>&lt;html&gt;</c>'s own background (if set) fills
    /// the whole page on every page, not just its own laid-out content rect; <c>&lt;body&gt;</c>'s is
    /// propagated to the canvas only when html has none. Verified via raw PDF content-stream inspection
    /// (uncompressed content streams, per this repo's convention for painting-affecting integration
    /// tests) rather than rasterization, since a solid-color full-page fill's exact operator sequence (<c>rg</c> color set immediately
    /// followed by a <c>0 0 W H re f</c> rectangle spanning the full page) is unambiguous and doesn't
    /// suffer from the "content-stream substring" pitfall CLAUDE.md warns about for structural features
    /// like masks/patterns/gradients - a color set + a full-page rect + fill is precisely what "the
    /// canvas was filled" means here, not merely suggestive of it.
    /// </summary>
    public class CanvasBackgroundIntegrationTests
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

        // A4 in PDF points, per PageSizeConverter - the exact numbers a full-page-canvas "re" fill must use.
        private const string FullPageRectPattern = @"595(\.\d+)? 842(\.\d+)? re\s*\nf";

        [Fact]
        public async Task BodyBackground_FillsWholePage_NotJustContentHeight()
        {
            var pdfText = await GetPdfText(
                "<!DOCTYPE html><html><head><style>body { margin: 0; background-color: rgb(255,0,0); }</style></head>" +
                "<body><p>short</p></body></html>");

            // Body's own laid-out content (one short paragraph) is nowhere near a full A4 page tall -
            // the exact confirmed gap this workstream closes.
            Assert.Matches(new Regex(@"1 0 0 rg[\s\S]{0,40}0 0 " + FullPageRectPattern), pdfText);
        }

        [Fact]
        public async Task BodyBackground_RepeatsOnEveryPage_ForMultiPageContent()
        {
            // Explicit page breaks guarantee exactly 3 pages, independent of any px-to-pt height math.
            var pdfText = await GetPdfText(
                "<!DOCTYPE html><html><head><style>body { margin: 0; background-color: rgb(0,255,0); }</style></head>" +
                "<body><p>page 1</p>" +
                "<p style='page-break-before: always'>page 2</p>" +
                "<p style='page-break-before: always'>page 3</p></body></html>");

            var fillPattern = new Regex(@"0 1 0 rg[\s\S]{0,40}0 0 " + FullPageRectPattern);
            var matches = fillPattern.Matches(pdfText);

            Assert.True(matches.Count >= 3, $"expected the canvas fill to repeat on every page (>=3), found {matches.Count}");
        }

        [Fact]
        public async Task BodyAndHtmlBothSet_HtmlWins_BodyPaintsOverItsOwnBoxOnly()
        {
            var pdfText = await GetPdfText(
                "<!DOCTYPE html><html style='background-color: rgb(0,0,255)'>" +
                "<head><style>body { margin: 0; width: 100pt; height: 50pt; background-color: rgb(255,0,0); }</style></head>" +
                "<body><p>short</p></body></html>");

            // html's blue is the canvas fill; body's red is not propagated, so it paints only over
            // body's own small box - never as a full-page fill.
            Assert.Matches(new Regex(@"0 0 1 rg[\s\S]{0,40}0 0 " + FullPageRectPattern), pdfText);
            Assert.Matches(new Regex(@"1 0 0 rg[\s\S]{0,40}100(\.\d+)? 50(\.\d+)? re\s*\nf"), pdfText);
            Assert.DoesNotMatch(new Regex(@"1 0 0 rg[\s\S]{0,40}0 0 " + FullPageRectPattern), pdfText);
        }

        [Fact]
        public async Task HtmlBackgroundImageOnly_OwnsCanvas_BodyColorNotPropagated()
        {
            // §2.11.2: body propagates only when the root's background-image is none AND its
            // background-color is transparent - an image-only root background still owns the canvas.
            var (_, container) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html style='background-image: linear-gradient(red, blue)'>" +
                "<head><style>body { background-color: rgb(0,255,0); }</style></head>" +
                "<body><p>short</p></body></html>");

            Assert.Equal("html", container.CanvasBackgroundBox?.HtmlTag?.Name);
        }

        [Fact]
        public async Task OnlyHtmlBackgroundSet_HtmlBackgroundUsedForCanvas()
        {
            var pdfText = await GetPdfText(
                "<!DOCTYPE html><html style='background-color: rgb(0,0,255)'>" +
                "<head></head><body><p>short</p></body></html>");

            Assert.Matches(new Regex(@"0 0 1 rg[\s\S]{0,40}0 0 " + FullPageRectPattern), pdfText);
        }

        [Fact]
        public async Task NeitherBodyNorHtmlBackgroundSet_NoCanvasFill()
        {
            var pdfText = await GetPdfText(
                "<!DOCTYPE html><html><head></head><body><p>short</p></body></html>");

            Assert.DoesNotMatch(new Regex(@"rg\s*\n0 0 " + FullPageRectPattern), pdfText);
        }

        [Fact]
        public async Task UnrelatedElementBackground_StillPaintedNormally_NotPromoted()
        {
            // Regression guard: SuppressOwnBackgroundPaint must never leak onto an unrelated element -
            // a <div> with its own background continues to paint at its own (non-full-page) rect.
            var pdfText = await GetPdfText(
                "<!DOCTYPE html><html><head><style>body { margin: 0 } div { width: 100pt; height: 50pt; background-color: rgb(0,255,255); }</style></head>" +
                "<body><div></div></body></html>");

            // The div's own background must still be painted, at its own small rect - not a full page.
            Assert.Matches(new Regex(@"0 1 1 rg[\s\S]{0,40}100(\.\d+)? 50(\.\d+)? re\s*\nf"), pdfText);
            Assert.DoesNotMatch(new Regex(@"0 1 1 rg[\s\S]{0,40}0 0 " + FullPageRectPattern), pdfText);
        }
    }
}
