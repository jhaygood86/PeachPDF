using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Break values on flex and grid items, honoured at the point the items are finally placed.
    /// </summary>
    /// <remarks>
    /// Every phase of these engines before that lays an item out at the container's content origin purely
    /// to measure it, and translates it into place afterwards — so a break decided during one names a
    /// position the item is about to be moved away from. The pass under test is the one that runs once
    /// the items are where they will finally be.
    /// <para>
    /// The page band here is 160pt (a 200pt page less 20pt margins), so slot <c>k</c> is
    /// <c>[20 + 160k, 180 + 160k)</c>. Fixtures state heights in <c>pt</c> so the arithmetic reads
    /// literally.
    /// </para>
    /// </remarks>
    public class FlexGridFragmentationIntegrationTests
    {
        private const double PageHeight = 200;

        private static int SlotOf(HtmlContainerInt container, CssBox box) =>
            container.PageIndexOf(box.Location.Y + HtmlContainerInt.PageBoundaryEpsilon);

        // A filler tall enough that the container's first line starts near the foot of slot 0, so a line
        // of the stated height necessarily crosses into slot 1.
        private static string Document(string containerStyle, string itemStyle) =>
            LayoutHarness.Wrap(
                "<div style='height:120pt'>filler</div>"
                + $"<div id='c' style='{containerStyle}'>"
                + $"<div id='a' style='height:70pt;{itemStyle}'>A</div>"
                + "</div>");

        // ─── Flex ─────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("break-inside: avoid")]
        [InlineData("break-inside: avoid-page")]
        public async Task FlexItem_AvoidingABreak_MovesItsLineToTheNextPage(string declaration)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                Document("display:flex", declaration), pageHeight: PageHeight);

            var a = LayoutHarness.FindById(root, "a");
            Assert.NotNull(a);
            Assert.Equal(1, SlotOf(container, a!));

            // Whole, not cut: its full height sits inside the destination band.
            Assert.True(a!.ActualBottom <= container.PageBottomOf(1) + 0.5,
                $"expected the item whole in slot 1, it runs to {a.ActualBottom}");
        }

        // §2 monolithic content — here a scroll container — may not be broken by any user agent, and a
        // page boundary in a flex container is a break like any other.
        [Fact]
        public async Task MonolithicFlexItem_MovesItsLineToTheNextPage()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                Document("display:flex", "overflow:hidden"), pageHeight: PageHeight);

            var a = LayoutHarness.FindById(root, "a");
            Assert.NotNull(a);
            Assert.Equal(1, SlotOf(container, a!));
        }

        // A forced break is taken whether or not the line would have been cut.
        [Theory]
        [InlineData("break-before: page")]
        [InlineData("page-break-before: always")]
        public async Task FlexItem_WithAForcedBreak_StartsItsLineOnTheNextPage(string declaration)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='height:40pt'>filler</div>"
                    + $"<div id='c' style='display:flex'><div id='a' style='height:20pt;{declaration}'>A</div></div>"),
                pageHeight: PageHeight);

            var a = LayoutHarness.FindById(root, "a");
            Assert.NotNull(a);
            Assert.Equal(1, SlotOf(container, a!));
        }

        // §3.1: a forced break falls at a class-A break point if *either* side asks for it. The line
        // after one whose items carry a forced break-after starts on the next page, exactly as it would
        // had the same intent been spelt on its own items as break-before.
        [Theory]
        [InlineData("break-after: page")]
        [InlineData("page-break-after: always")]
        public async Task FlexItem_WithAForcedBreakAfter_StartsTheNextLineOnTheNextPage(string declaration)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='height:40pt'>filler</div>"
                    + "<div id='c' style='display:flex; flex-wrap:wrap; width:300pt'>"
                    + $"<div id='a' style='width:100%;height:20pt;{declaration}'>A</div>"
                    + "<div id='b' style='width:100%;height:20pt'>B</div>"
                    + "</div>"),
                pageHeight: PageHeight);

            var a = LayoutHarness.FindById(root, "a");
            var b = LayoutHarness.FindById(root, "b");
            Assert.NotNull(a);
            Assert.NotNull(b);

            // The line that declared it stays where it was - the break is after it, not before it.
            Assert.Equal(0, SlotOf(container, a!));
            Assert.Equal(1, SlotOf(container, b!));
        }

        [Theory]
        [InlineData("break-after: page")]
        [InlineData("page-break-after: always")]
        public async Task GridItem_WithAForcedBreakAfter_StartsTheNextRowOnTheNextPage(string declaration)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='height:40pt'>filler</div>"
                    + "<div id='c' style='display:grid; grid-template-columns:1fr'>"
                    + $"<div id='a' style='height:20pt;{declaration}'>A</div>"
                    + "<div id='b' style='height:20pt'>B</div>"
                    + "</div>"),
                pageHeight: PageHeight);

            var a = LayoutHarness.FindById(root, "a");
            var b = LayoutHarness.FindById(root, "b");
            Assert.NotNull(a);
            Assert.NotNull(b);

            Assert.Equal(0, SlotOf(container, a!));
            Assert.Equal(1, SlotOf(container, b!));
        }

        // A break-after on the *last* line names the break point after the container, which is not this
        // pass's to take - there is no line after it to move, and §3.1's propagation stops before a box
        // whose children an engine places for itself.
        [Theory]
        [InlineData("display:flex; flex-wrap:wrap")]
        [InlineData("display:grid; grid-template-columns:1fr")]
        public async Task AForcedBreakAfterOnTheLastLine_MovesNothing(string containerStyle)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='height:40pt'>filler</div>"
                    + $"<div id='c' style='{containerStyle}; width:300pt'>"
                    + "<div id='a' style='width:100%;height:20pt'>A</div>"
                    + "<div id='b' style='width:100%;height:20pt;break-after:page'>B</div>"
                    + "</div>"),
                pageHeight: PageHeight);

            var a = LayoutHarness.FindById(root, "a");
            var b = LayoutHarness.FindById(root, "b");
            Assert.NotNull(a);
            Assert.NotNull(b);

            Assert.Equal(0, SlotOf(container, a!));
            Assert.Equal(0, SlotOf(container, b!));
        }

        // An item spanning several rows is the earlier sibling of the row after the *last* one it covers,
        // so its break-after falls there. Attributing it to the row it starts in would take the break at a
        // boundary running through the middle of the item itself.
        [Fact]
        public async Task AGridItemSpanningRows_TakesItsBreakAfterAtTheEndOfTheSpan()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='height:40pt'>filler</div>"
                    + "<div id='c' style='display:grid; grid-template-columns:1fr 1fr'>"
                    + "<div id='a' style='grid-row:1/3;height:40pt;break-after:page'>A</div>"
                    + "<div id='b' style='grid-column:2;height:20pt'>B</div>"
                    + "<div id='d' style='grid-column:2;height:20pt'>D</div>"
                    + "<div id='e' style='height:20pt'>E</div>"
                    + "</div>"),
                pageHeight: PageHeight);

            var a = LayoutHarness.FindById(root, "a");
            var d = LayoutHarness.FindById(root, "d");
            var e = LayoutHarness.FindById(root, "e");
            Assert.NotNull(a);
            Assert.NotNull(d);
            Assert.NotNull(e);

            // Row 2 is still beside the spanning item, on its page; only the row after the span moves.
            Assert.Equal(0, SlotOf(container, a!));
            Assert.Equal(0, SlotOf(container, d!));
            Assert.Equal(1, SlotOf(container, e!));
        }

        // The line moves together, not the item that asked. The cross-axis phases aligned these against
        // each other, and moving one alone would break that alignment.
        [Fact]
        public async Task AFlexLineMovesTogether_NotOnlyTheItemThatAsked()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='height:120pt'>filler</div>"
                    + "<div id='c' style='display:flex'>"
                    + "<div id='a' style='height:70pt; break-inside:avoid'>A</div>"
                    + "<div id='b' style='height:70pt'>B</div>"
                    + "</div>"),
                pageHeight: PageHeight);

            var a = LayoutHarness.FindById(root, "a");
            var b = LayoutHarness.FindById(root, "b");
            Assert.NotNull(a);
            Assert.NotNull(b);

            Assert.Equal(1, SlotOf(container, a!));
            Assert.Equal(SlotOf(container, a!), SlotOf(container, b!));
            Assert.Equal(a.Location.Y, b!.Location.Y, 1);
        }

        // An item with no break value of its own is left where it is. Moving every straddling line would
        // paginate a flex container quite differently from how it renders today, which is a larger change
        // than the break values themselves ask for.
        [Fact]
        public async Task FlexItem_WithNoBreakValue_IsLeftWhereItIs()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                Document("display:flex", ""), pageHeight: PageHeight);

            var a = LayoutHarness.FindById(root, "a");
            Assert.NotNull(a);
            Assert.Equal(0, SlotOf(container, a!));
        }

        // A line taller than a whole band has nowhere better to be: moving it would ask the same question
        // again on the next fragmentainer, forever.
        [Fact]
        public async Task AFlexLineTallerThanTheBand_IsNotMoved()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                Document("display:flex", "break-inside:avoid").Replace("height:70pt", "height:400pt"),
                pageHeight: PageHeight);

            var a = LayoutHarness.FindById(root, "a");
            Assert.NotNull(a);
            Assert.Equal(0, SlotOf(container, a!));
        }

        // ─── Grid ─────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("break-inside: avoid")]
        [InlineData("break-inside: avoid-page")]
        public async Task GridItem_AvoidingABreak_MovesItsRowToTheNextPage(string declaration)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                Document("display:grid; grid-template-columns:1fr", declaration), pageHeight: PageHeight);

            var a = LayoutHarness.FindById(root, "a");
            Assert.NotNull(a);
            Assert.Equal(1, SlotOf(container, a!));
        }

        [Fact]
        public async Task MonolithicGridItem_MovesItsRowToTheNextPage()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                Document("display:grid; grid-template-columns:1fr", "overflow:hidden"), pageHeight: PageHeight);

            var a = LayoutHarness.FindById(root, "a");
            Assert.NotNull(a);
            Assert.Equal(1, SlotOf(container, a!));
        }

        // The row moves together, for the same reason a flex line does.
        [Fact]
        public async Task AGridRowMovesTogether_NotOnlyTheItemThatAsked()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='height:120pt'>filler</div>"
                    + "<div id='c' style='display:grid; grid-template-columns:1fr 1fr'>"
                    + "<div id='a' style='height:70pt; break-inside:avoid'>A</div>"
                    + "<div id='b' style='height:70pt'>B</div>"
                    + "</div>"),
                pageHeight: PageHeight);

            var a = LayoutHarness.FindById(root, "a");
            var b = LayoutHarness.FindById(root, "b");
            Assert.NotNull(a);
            Assert.NotNull(b);

            Assert.Equal(1, SlotOf(container, a!));
            Assert.Equal(SlotOf(container, a!), SlotOf(container, b!));
            Assert.Equal(a.Location.Y, b!.Location.Y, 1);
        }

        [Fact]
        public async Task GridItem_WithNoBreakValue_IsLeftWhereItIs()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                Document("display:grid; grid-template-columns:1fr", ""), pageHeight: PageHeight);

            var a = LayoutHarness.FindById(root, "a");
            Assert.NotNull(a);
            Assert.Equal(0, SlotOf(container, a!));
        }

        // ─── The boundary this pass must not cross ────────────────────────────────

        // Inside a table cell the container's coordinates belong to the table's own row grid, not the
        // page's. Shifting a line against the page grid from there moves it out from under the row that
        // is placing it - the defect the monolithic mover had to be gated for as well.
        [Fact]
        public async Task AFlexItemInsideATableCell_IsLeftToTheTableEngine()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='height:120pt'>filler</div>"
                    + "<table><tbody><tr><td>"
                    + "<div id='c' style='display:flex'><div id='a' style='height:70pt; break-inside:avoid'>A</div></div>"
                    + "</td></tr></tbody></table>"),
                pageHeight: PageHeight);

            var a = LayoutHarness.FindById(root, "a");
            var cell = LayoutHarness.Descendants(root).First(b => b.Display == "table-cell");
            Assert.NotNull(a);

            // Wherever the table put the cell, the item is still inside it rather than a page away.
            Assert.True(a!.Location.Y >= cell.Location.Y - 0.5,
                $"expected the item to stay within its cell (cell at {cell.Location.Y}, item at {a.Location.Y})");
        }

        // ─── The lines after a relocated one follow it ────────────────────────────

        // A line or row is moved by the *accumulated* displacement, not by its own. Everything below a
        // line that moved to the next fragmentainer has to follow it there: applying only the moving
        // line's own delta left the lines after it sitting on top of it, and the container reported a
        // height a whole displacement short of the content it holds.
        private const string ThreeLines =
            "<div style='height:260pt'>filler</div>"
            + "<div id='c' style='{0}; width:300pt'>"
            + "<div class='it' id='i1' style='width:45%;height:60pt;break-inside:avoid'>1</div>"
            + "<div class='it' id='i2' style='width:45%;height:60pt;break-inside:avoid'>2</div>"
            + "<div class='it' id='i3' style='width:45%;height:60pt;break-inside:avoid'>3</div>"
            + "<div class='it' id='i4' style='width:45%;height:60pt;break-inside:avoid'>4</div>"
            + "<div class='it' id='i5' style='width:45%;height:60pt;break-inside:avoid'>5</div>"
            + "<div class='it' id='i6' style='width:45%;height:60pt;break-inside:avoid'>6</div>"
            + "</div>";

        [Theory]
        [InlineData("display:flex; flex-wrap:wrap")]
        [InlineData("display:grid; grid-template-columns:auto auto")]
        public async Task ALineAfterARelocatedOne_FollowsItRatherThanOverlapping(string containerStyle)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(string.Format(ThreeLines, containerStyle)),
                pageHeight: 400, margin: 20);

            var items = Enumerable.Range(1, 6)
                .Select(i => LayoutHarness.FindById(root, $"i{i}")!)
                .ToList();

            // The middle line straddled the boundary and moved; the last one has to end up below it.
            Assert.True(items[2].Location.Y > items[0].ActualBottom - 0.5,
                "the relocated line must still be below the one before it");
            Assert.True(items[4].Location.Y >= items[2].ActualBottom - 0.5,
                $"the line after the relocated one overlaps it: it starts at {items[4].Location.Y:F1} "
                + $"while the line above ends at {items[2].ActualBottom:F1}");

            // And it really did relocate - otherwise this asserts nothing.
            Assert.True(items[2].Location.Y > items[0].ActualBottom + 0.5,
                "the fixture no longer relocates a line; it asserts nothing as written");
        }

        [Theory]
        [InlineData("display:flex; flex-wrap:wrap")]
        [InlineData("display:grid; grid-template-columns:auto auto")]
        public async Task ARelocatingContainer_ReportsAHeightThatHoldsItsContent(string containerStyle)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(string.Format(ThreeLines, containerStyle)),
                pageHeight: 400, margin: 20);

            var container = LayoutHarness.FindById(root, "c")!;
            var lowest = Enumerable.Range(1, 6)
                .Max(i => LayoutHarness.FindById(root, $"i{i}")!.ActualBottom);

            // Sized from its lines before the relocation ran, the container was a whole displacement
            // short of the content it holds.
            Assert.True(container.ActualBottom >= lowest - 0.5,
                $"container ends at {container.ActualBottom:F1} but its content reaches {lowest:F1}");
        }
    }
}
