using PeachPDF.Tests.TestSupport;
using PeachPDF;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.Advanced;
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
    /// <see cref="Html.PageGeometryTable.ResolveForMaterializedPage"/>, which re-resolves a
    /// page's margins against its materialized number whenever that wouldn't change the content-box
    /// dimensions layout already used (the ordinary mirrored binding-gutter case) - both fixtures here
    /// use the same content-empty-gap shape (<c>&lt;div style='height: 1500pt'&gt;</c>, following
    /// <c>HandleLinksPaginationTests</c>' own convention) to land the second paragraph on grid slot 2
    /// (grid page 3, odd - <c>:right</c>) which is actually materialized page 2 (even - <c>:left</c>).
    ///
    /// Also covers issue #1041's narrow, relayout-gated extension for the case
    /// <c>ResolveForMaterializedPage</c> alone still always declines - a page-side override that
    /// itself changes the content-box WIDTH or HEIGHT (not both) - via
    /// <see cref="Html.HtmlContainerInt.TryApplyDimensionChangingPageCorrection"/>. See
    /// <c>.claude/accepted-gaps/left-right-page-geometry-vs-materialized-numbering.md</c> for what
    /// still stays out of scope (both dimensions changing at once; a slot under an active named page).
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
        public async Task AsymmetricLeftRightMargins_WidthOnlyChanges_CorrectsViaOneExtraRelayoutPass()
        {
            // :left's total margin (200pt) genuinely differs from :right's (20pt) - substituting the
            // materialized side's margin here changes the page's own content-box WIDTH, which
            // PageGeometryTable.ResolveForMaterializedPage alone can never safely bake in after the
            // fact (content was already wrapped against the grid-numbered width). Issue #1041's
            // narrow, relayout-gated correction covers exactly this case, though: only WIDTH changes
            // here (both rules leave margin-top/bottom at the base 60pt), so
            // HtmlContainerInt.TryApplyDimensionChangingPageCorrection re-runs layout once more
            // against the corrected width, and - because the tiny paragraph here fits on one line
            // either way, so the page count and box-to-page assignment don't change - the correction
            // validates and applies: the second (materialized) page must be translated by :left's
            // margin-left (100pt), not grid slot 2's own rule (grid page 3, odd -> :right, 10pt).
            var doc = await GeneratePdf(
                "@page :left { margin-left: 100pt; margin-right: 100pt; }",
                "@page :right { margin-left: 10pt; margin-right: 10pt; }");

            Assert.Equal(2, doc.PageCount);

            var deltaXPage1 = GetLeadingDeltaX(doc, 1);

            Assert.Equal(100 - BaseMarginLeft, deltaXPage1, 3);
        }

        [Fact]
        public async Task AsymmetricLeftRightMarginsAndSize_BothDimensionsChange_KeepsTheGridNumberedMargin()
        {
            // The genuine residual issue #1041 still leaves open (see the accepted-gap file): :left
            // here changes BOTH the width (asymmetric margin, as above) AND the height (a `size`
            // override :right doesn't share) relative to :right. Nothing in the narrow fix can tell
            // "a relayout would fix the width" apart from "...and also the height" with the same
            // fallback-safety guarantee a single-dimension change gets, so both changing at once
            // stays declined exactly as before this issue's fix: the grid-numbered (:right) margin is
            // kept, unaffected by the materialized number.
            var doc = await GeneratePdf(
                "@page :left { margin-left: 100pt; margin-right: 100pt; size: 400pt 400pt; }",
                "@page :right { margin-left: 10pt; margin-right: 10pt; }");

            Assert.Equal(2, doc.PageCount);

            var deltaXPage1 = GetLeadingDeltaX(doc, 1);

            // Grid slot 2 / grid page 3 is :right (margin-left 10) - still kept, even though the
            // materialized page number (2) would otherwise select :left.
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
            Assert.Single(doc.PageAnnotations(1));

            // The link sits flush at the page's own content-left edge, so its rect's X1 is exactly
            // this page's resolved margin-left - :left's 100pt, not :right's 40pt.
            var rect = doc.PageAnnotations(1)[0].RectOf();
            Assert.Equal(100, rect.Left, 1);
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

            var outline = Assert.Single(doc.Inspect().Outlines);
            Assert.Equal("heading after the gap", outline.Title);
            Assert.Equal(1, outline.DestinationPageIndex);

            // PDF Y counts from the page bottom. Both preceding blocks have explicit, deterministic
            // heights (12pt + 1500pt), so the heading's own top position is pinned regardless of font
            // metrics: pageHeight - outline.Top must be :left's 90pt margin plus that fixed content
            // offset (158pt total) - never anywhere near :right's 30pt (what the pre-fix grid-numbered
            // margin would have produced instead, 98pt total).
            var file = doc.Inspect();
            var topFromPageTop = PdfFile.Height(file.Pages[outline.DestinationPageIndex]) - outline.DestinationTop!.Value;
            Assert.Equal(158, topFromPageTop, 1);
        }

        private static async Task<PdfFile> GeneratePdf(string leftRule, string rightRule)
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
            return doc.Inspect();
        }

        private static double GetLeadingDeltaX(PdfFile doc, int pageIndex)
        {
            foreach (var stream in doc.ContentStreams(doc.Pages[pageIndex]))
            {
                if (stream.Decoded.Length == 0) continue;

                var match = LeadingTranslate.Match(stream.DecodedText);
                if (match.Success)
                    return double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            }

            Assert.Fail($"No page-margin translate found on page {pageIndex}.");
            return double.NaN;
        }
    }
}
