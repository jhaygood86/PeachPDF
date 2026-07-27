using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <see href="https://www.w3.org/TR/css-break-3/#break-between">§3.1</see>'s forced breaks at the
    /// class-A break point between two table rows.
    /// </summary>
    /// <remarks>
    /// The engine has only ever broken rows on geometry — <c>EstimateRowHeight</c> against the remaining
    /// band — and never read the values, which have cascaded onto the row boxes since #299. A row is
    /// never split, so <c>break-inside: avoid</c> on one is satisfied by construction; what was missing
    /// is a break that has to happen even though the row fits.
    /// </remarks>
    public class TableRowBreakValueTests
    {
        private const double PageHeight = 300;
        private const double Margin = 20;

        private static string Table(string rowCss, string extraRowAttrs = "") =>
            LayoutHarness.Wrap(
                "<table id='t' style='width:100%'>"
                + "<tbody>"
                + "<tr id='r1'><td>Row one</td></tr>"
                + $"<tr id='r2' {extraRowAttrs}><td>Row two</td></tr>"
                + $"<tr id='r3' style='{rowCss}'><td>Row three</td></tr>"
                + "</tbody></table>");

        private static int PageOf(HtmlContainerInt container, CssBox box) =>
            container.SlotStartingAt(box.Location.Y);

        [Fact]
        public async Task WithoutABreakValue_EveryRowStaysOnTheFirstPage()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(Table(""), pageHeight: PageHeight, margin: Margin);

            foreach (var id in new[] { "r1", "r2", "r3" })
            {
                Assert.Equal(0, PageOf(container, LayoutHarness.FindById(root, id)!));
            }
        }

        [Fact]
        public async Task BreakBeforeOnARow_StartsItOnTheNextPage()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                Table("break-before:page"), pageHeight: PageHeight, margin: Margin);

            Assert.Equal(0, PageOf(container, LayoutHarness.FindById(root, "r2")!));
            Assert.Equal(1, PageOf(container, LayoutHarness.FindById(root, "r3")!));
        }

        [Fact]
        public async Task BreakAfterOnTheRowBefore_StartsTheNextOneOnTheNextPage()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                Table("", "style='break-after:page'"), pageHeight: PageHeight, margin: Margin);

            Assert.Equal(0, PageOf(container, LayoutHarness.FindById(root, "r2")!));
            Assert.Equal(1, PageOf(container, LayoutHarness.FindById(root, "r3")!));
        }

        [Fact]
        public async Task BreakBeforeOnARowGroup_IsSeenAtItsFirstRow()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<table id='t' style='width:100%'>"
                + "<tbody><tr id='r1'><td>Row one</td></tr></tbody>"
                + "<tbody style='break-before:page'><tr id='r2'><td>Row two</td></tr></tbody>"
                + "</table>"), pageHeight: PageHeight, margin: Margin);

            // §3.1 reads both sides of the break point through the chains they begin and end, so the
            // group's own value is seen at the row that begins it.
            Assert.Equal(0, PageOf(container, LayoutHarness.FindById(root, "r1")!));
            Assert.Equal(1, PageOf(container, LayoutHarness.FindById(root, "r2")!));
        }

        [Fact]
        public async Task AForcedRowBreak_StillRepeatsTheHeaderOnTheNewPage()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<table id='t' style='width:100%'>"
                + "<thead><tr><th>Head</th></tr></thead>"
                + "<tbody><tr id='r1'><td>Row one</td></tr>"
                + "<tr id='r2' style='break-before:page'><td>Row two</td></tr></tbody>"
                + "</table>"), pageHeight: PageHeight, margin: Margin);

            var proxies = LayoutHarness.Descendants(root).OfType<CssProxyBox>().ToList();

            Assert.Equal(1, PageOf(container, LayoutHarness.FindById(root, "r2")!));

            // Taking the break through the engine's own row loop is what keeps the repeated header
            // working: the proxy for the new page is created by the same code the geometric break uses.
            Assert.Equal(2, proxies.Count);
            Assert.Equal(new[] { 0, 1 }, proxies.Select(p => container.SlotStartingAt(p.Location.Y)).Order().ToArray());
        }
    }
}
