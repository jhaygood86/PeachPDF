using PeachPDF;
using PeachPDF.PdfSharpCore;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression tests for issue #880: <c>position: fixed</c> content positioned inside the page's own
    /// margins (e.g. a small corner watermark or badge, a realistic placement for fixed content) silently
    /// never painted into the PDF at all whenever any <c>@page</c> rule existed in the document, on every
    /// page including the first. Two independent, compounding pre-existing bugs caused this:
    /// <list type="number">
    /// <item>
    /// <b>Paint-time clip escape</b>: <c>PdfGenerator.AddPdfPages</c> used to intersect the content-area
    /// clip directly on the raw <c>XGraphics</c>, ahead of <c>RGraphics</c>'s own clip-stack bookkeeping -
    /// invisible to <c>RGraphics.SuspendClipping()</c>, the mechanism <c>FragmentPainter.PaintFragment</c>
    /// uses so a fixed box's own paint can reach back out to its true containing block (the page box,
    /// margins included, per CSS2.1 §10.1). <c>FragmentPainter.Paint</c> now pushes the content clip
    /// itself, inside a second, outer full-sheet clip <c>SuspendClipping</c> can actually reach.
    /// </item>
    /// <item>
    /// <b>Fragment-tree region exclusion</b>: <see cref="PeachPDF.Html.Core.Fragmentation.FragmentEmitter"/>'s
    /// per-slot membership test for fixed content used the page's content band (starting at
    /// <c>MarginTop</c>), so a fixed box positioned above that - inside the margin - was excluded from the
    /// materialized fragment tree entirely on every page, not merely clipped at paint time.
    /// </item>
    /// </list>
    /// Both are end-to-end, real-<see cref="PdfGenerator"/>-output bugs, so these assert on the actual
    /// rendered content stream (this repo's convention for painting changes - see
    /// <c>FixedPositionPaginationIntegrationTests</c>, whose fixture predates this issue and happens to
    /// place its own box too close to the margin boundary to distinguish the two cases), not merely on
    /// fragment-tree geometry.
    /// </summary>
    public class FixedPositionWithinPageMarginIntegrationTests
    {
        // A small, distinctively-colored fixed rect - unambiguous per this repo's structural
        // content-stream convention (color set immediately followed by its own "re f" rect, not a bare
        // substring - see CanvasBackgroundIntegrationTests).
        private const string FixedRectPattern = @"0\.0\d* 0\.19\d* 0\.3\d* rg[\s\S]{0,40}20(\.\d+)? 20(\.\d+)? re\s*\nf";

        private static string WithinMarginHtml(string extraPages) =>
            "<!DOCTYPE html><html><head><style>"
            + "@page { margin: 40pt; }"
            + "body, div, p { margin: 0; }"
            + ".fixedBox { position: fixed; top: 5pt; left: 5pt; width: 20pt; height: 20pt; background: rgb(12,50,80); }"
            + "</style></head><body>"
            + "<div class='fixedBox'></div>"
            + "<p>page 1</p>"
            + extraPages
            + "</body></html>";

        [Fact]
        public async Task FixedBoxInsidePageMargin_PaintsOnFirstPage_WithAPageRuleInEffect()
        {
            // The literal #880 repro: a single-page document with an ordinary @page rule (no size or
            // margin override at all) and a fixed box positioned above the content band's own top -
            // pre-fix, this never appeared in the output at all, on the very first (and only) page.
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            var doc = await generator.GeneratePdf(WithinMarginHtml(""), config);

            Assert.Equal(1, doc.PageCount);

            var ms = new MemoryStream();
            doc.Save(ms);
            var pdfText = Encoding.Latin1.GetString(ms.ToArray());

            Assert.Matches(new Regex(FixedRectPattern), pdfText);
        }

        [Fact]
        public async Task FixedBoxInsidePageMargin_RepeatsOnEveryPage_NotJustTheFirst()
        {
            // The second, compounding #880 root cause only shows up past page 1: a naive fix for the
            // paint-clip half alone left the fragment-tree membership test keyed to each slot's own
            // cumulative document-space position, which (by construction) only ever matches slot 0 - so
            // the box would appear to work on a single-page document and silently vanish from every
            // subsequent page of a multi-page one.
            var extraPages = "<p style='page-break-before: always'>page 2</p>"
                + "<p style='page-break-before: always'>page 3</p>";

            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            var doc = await generator.GeneratePdf(WithinMarginHtml(extraPages), config);

            Assert.Equal(3, doc.PageCount);

            var ms = new MemoryStream();
            doc.Save(ms);
            var pdfText = Encoding.Latin1.GetString(ms.ToArray());

            var matches = new Regex(FixedRectPattern).Matches(pdfText);

            Assert.True(matches.Count >= 3,
                $"expected the fixed-position box (positioned inside the page margin) to repeat on every one of the 3 real pages, found {matches.Count}");
        }
    }
}
