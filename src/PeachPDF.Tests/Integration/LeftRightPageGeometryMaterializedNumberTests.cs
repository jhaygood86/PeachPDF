using PeachPDF;
using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.Advanced;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end regression coverage for issue #148: a content-empty gap skipped earlier in the
    /// document (CSS Paged Media 3 §3.2) can leave a later kept page's raw grid slot number and its
    /// final materialized page number disagreeing on <c>:left</c>/<c>:right</c> parity, so the page's
    /// own margins (resolved by <c>PageGeometryTable</c> from the GRID number during layout) used to
    /// disagree with the materialized-numbered margin-box content painted into them
    /// (<c>PdfGenerator.cs</c>'s page loop, already materialized-numbered). Fixed by
    /// <see cref="Html.Core.PageGeometryTable.ResolveForMaterializedPage"/>, which re-resolves a
    /// page's margins against its materialized number whenever that wouldn't change the content-box
    /// dimensions layout already used (the ordinary mirrored binding-gutter case) - both fixtures here
    /// use the same content-empty-gap shape (<c>&lt;div style='height: 1500pt'&gt;</c>, following
    /// <c>HandleLinksPaginationTests</c>' own convention) to land the second paragraph on grid slot 2
    /// (grid page 3, odd - <c>:right</c>) which is actually materialized page 2 (even - <c>:left</c>).
    ///
    /// Asserted structurally, per this repo's painting-test convention: each page's own content
    /// stream is read in isolation via <c>PdfPage.Contents</c> (not a whole-file regex, which would be
    /// ambiguous across pages), and the leading `1 -0 -0 1 dx dy cm` page-margin translate
    /// (<c>PdfGenerator.cs</c>'s <c>deltaX</c>/<c>deltaY</c>) is extracted and compared against the
    /// margin the winning rule should have produced.
    /// </summary>
    public class LeftRightPageGeometryMaterializedNumberTests
    {
        private const double BaseMarginLeft = 50; // @page { margin: 60pt 50pt; }

        private static readonly Regex LeadingTranslate = new(@"1 -?0 -?0 1 (-?[\d.]+) (-?[\d.]+) cm", RegexOptions.Compiled);

        [Fact]
        public async Task MirroredLeftRightMargins_SecondPageUsesTheMaterializedSidesMargin()
        {
            // :left/:right have equal total left+right margin (140pt), only the split differs - the
            // substitution is dimensionally safe, so the fix applies: the second page (materialized
            // page 2, even -> :left) must be translated by :left's margin-left (100pt), NOT grid slot
            // 2's own rule (grid page 3, odd -> :right, margin-left 40pt).
            var doc = await GeneratePdf(
                "@page :left { margin-left: 100pt; margin-right: 40pt; }",
                "@page :right { margin-left: 40pt; margin-right: 100pt; }");

            Assert.Equal(2, doc.PageCount);

            var deltaXPage0 = GetLeadingDeltaX(doc, 0);
            var deltaXPage1 = GetLeadingDeltaX(doc, 1);

            // Page 0 (materialized page 1, slot 0, grid page 1 - both agree it's :right): unaffected.
            Assert.Equal(40 - BaseMarginLeft, deltaXPage0, 3);

            // Page 1 (materialized page 2, but grid slot 2 / grid page 3): must reflect :left's
            // margin-left (100), not :right's (40) - the materialized-correct side.
            Assert.Equal(100 - BaseMarginLeft, deltaXPage1, 3);
        }

        [Fact]
        public async Task AsymmetricLeftRightMargins_UnsafeToSubstitute_KeepsTheGridNumberedMargin()
        {
            // :left's total margin (200pt) genuinely differs from :right's (20pt) - substituting the
            // materialized side's margin here would paint a page whose content-box width disagrees
            // with what its content was actually wrapped against, so ResolveForMaterializedPage
            // declines (returns null) and the existing (accepted-gap, grid-numbered) behavior is
            // unchanged: this is a regression guard on the fallback path, not a fix.
            var doc = await GeneratePdf(
                "@page :left { margin-left: 100pt; margin-right: 100pt; }",
                "@page :right { margin-left: 10pt; margin-right: 10pt; }");

            Assert.Equal(2, doc.PageCount);

            var deltaXPage1 = GetLeadingDeltaX(doc, 1);

            // Grid slot 2 / grid page 3 is :right (margin-left 10) - the fallback keeps that, even
            // though the materialized page number (2) would otherwise select :left.
            Assert.Equal(10 - BaseMarginLeft, deltaXPage1, 3);
        }

        [Fact]
        public async Task MirroredLeftRightMargins_LinkOnSecondPageUsesTheMaterializedSidesMargin()
        {
            // The same parity flip as above, but for PdfGenerator.HandleLinks' own rect-to-page
            // resolution: a link on the second (materialized) page must be positioned using :left's
            // margin-left (100pt), matching what the page loop now paints - not grid slot 2's own
            // :right rule (40pt), which would place the link's clickable rect in the wrong spot
            // relative to the visible content.
            const string html = """
                <!DOCTYPE html><html><head><style>
                body { margin: 0; }
                p { margin: 0; }
                @page { margin: 60pt 50pt; }
                @page :left { margin-left: 100pt; margin-right: 40pt; }
                @page :right { margin-left: 40pt; margin-right: 100pt; }
                </style></head><body>
                <p>page one content</p>
                <div style='height: 1500pt'></div>
                <p><a href="https://example.com/">a link after the gap</a></p>
                </body></html>
                """;

            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4 };
            var doc = await generator.GeneratePdf(html, config);

            Assert.Equal(2, doc.PageCount);
            Assert.Equal(1, doc.Pages[1].Annotations.Count);

            // The link sits flush at the page's own content-left edge, so its rect's X1 is exactly
            // this page's resolved margin-left - :left's 100pt, not :right's 40pt.
            var rect = doc.Pages[1].Annotations[0].Rectangle;
            Assert.Equal(100, rect.X1, 1);
        }

        [Fact]
        public async Task MirroredTopBottomMargins_BookmarkOnSecondPageUsesTheMaterializedSidesMargin()
        {
            // The vertical analogue of the link test above, exercising PageAnchorResolver.
            // ResolveRectToPage (shared by BookmarkOutlineBuilder's PDF outline destinations) rather
            // than HandleLinks' own rect math: :left/:right here mirror margin-top/margin-bottom
            // (equal 120pt total, so BandHeight is unaffected either way - dimensionally safe). The
            // heading lands on grid slot 2 (grid page 3, :right, margin-top 30pt) which is actually
            // materialized page 2 (even, :left, margin-top 90pt) - the bookmark destination must
            // reflect the materialized 90pt, not the grid-numbered 30pt.
            const string html = """
                <!DOCTYPE html><html><head><style>
                body { margin: 0; }
                p, h1 { margin: 0; }
                @page { margin: 60pt 50pt; }
                @page :left { margin-top: 90pt; margin-bottom: 30pt; }
                @page :right { margin-top: 30pt; margin-bottom: 90pt; }
                </style></head><body>
                <div style='height: 12pt; overflow: hidden'>page one content</div>
                <div style='height: 1500pt'></div>
                <h1>heading after the gap</h1>
                </body></html>
                """;

            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4 };
            var doc = await generator.GeneratePdf(html, config);

            Assert.Equal(2, doc.PageCount);

            var outline = Assert.Single(doc.PdfDocument.Outlines);
            Assert.Equal("heading after the gap", outline.Title);
            Assert.Same(doc.Pages[1], outline.DestinationPage);

            // PDF Y counts from the page bottom. Both preceding blocks have explicit, deterministic
            // heights (12pt + 1500pt), so the heading's own top position is pinned regardless of font
            // metrics: pageHeight - outline.Top must be :left's 90pt margin plus that fixed content
            // offset (158pt total) - never anywhere near :right's 30pt (what the pre-fix grid-numbered
            // margin would have produced instead, 98pt total).
            var topFromPageTop = outline.DestinationPage!.Height - outline.Top;
            Assert.Equal(158, (double)topFromPageTop, 1);
        }

        private static async Task<PdfDocument> GeneratePdf(string leftRule, string rightRule)
        {
            var html = $$"""
                <!DOCTYPE html><html><head><style>
                body { margin: 0; }
                p { margin: 0; }
                @page { margin: 60pt 50pt; }
                {{leftRule}}
                {{rightRule}}
                </style></head><body>
                <p>page one content</p>
                <div style='height: 1500pt'></div>
                <p>page after the gap</p>
                </body></html>
                """;

            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            var doc = await generator.GeneratePdf(html, config);
            return doc.PdfDocument;
        }

        private static double GetLeadingDeltaX(PdfDocument doc, int pageIndex)
        {
            var content = doc.Pages[pageIndex].Contents;
            foreach (var item in content.Elements)
            {
                if (item is not PdfReference { Value: PdfDictionary dict }) continue;
                if (dict.Stream?.Value is not { Length: > 0 } bytes) continue;

                var text = Encoding.Latin1.GetString(bytes);
                var match = LeadingTranslate.Match(text);
                if (match.Success)
                    return double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            }

            Assert.Fail($"No page-margin translate found on page {pageIndex}.");
            return double.NaN;
        }
    }
}
