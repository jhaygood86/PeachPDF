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
    /// <c>background-*</c> properties, one layer below the CSS2.1 §14.2 canvas (<c>&lt;html&gt;</c>/
    /// <c>&lt;body&gt;</c>) background <see cref="CanvasBackgroundIntegrationTests"/> already covers.
    /// Verified the same way that sibling file is: a solid-color fill's exact operator sequence is
    /// unambiguous in the raw (uncompressed) content stream, so a regex match is real structural proof
    /// here, not the "content-stream substring" pitfall CLAUDE.md warns about for masks/gradients/
    /// patterns.
    /// <para>
    /// Since issue #1147, the page box has its own border/padding, so its background positioning area
    /// is no longer always the full physical sheet: <c>background-origin</c>/<c>-clip</c>'s default
    /// (padding-box, mirroring <see cref="PeachPDF.Html.Core.Dom.MarginBoxRenderer"/>'s own fallback for
    /// a page-margin box's background) resolves inside this page's own MARGIN - background never
    /// extends into any box's margin, page box included - even with no border/padding declared, which is
    /// why <see cref="PageBackgroundColor_FillsInsideMargin_WhenNoBorderOrPaddingDeclared"/> (previously
    /// "FillsWholeSheet") asserts a margin-inset rect, not the full sheet, as the correct pre-#1147-gap
    /// behavior too.
    /// </para>
    /// </summary>
    public class PageBackgroundIntegrationTests
    {
        private static async Task<string> GetPdfText(string html, PageSize pageSize = PageSize.A4, int margin = 20)
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = pageSize, CompressContentStreams = false };
            config.SetMargins(margin);
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        // A4 in PDF points, per PageSizeConverter - the exact numbers a full-page-box "re" fill must use.
        private const string FullPageRectPattern = @"595(\.\d+)? 842(\.\d+)? re\s*\nf";

        // The page box's own border-box/padding-box/content-box with a 20pt margin and NO border/
        // padding declared (so all three coincide) - A4 (595x842pt) minus 20pt on every side.
        private const string MarginInsetRectPattern = @"20(\.\d+)? 20(\.\d+)? 555(\.\d+)? 802(\.\d+)? re\s*\nf";

        [Fact]
        public async Task PageBackgroundColor_FillsInsideMargin_WhenNoBorderOrPaddingDeclared()
        {
            var pdfText = await GetPdfText(
                "<!DOCTYPE html><html><head><style>@page { background-color: rgb(255,0,0); }</style></head>" +
                "<body><p>short</p></body></html>");

            Assert.Matches(new Regex(@"1 0 0 rg[\s\S]{0,40}" + MarginInsetRectPattern), pdfText);
            // Never the full sheet - background never extends into the page box's own margin, matching
            // how an ordinary element's (or a page-margin box's) background never extends into ITS
            // margin either.
            Assert.DoesNotMatch(new Regex(@"1 0 0 rg[\s\S]{0,40}0 0 " + FullPageRectPattern), pdfText);
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
            // first in the content stream. The canvas fill (CSS2.1 §14.2's html/body propagation) still
            // covers the WHOLE sheet, including the page box's own margin - unlike the page background
            // above it, it is not itself subject to background-origin/-clip.
            var pdfText = await GetPdfText(
                "<!DOCTYPE html><html><head><style>" +
                "@page { background-color: rgb(255,0,0); }" +
                "body { margin: 0; background-color: rgb(0,255,0); }" +
                "</style></head><body><p>short</p></body></html>");

            var pageFillMatch = new Regex(@"1 0 0 rg[\s\S]{0,40}" + MarginInsetRectPattern).Match(pdfText);
            var canvasFillMatch = new Regex(@"0 1 0 rg[\s\S]{0,40}0 0 " + FullPageRectPattern).Match(pdfText);

            Assert.True(pageFillMatch.Success, "expected the @page background fill to be present");
            Assert.True(canvasFillMatch.Success, "expected the canvas (body) background fill to be present");
            Assert.True(pageFillMatch.Index < canvasFillMatch.Index,
                "expected the @page background to be painted before (beneath) the canvas background");
        }

        [Fact]
        public async Task PageBackgroundOrigin_BorderBoxVsContentBox_ProduceDistinctRects()
        {
            // Issue #1147's actual fix: background-origin/-clip now genuinely distinguish border-box/
            // padding-box/content-box against the page's own resolved border+padding, rather than all
            // three resolving identically (the pre-#1147 gap). No margin declared here (margin: 0) so
            // the only inset in play is the page box's own 10pt border and 15pt padding.
            var pdfText = await GetPdfText(
                "<!DOCTYPE html><html><head><style>" +
                "@page { margin: 0; border: 10pt solid black; padding: 15pt; " +
                "background-color: rgb(255,0,0); background-clip: content-box; }" +
                "</style></head><body><p>short</p></body></html>", margin: 0);

            // content-box: inset by border(10) + padding(15) = 25pt on every side of A4 (595x842).
            Assert.Matches(new Regex(@"1 0 0 rg[\s\S]{0,40}25(\.\d+)? 25(\.\d+)? 545(\.\d+)? 792(\.\d+)? re\s*\nf"), pdfText);
        }

        [Fact]
        public async Task PageBackgroundOrigin_BorderBox_InsetsOnlyByMargin()
        {
            var pdfText = await GetPdfText(
                "<!DOCTYPE html><html><head><style>" +
                "@page { margin: 0; border: 10pt solid black; padding: 15pt; " +
                "background-color: rgb(255,0,0); background-clip: border-box; }" +
                "</style></head><body><p>short</p></body></html>", margin: 0);

            // border-box: the full sheet, since margin is 0 here and border-box sits OUTSIDE border/padding.
            Assert.Matches(new Regex(@"1 0 0 rg[\s\S]{0,40}0 0 " + FullPageRectPattern), pdfText);
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

            var redFillCount = new Regex(@"1 0 0 rg[\s\S]{0,40}" + MarginInsetRectPattern).Matches(pdfText).Count;
            var blueFillCount = new Regex(@"0 0 1 rg[\s\S]{0,40}" + MarginInsetRectPattern).Matches(pdfText).Count;

            Assert.Equal(1, redFillCount);
            Assert.Equal(1, blueFillCount);
        }
    }
}
