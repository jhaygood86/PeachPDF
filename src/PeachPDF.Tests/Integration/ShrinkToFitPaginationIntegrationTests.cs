using PeachPDF.PdfSharpCore;
using System.Text;
using System.Text.RegularExpressions;

namespace PeachPDF.Tests.Integration
{
    // Regression for the paint-time scroll offset used for pagination: GetPaginationSlots
    // yields slot tops in layout-PIXEL space, but PdfGenerator historically assigned them
    // through the public HtmlContainer.ScrollOffset setter, which treats its input as POINTS
    // and multiplies by PixelsPerPoint to store pixels. Whenever ShrinkToFit actually shrank
    // content (PixelsPerPoint > 1), the offset was scaled twice and every page's content
    // painted slot × (PixelsPerPoint - 1) too high - the error compounds per page, so boxes
    // laid out flush at a later page's content top painted their glyph tops back across the
    // PREVIOUS page's bottom clip edge (visible as sliced text at page bottoms in the SVG
    // showcase). Per CSS Fragmentation Level 3 §4 / CSS2.1 §13.2, a fragment after a page
    // break starts at the top of its own page area - it must never straddle the previous one.
    public class ShrinkToFitPaginationIntegrationTests
    {
        // Three identical paragraphs, each forced onto its own page, must paint their text at
        // the SAME page-relative position on every page - that's what "starts at the top of
        // its own page area" means for identical fragments. With the double-scaled scroll
        // offset the error compounds per page (~165pt/page in this setup), so page 3's and
        // page 4's baselines drift far above page 2's (and off the page entirely).
        [Fact]
        public async Task ShrunkContent_IdenticalParagraphsOnSuccessivePages_PaintAtTheSamePageRelativePosition()
        {
            // The 700pt-wide box exceeds A4's 555pt content width, forcing ShrinkToFit to
            // actually shrink (PixelsPerPoint ≈ 1.26); each section then starts a new page.
            const string html = @"<!DOCTYPE html>
<html>
<body style='margin: 0'>
<div style='width: 700pt; height: 10px'>wide</div>
<p>first page word</p>
<p style='break-before: page'>second page word</p>
<p style='break-before: page'>third page word</p>
<p style='break-before: page'>fourth page word</p>
</body>
</html>";

            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                ShrinkToFit = true,
                CompressContentStreams = false
            };

            var doc = await generator.GeneratePdf(html, config);
            Assert.True(doc.PdfDocument.Pages.Count >= 4, $"Test setup expects 4+ pages, got {doc.PdfDocument.Pages.Count}");

            using var ms = new MemoryStream();
            doc.Save(ms);
            var pdfText = Encoding.Latin1.GetString(ms.ToArray());

            // The first Td after each BT is absolute (later Tds within the same BT are
            // relative glyph-run advances). Its y is a bottom-up baseline coordinate.
            var baselines = Regex.Matches(pdfText, @"BT\s(?:(?!ET).)*?(-?[\d.]+) (-?[\d.]+) Td", RegexOptions.Singleline)
                .Select(m => double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture))
                .ToList();

            Assert.True(baselines.Count >= 4, $"Expected a text run per page, found {baselines.Count}");

            // The last three runs are the three identical break-before: page paragraphs, one
            // per page (streams are emitted in page order). Their page-relative baselines must
            // coincide, and sit inside the page (a positive top-down coordinate).
            var pageParagraphBaselines = baselines.TakeLast(3).ToList();
            var reference = pageParagraphBaselines[0];

            foreach (var baselineY in pageParagraphBaselines)
            {
                Assert.True(Math.Abs(baselineY - reference) < 0.5,
                    $"Identical paragraphs on successive pages paint at different page-relative baselines " +
                    $"({reference:F2} vs {baselineY:F2}) - the pagination scroll offset must not be scaled by PixelsPerPoint twice.");
                Assert.True(baselineY is > 0 and < 842,
                    $"A page's text baseline (bottom-up y={baselineY:F2}) paints outside the page box entirely.");
            }
        }
    }
}
