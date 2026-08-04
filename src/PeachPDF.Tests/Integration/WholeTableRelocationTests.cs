using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Tests.TestSupport;
using System.Linq;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A table that laid out without breaking between any two of its own rows did not fragment, so
    /// <see href="https://www.w3.org/TR/css-break-3/#possible-breaks">§4.3</see>'s mover treats it as
    /// content that cannot be broken and moves it whole.
    /// </summary>
    /// <remarks>
    /// This used to be a post-check at the end of <c>CssLayoutEngineTable.LayoutCells</c>, which could
    /// only ever translate: the engine runs with the fragmentainer detached. Asked from
    /// <c>CssBox.PerformLayoutEpilogue</c> instead, the table is laid out again at its destination like
    /// every other relocated box — which is what lets a table repeating a header take part, since a
    /// re-layout rebuilds the header's per-page proxies from scratch.
    /// </remarks>
    public class WholeTableRelocationTests
    {
        private const double PageHeight = 842;

        private static string Document(string tableMarkup, double spacerHeight, string extraCss = "") =>
            $$"""
            <!DOCTYPE html><html><head><style>
            body { margin: 0 }
            table { border-collapse: collapse; width: 100% }
            td, th { padding: 3pt }
            {{extraCss}}
            </style></head><body>
            <div style='height: {{spacerHeight}}pt'></div>
            {{tableMarkup}}
            </body></html>
            """;

        private static async Task<(CssBox Table, HtmlContainerInt Container, CssBox Root)> LayoutAsync(
            string html)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight, margin: 0);
            var table = LayoutHarness.Descendants(root).First(b => b.Display.Value == DisplayMode.Table);
            return (table, container, root);
        }

        // The row-height estimate is a one-line-of-text heuristic, blind to a cell holding tall block
        // content. When it concludes "fits" and real layout straddles the boundary, the table is moved
        // once its own height is known - a single-row table is never row-split, so without the move its
        // content paints sliced across two pages.
        [Fact]
        public async Task ATableTheEstimateMisjudged_IsMovedWholeOnceItsHeightIsKnown()
        {
            var (table, _, _) = await LayoutAsync(Document(
                "<table><tr><td><div style='height:400pt'>tall content</div></td></tr></table>",
                spacerHeight: 500));

            Assert.Equal(PageHeight, table.Location.Y, 1);
        }

        // The move places the table at the page's own content top, and nothing inside the engine may
        // nudge it off there afterwards. GetVerticalSpacing() is -1 for a collapsed-border table, so the
        // row cursor it derives sits one point *above* the box - and asking which page *that* is on names
        // the page the table just left, which the engine's own pre-check then corrects by moving the table
        // one further point onto the next page.
        [Fact]
        public async Task ARelocatedCollapsedBorderTable_IsNotNudgedPastThePageTop()
        {
            var (table, container, _) = await LayoutAsync(Document(
                "<table><tr><td><div style='height:400pt'>tall content</div></td></tr></table>",
                spacerHeight: 500));

            Assert.Equal(container.PageTopOf(1), table.Location.Y, 3);
        }

        // A table taller than a whole page cannot be helped by moving it: it would straddle the next
        // boundary just the same, so §4.3 gives the constraint up and leaves it where flow put it.
        [Fact]
        public async Task ATableTallerThanOnePage_IsLeftWhereFlowPutIt()
        {
            var (table, _, _) = await LayoutAsync(Document(
                "<table><tr><td><div style='height:900pt'>tall content</div></td></tr></table>",
                spacerHeight: 500));

            Assert.True(table.Location.Y < PageHeight,
                $"expected the table to stay on page 1 but it is at Y={table.Location.Y:F1}");
        }

        // A table that *did* break between two of its rows fragmented, so the boundary it crosses is one
        // it chose - the mover has nothing to say about it.
        [Fact]
        public async Task ATableThatBrokeBetweenItsOwnRows_IsNotMoved()
        {
            var (table, _, _) = await LayoutAsync(Document(
                "<table>" + string.Concat(Enumerable.Range(1, 40).Select(i =>
                    $"<tr><td><div style='height:30pt'>row {i}</div></td></tr>")) + "</table>",
                spacerHeight: 500));

            Assert.True(table.Location.Y < PageHeight,
                $"expected the fragmenting table to stay where it began but it is at Y={table.Location.Y:F1}");
            Assert.NotNull(table.PageBreakBottoms);
            Assert.NotEmpty(table.PageBreakBottoms!);
        }

        // The move introduces a break between the table and whatever precedes it, so §3.1's keep-with-next
        // applies exactly as it does to every other relocation: the UA print default
        // h1-h6 { break-after: avoid } chains the heading to the table and it travels too.
        [Fact]
        public async Task AnAvoidChainedHeading_TravelsWithTheMovedTable()
        {
            var (table, _, root) = await LayoutAsync(Document(
                "<h2 id='h'>Heading</h2>"
                + "<table><tr><td><div style='height:400pt'>tall content</div></td></tr></table>",
                spacerHeight: 500,
                extraCss: "h2 { margin: 6pt 0 }"));

            var heading = LayoutHarness.FindById(root, "h")!;

            Assert.True(heading.Location.Y >= PageHeight,
                $"the heading should have travelled with its table but it is at Y={heading.Location.Y:F1}");
            Assert.True(heading.Location.Y < table.Location.Y,
                "the heading must still precede the table it is chained to");
        }

        // A repeating <thead> used to exclude a table from this correction entirely, because the engine
        // could not be run a second time (#353) and the proxies it had already created would be left at
        // the coordinates of the page the table was leaving. Laying the table out again rebuilds them.
        [Fact]
        public async Task ATableWithARepeatingHeader_IsMovedAndItsHeaderProxiesRebuilt()
        {
            var (table, _, root) = await LayoutAsync(Document(
                "<table><thead><tr><th>Head</th></tr></thead>"
                + "<tbody><tr><td><div style='height:400pt'>tall content</div></td></tr></tbody></table>",
                spacerHeight: 500));

            var proxies = LayoutHarness.Descendants(root).OfType<CssProxyBox>().ToList();

            Assert.Equal(PageHeight, table.Location.Y, 1);

            // One proxy, at the moved table's own top - not a stale one left behind on the page the
            // table came from, which is what the exclusion this replaces existed to avoid. It sits one
            // point above the table box because a collapsed-border table's row cursor does
            // (GetVerticalSpacing() is -1), so the claim is proximity, not the slot index.
            Assert.Single(proxies);
            Assert.True(Math.Abs(proxies[0].Location.Y - table.Location.Y) <= 2,
                $"the header proxy should sit at the moved table's top ({table.Location.Y:F1}) "
                + $"but it is at Y={proxies[0].Location.Y:F1}");
        }

        // A table with a repeating <tfoot> and no <thead> fell between both of the engine's own
        // pre-checks: the headerless one requires no repeating footer, and the header-aware one requires
        // a repeating header. The correction does not ask which of them the table has - only whether it
        // broke - so a footer-only table is moved like any other.
        [Fact]
        public async Task ATableWithARepeatingFooterAndNoHeader_IsMovedToo()
        {
            var (table, _, _) = await LayoutAsync(Document(
                "<table><tfoot><tr><td>Foot</td></tr></tfoot>"
                + "<tbody><tr><td><div style='height:400pt'>tall content</div></td></tr></tbody></table>",
                spacerHeight: 500));

            Assert.True(table.Location.Y >= PageHeight,
                $"the footer-only table should have moved to page 2 but it is at Y={table.Location.Y:F1}");
        }
    }
}
