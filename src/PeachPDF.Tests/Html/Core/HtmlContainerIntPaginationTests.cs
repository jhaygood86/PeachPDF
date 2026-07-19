using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.PdfSharpCore.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Html.Core
{
    /// <summary>
    /// Tests for <see cref="HtmlContainerInt.GetPaginationSlots"/> - the Round 9 mechanism that skips
    /// materializing PDF pages for wholly content-empty page-slots, per CSS Paged Media Level 3 §3.2
    /// ("User agents SHOULD avoid generating a large number of content-empty pages"). Added for the
    /// real Acid2 fixture's own intentionally-huge "100em" margins on "#top"/".picture" (meant to be
    /// scrolled off-screen in a real, single-viewport browser - a mechanic a paginated PDF otherwise
    /// has no equivalent for) - see Acid2RegressionTests.FullFixture_MatchesPrinceXmlPageCount.
    /// </summary>
    public class HtmlContainerIntPaginationTests
    {
        [Fact]
        public async Task GetPaginationSlots_RealContentSeparatedByMultiPageGap_SkipsWhollyEmptySlots()
        {
            // Page height 200: real content at the very top (page-slot 0) and real content starting
            // around y=900 (page-slot 4, since 900/200=4.5) - slots 1-3 have nothing painted in them
            // at all and must not be materialized.
            var container = await BuildAndLayout(
                "<div id='a' style='height:20px; background:rgb(0,0,0);'></div>" +
                "<div id='gap' style='height:880px;'></div>" +
                "<div id='b' style='height:20px; background:rgb(0,0,0);'></div>",
                pageHeight: 200);

            var slots = container.GetPaginationSlots();

            Assert.Contains(0.0, slots);
            Assert.DoesNotContain(200.0, slots);
            Assert.DoesNotContain(400.0, slots);
            Assert.DoesNotContain(600.0, slots);
            Assert.Contains(800.0, slots);
        }

        [Fact]
        public async Task GetPaginationSlots_ContiguousRealContent_KeepsEveryPage()
        {
            // Real, painted content spanning several page-heights (no gaps) must still produce one
            // slot per page, exactly matching the un-skipped pagination behavior - this is the
            // existing OverflowHiddenOnRootHtml_DoesNotClipLaterPages scenario's own shape (several
            // real, page-break-driven sections), reproduced here as a direct unit test of the
            // mechanism rather than only through the full PdfGenerator pipeline.
            var container = await BuildAndLayout(
                "<div style='height:900px; background:rgb(9,9,9);'>section content spanning pages</div>",
                pageHeight: 200);

            var slots = container.GetPaginationSlots();

            Assert.Equal(new[] { 0.0, 200.0, 400.0, 600.0, 800.0 }, slots);
        }

        [Fact]
        public async Task GetPaginationSlots_PureMarginOnlyDocument_FallsBackToSingleSlot()
        {
            // A document that laid out to a real, non-zero height but has nothing "printable"
            // anywhere (an extreme, all-margin edge case) must still produce exactly one page - never
            // zero - rather than emitting a content-less PDF.
            var container = await BuildAndLayout(
                "<div id='gap' style='height:900px;'></div>",
                pageHeight: 200);

            var slots = container.GetPaginationSlots();

            Assert.Single(slots);
            Assert.Equal(0.0, slots[0]);
        }

        // --- Helper ---

        private static async Task<HtmlContainerInt> BuildAndLayout(string bodyHtml, double pageHeight)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            var html = $"<!DOCTYPE html><html><body>{bodyHtml}</body></html>";
            await container.SetHtml(html, null);

            var size = new XSize(595, pageHeight);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = new PeachPDF.Html.Adapters.Entities.RSize(595, 0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new PeachPDF.Adapters.GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            return container;
        }
    }
}
