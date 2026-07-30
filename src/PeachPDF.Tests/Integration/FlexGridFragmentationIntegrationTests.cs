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

        // ─── Flex item content fragmentation, multi-line (issues #517/#526) ───────

        // A wrapped row-direction container's commit pass walks every line in one pass, committing as
        // many as fit before it stops - so line 1 finishes in slot 0 and only line 3's own content needs
        // to continue.
        [Fact]
        public async Task ALaterLinesContent_ContinuesOnTheNextPageWhenItDoesNotFit()
        {
            var lines = string.Concat(Enumerable.Range(1, 3)
                .Select(i => $"<div id='l{i}' style='width:100%'>line {i} one two</div>"));

            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='height:120pt'>filler</div>"
                    + "<div id='c' style='display:flex; flex-wrap:wrap; line-height:20pt'>"
                    + lines + "</div>"),
                pageHeight: PageHeight);

            var l1 = LayoutHarness.FindById(root, "l1");
            var l3 = LayoutHarness.FindById(root, "l3");
            Assert.NotNull(l1);
            Assert.NotNull(l3);

            // 120pt filler + 2 lines of 20pt leaves exactly 0pt for line 3 in slot 0's 160pt band.
            Assert.Equal(0, SlotOf(container, l1!));
            Assert.Equal(1, SlotOf(container, l3!));

            var authored = LayoutHarness.Descendants(root)
                .SelectMany(b => b.Words).Where(w => !w.IsSpaces).Select(w => w.Text).ToList();
            var claimed = container.FragmentTree!.Fragmentainers
                .SelectMany(f => FlattenFragments(f.Root))
                .SelectMany(f => f.Words).Select(w => w.Word.Text).ToList();

            Assert.Equal(authored.OrderBy(w => w), claimed.OrderBy(w => w));
        }

        // The commit loop must walk lines in block-axis (down-the-page) order, not source order - under
        // wrap-reverse those differ, and reading the wrong one would commit the wrong line first (or
        // resume the wrong one), exactly the bug issues #448/#458/#459 already fixed for line relocation.
        [Fact]
        public async Task WrapReverse_CommitsLinesInBlockAxisOrderNotSourceOrder()
        {
            var lines = string.Concat(Enumerable.Range(1, 3)
                .Select(i => $"<div id='l{i}' style='width:100%'>line {i} one two</div>"));

            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='height:120pt'>filler</div>"
                    + "<div id='c' style='display:flex; flex-wrap:wrap-reverse; line-height:20pt'>"
                    + lines + "</div>"),
                pageHeight: PageHeight);

            var l1 = LayoutHarness.FindById(root, "l1");
            var l3 = LayoutHarness.FindById(root, "l3");
            Assert.NotNull(l1);
            Assert.NotNull(l3);

            // wrap-reverse stacks source-first line 1 at the bottom and source-last line 3 at the top, so
            // line 3 (block-axis first) is the one already sitting in slot 0's remaining room, and line 1
            // (block-axis last) is the one pushed into slot 1.
            Assert.Equal(1, SlotOf(container, l1!));
            Assert.Equal(0, SlotOf(container, l3!));

            var authored = LayoutHarness.Descendants(root)
                .SelectMany(b => b.Words).Where(w => !w.IsSpaces).Select(w => w.Text).ToList();
            var claimed = container.FragmentTree!.Fragmentainers
                .SelectMany(f => FlattenFragments(f.Root))
                .SelectMany(f => f.Words).Select(w => w.Word.Text).ToList();

            Assert.Equal(authored.OrderBy(w => w), claimed.OrderBy(w => w));
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

        // ─── Grid item content fragmentation (issues #517/#526) ───────────────────

        // A row's own content, once it sits at its final position, is laid out for real against a live
        // fragmentainer - so a later row whose content does not fit the current page continues onto the
        // next one instead of translating a subtree measured with breaking suppressed. The commit pass
        // walks every row in one pass, committing as many as fit before it stops, so rows 1-3 finish in
        // slot 0 and only row 4's own content needs to continue.
        [Fact]
        public async Task ALaterRowsContent_ContinuesOnTheNextPageWhenItDoesNotFit()
        {
            var rows = string.Concat(Enumerable.Range(1, 4)
                .Select(i => $"<div id='r{i}'>row {i} one two</div>"));

            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='height:120pt'>filler</div>"
                    + "<div id='c' style='display:grid; grid-template-columns:1fr; line-height:20pt'>"
                    + rows + "</div>"),
                pageHeight: PageHeight);

            var r1 = LayoutHarness.FindById(root, "r1");
            var r4 = LayoutHarness.FindById(root, "r4");
            Assert.NotNull(r1);
            Assert.NotNull(r4);

            // The fixture is only meaningful if row 4 genuinely reaches the boundary rather than fitting
            // comfortably: 120pt filler + 3 rows of 20pt leaves exactly 0pt for row 4 in slot 0's 160pt band.
            Assert.Equal(0, SlotOf(container, r1!));
            Assert.Equal(1, SlotOf(container, r4!));

            var authored = LayoutHarness.Descendants(root)
                .SelectMany(b => b.Words).Where(w => !w.IsSpaces).Select(w => w.Text).ToList();
            var claimed = container.FragmentTree!.Fragmentainers
                .SelectMany(f => FlattenFragments(f.Root))
                .SelectMany(f => f.Words).Select(w => w.Word.Text).ToList();

            // Every authored word survives somewhere in the fragment tree - row 4's content is neither
            // lost (the resumed pass never re-entering it) nor duplicated (claimed by two pages at once).
            Assert.Equal(authored.OrderBy(w => w), claimed.OrderBy(w => w));
        }

        // An item spanning several rows is grouped at the row it starts, matching
        // RelocateRowsAcrossFragmentainers's own choice - so its own content commits there too, and can
        // itself continue onto the next page independently of the ordinary single-row items beside it.
        [Fact]
        public async Task ARowSpanningItemsOwnContent_ContinuesOnTheNextPageWhenItDoesNotFit()
        {
            var spanningChildren = string.Concat(Enumerable.Range(1, 4)
                .Select(i => $"<div id='a{i}' style='height:20pt'>a{i}</div>"));

            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='height:100pt'>filler</div>"
                    + "<div id='c' style='display:grid; grid-template-columns:1fr 1fr'>"
                    + $"<div id='a' style='grid-row:1/3'>{spanningChildren}</div>"
                    + "<div id='b' style='grid-column:2;height:40pt'>B</div>"
                    + "<div id='d' style='grid-column:2;height:40pt'>D</div>"
                    + "</div>"),
                pageHeight: PageHeight);

            var a1 = LayoutHarness.FindById(root, "a1");
            var a4 = LayoutHarness.FindById(root, "a4");
            Assert.NotNull(a1);
            Assert.NotNull(a4);

            // 100pt filler leaves 60pt of slot 0's 160pt band - room for a1-a3 (60pt) but not a4.
            Assert.Equal(0, SlotOf(container, a1!));
            Assert.Equal(1, SlotOf(container, a4!));
        }

        // A subgrid item's adopted track geometry (GridSubgridContext) is captured once, on the fresh
        // pass, and must be re-threaded on every resumed commit too - it is not recomputed, since a
        // resumed pass has no local Track[] arrays to derive it from. If the context were lost on resume,
        // the subgrid's columns would fall back to an ordinary implicit track instead of adopting the
        // parent's, and the item would size itself far narrower than the parent's spanned columns.
        [Fact]
        public async Task ASubgridItemsOwnContent_KeepsItsAdoptedColumnsAcrossAResume()
        {
            var words = string.Join(" ", Enumerable.Range(1, 4).Select(i => $"word{i}"));

            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='height:120pt'>filler</div>"
                    + "<div id='c' style='display:grid; grid-template-columns:100pt 100pt 100pt; line-height:20pt'>"
                    + "<div id='sub' style='grid-column:1/4; display:grid; grid-template-columns:subgrid'>"
                    // inner spans all 3 adopted columns explicitly - ordinary auto-placement would only
                    // give it the first one, which would pass even with a lost SubgridContext.
                    + $"<div id='inner' style='grid-column:1/4'>{words}</div>"
                    + "</div></div>"),
                pageHeight: PageHeight);

            var sub = LayoutHarness.FindById(root, "sub");
            var inner = LayoutHarness.FindById(root, "inner");
            Assert.NotNull(sub);
            Assert.NotNull(inner);

            // The subgrid adopted all three 100pt parent columns (300pt), not an ordinary implicit
            // single-item track - the value a lost SubgridContext on resume would fall back to.
            Assert.Equal(300, sub!.ActualBoxSizingWidth, 1);
            Assert.Equal(300, inner!.ActualBoxSizingWidth, 1);

            var authored = LayoutHarness.Descendants(root)
                .SelectMany(b => b.Words).Where(w => !w.IsSpaces).Select(w => w.Text).ToList();
            var claimed = container.FragmentTree!.Fragmentainers
                .SelectMany(f => FlattenFragments(f.Root))
                .SelectMany(f => f.Words).Select(w => w.Word.Text).ToList();
            Assert.Equal(authored.OrderBy(w => w), claimed.OrderBy(w => w));
        }

        private static IEnumerable<PeachPDF.Html.Core.Fragments.BoxFragment> FlattenFragments(
            PeachPDF.Html.Core.Fragments.BoxFragment fragment)
        {
            yield return fragment;
            foreach (var child in fragment.Children)
                foreach (var descendant in FlattenFragments(child))
                    yield return descendant;
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

        // ─── §4.4: a break at a point that already is a boundary is already satisfied ─────

        // A line whose top is already flush on a fragmentainer top has had the break the value asks for.
        // Moving it a further page produces the empty fragmentainer §4.4 forbids, and attributes the
        // content to the wrong slot for @page :left/:right and for a directional break's parity.
        [Theory]
        [InlineData("display:flex")]
        [InlineData("display:grid; grid-template-columns:1fr")]
        public async Task AForcedBreakOnALineAlreadyFlushOnAFragmentainerTop_MovesNothing(string containerStyle)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    // Exactly the band: the container's first line begins on slot 1's top edge.
                    "<div style='height:160pt'>filler</div>"
                    + $"<div id='c' style='{containerStyle}'>"
                    + "<div id='a' style='height:20pt;break-before:page'>A</div>"
                    + "</div>"),
                pageHeight: PageHeight);

            var a = LayoutHarness.FindById(root, "a");
            Assert.NotNull(a);

            Assert.Equal(1, SlotOf(container, a!));
            Assert.Equal(container.PageTopOf(1), a!.Location.Y, 1);
        }

        // The neighbours of the flush case, which must keep behaving as they did: a line whose top falls
        // one point short of the boundary still moves to it, and one already past it moves to the slot
        // after.
        [Theory]
        [InlineData(159, 1)]
        [InlineData(161, 2)]
        public async Task AForcedBreakNearAFragmentainerTop_StillTakesTheBreak(double filler, int expectedSlot)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    $"<div style='height:{filler}pt'>filler</div>"
                    + "<div id='c' style='display:flex'>"
                    + "<div id='a' style='height:20pt;break-before:page'>A</div>"
                    + "</div>"),
                pageHeight: PageHeight);

            var a = LayoutHarness.FindById(root, "a");
            Assert.NotNull(a);
            Assert.Equal(expectedSlot, SlotOf(container, a!));
        }

        // §4.4 satisfies a break that has already happened, but a directional value asks for more than a
        // boundary: it asks that the content *begin* on a page of the named side. A line flush on slot 1's
        // top has taken its break, and slot 1 is page 2 - a left page - so `verso` is satisfied there and
        // `recto` is not.
        [Theory]
        [InlineData("break-before:verso", 1)]
        [InlineData("break-before:recto", 2)]
        public async Task ADirectionalBreakOnALineFlushOnAFragmentainerTop_StillAsksForItsSide(
            string declaration, int expectedSlot)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='height:160pt'>filler</div>"
                    + "<div id='c' style='display:flex'>"
                    + $"<div id='a' style='height:20pt;{declaration}'>A</div>"
                    + "</div>"),
                pageHeight: PageHeight);

            var a = LayoutHarness.FindById(root, "a");
            Assert.NotNull(a);
            Assert.Equal(expectedSlot, SlotOf(container, a!));

            // The slot the line vacates has to be reserved, or it holds nothing printable, the fragment
            // tree drops it, and the line prints on exactly the page it was moved off - which would make
            // `recto` indistinguishable from `page` again in precisely the case this test is about.
            Assert.NotNull(container.FragmentTree);
            Assert.Equal(expectedSlot + 1, container.FragmentTree!.Fragmentainers.Count);
        }

        // ─── §3.1: a directional break selects the side, not merely the next page ────────

        // Slot k is page k + 1 and page 1 is a right page, so slot 1 is a left page: a line asking to
        // begin recto has to step over it, and the slot stepped over is a deliberately blank page rather
        // than one the fragment tree may drop for holding no content.
        [Theory]
        [InlineData("display:flex", "break-before:right")]
        [InlineData("display:flex", "break-before:recto")]
        [InlineData("display:grid; grid-template-columns:1fr", "break-before:right")]
        [InlineData("display:grid; grid-template-columns:1fr", "break-before:recto")]
        public async Task ADirectionalBreakBeforeALine_LandsItOnAPageOfThatSide(
            string containerStyle, string declaration)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='height:40pt'>filler</div>"
                    + $"<div id='c' style='{containerStyle}'>"
                    + $"<div id='a' style='height:20pt;{declaration}'>A</div>"
                    + "</div>"),
                pageHeight: PageHeight);

            var a = LayoutHarness.FindById(root, "a");
            Assert.NotNull(a);

            Assert.Equal(2, SlotOf(container, a!));
            Assert.True(container.IsReservedBlankSlot(1),
                "the slot stepped over must be reserved, or nothing blank is printed and recto reads as page");
            Assert.NotNull(container.FragmentTree);
            Assert.Equal(3, container.FragmentTree!.Fragmentainers.Count);
        }

        // §3.1 combines the values that apply at one break point. Within one side of it a directional value
        // subsumes a plain `page`, and two conflicting directional values resolve to the one on the latest
        // element in flow - so the second item's `right` wins over the first item's `left`, and the line
        // opens slot 2 (page 3, a right page) rather than slot 1.
        [Theory]
        [InlineData("break-before:left", "break-before:right", 2)]
        [InlineData("break-before:right", "break-before:left", 1)]
        [InlineData("break-before:right", "break-before:page", 2)]
        [InlineData("break-before:page", "break-before:right", 2)]
        public async Task ConflictingBreakValuesInOneLine_CombineByTheirPositionInFlow(
            string first, string second, int expectedSlot)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='height:40pt'>filler</div>"
                    + "<div id='c' style='display:flex; flex-wrap:wrap; width:300pt'>"
                    + $"<div id='a' style='width:40%;height:20pt;{first}'>A</div>"
                    + $"<div id='b' style='width:40%;height:20pt;{second}'>B</div>"
                    + "</div>"),
                pageHeight: PageHeight);

            var a = LayoutHarness.FindById(root, "a");
            var b = LayoutHarness.FindById(root, "b");
            Assert.NotNull(a);
            Assert.NotNull(b);

            // Both items share one line, so the whole line takes the combined value.
            Assert.Equal(a!.Location.Y, b!.Location.Y, 1);
            Assert.Equal(expectedSlot, SlotOf(container, a));
        }

        // The same rule read from the earlier side of the break point (§3.1 takes it from either side),
        // and the side that needs no step, which must not gain a blank page.
        [Theory]
        [InlineData("break-after:right", 2, true)]
        [InlineData("break-after:left", 1, false)]
        [InlineData("break-after:verso", 1, false)]
        public async Task ADirectionalBreakAfterALine_LandsTheNextLineOnThatSide(
            string declaration, int expectedSlot, bool expectABlankSlot)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='height:40pt'>filler</div>"
                    + "<div id='c' style='display:flex; flex-wrap:wrap; width:300pt'>"
                    + $"<div id='a' style='width:100%;height:20pt;{declaration}'>A</div>"
                    + "<div id='b' style='width:100%;height:20pt'>B</div>"
                    + "</div>"),
                pageHeight: PageHeight);

            var b = LayoutHarness.FindById(root, "b");
            Assert.NotNull(b);

            Assert.Equal(0, SlotOf(container, LayoutHarness.FindById(root, "a")!));
            Assert.Equal(expectedSlot, SlotOf(container, b!));
            Assert.Equal(expectABlankSlot, container.IsReservedBlankSlot(1));
        }

        // ─── §3.1: avoidance between two lines ───────────────────────────────────────────

        // break-after: avoid on the earlier line's items forbids the unforced break at the point below
        // them, so the earlier line travels to join the later one rather than being stranded at the foot
        // of the page its successor just left. This is the user-agent print sheet's h1-h6 rule.
        [Theory]
        [InlineData("display:flex; flex-wrap:wrap", "break-after:avoid")]
        [InlineData("display:flex; flex-wrap:wrap", "break-after:avoid-page")]
        [InlineData("display:flex; flex-wrap:wrap", "page-break-after:avoid")]
        [InlineData("display:grid; grid-template-columns:1fr", "break-after:avoid")]
        [InlineData("display:grid; grid-template-columns:1fr", "page-break-after:avoid")]
        public async Task AnAvoidedBreakBetweenTwoLines_TakesTheEarlierLineWithTheLater(
            string containerStyle, string declaration)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                // The first line ends at 160 and the second begins there, so the fragmentainer boundary
                // at 180 falls between them only once the second line has crossed it: filler 100pt puts
                // line one at 120-160 and line two at 160-200, straddling. Instead: size them so the
                // boundary falls exactly at the break point.
                LayoutHarness.Wrap(
                    "<div style='height:120pt'>filler</div>"
                    + $"<div id='c' style='{containerStyle}; width:300pt'>"
                    + $"<div id='a' style='width:100%;height:40pt;{declaration}'>A</div>"
                    + "<div id='b' style='width:100%;height:40pt;break-inside:avoid'>B</div>"
                    + "</div>"),
                pageHeight: PageHeight);

            var a = LayoutHarness.FindById(root, "a");
            var b = LayoutHarness.FindById(root, "b");
            Assert.NotNull(a);
            Assert.NotNull(b);

            // B moved to slot 1 because it may not be cut; A must follow it there rather than stay behind.
            Assert.Equal(1, SlotOf(container, b!));
            Assert.Equal(1, SlotOf(container, a!));
            Assert.True(a!.ActualBottom <= b!.Location.Y + 0.5,
                $"A must stay above B (A ends {a.ActualBottom:F1}, B starts {b.Location.Y:F1})");
        }

        // Read from the later side of the same break point: break-before: avoid on the line below says
        // exactly what break-after: avoid on the line above says, and neither side can veto the other.
        [Theory]
        [InlineData("display:flex; flex-wrap:wrap")]
        [InlineData("display:grid; grid-template-columns:1fr")]
        public async Task AnAvoidDeclaredOnTheLaterLine_TakesTheEarlierLineToo(string containerStyle)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='height:120pt'>filler</div>"
                    + $"<div id='c' style='{containerStyle}; width:300pt'>"
                    + "<div id='a' style='width:100%;height:40pt'>A</div>"
                    + "<div id='b' style='width:100%;height:40pt;break-before:avoid;break-inside:avoid'>B</div>"
                    + "</div>"),
                pageHeight: PageHeight);

            var a = LayoutHarness.FindById(root, "a");
            var b = LayoutHarness.FindById(root, "b");
            Assert.NotNull(a);
            Assert.NotNull(b);

            Assert.Equal(1, SlotOf(container, b!));
            Assert.Equal(1, SlotOf(container, a!));
        }

        // Nothing relocates the later line: the fragmentainer boundary simply falls at the break point
        // between the two, which is the plain form of the defect. The avoid moves the earlier line so the
        // break falls before it instead.
        [Theory]
        [InlineData("display:flex; flex-wrap:wrap")]
        [InlineData("display:grid; grid-template-columns:1fr")]
        public async Task AnAvoidWhereTheBoundaryFallsExactlyAtTheBreakPoint_MovesTheEarlierLine(
            string containerStyle)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                // Line one runs 140-180 and line two 180-220: the boundary at 180 is the break point.
                LayoutHarness.Wrap(
                    "<div style='height:120pt'>filler</div>"
                    + $"<div id='c' style='{containerStyle}; width:300pt'>"
                    + "<div id='a' style='width:100%;height:40pt;break-after:avoid'>A</div>"
                    + "<div id='b' style='width:100%;height:40pt'>B</div>"
                    + "</div>"),
                pageHeight: PageHeight);

            var a = LayoutHarness.FindById(root, "a");
            var b = LayoutHarness.FindById(root, "b");
            Assert.NotNull(a);
            Assert.NotNull(b);

            Assert.Equal(1, SlotOf(container, a!));
            Assert.Equal(1, SlotOf(container, b!));
            Assert.Equal(container.PageTopOf(1), a!.Location.Y, 1);
        }

        // §4.3: an avoid that cannot be satisfied is relaxed rather than abandoned wholesale. A chain of
        // three lines is trimmed from its front - the members nearest the break point are what the chain
        // is about - and where even one member does not fit, the break is taken as written.
        [Fact]
        public async Task AKeepWithNextChainTooTallForTheDestination_IsTrimmedFromItsFront()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='height:40pt'>filler</div>"
                    + "<div id='c' style='display:flex; flex-wrap:wrap; width:300pt'>"
                    + "<div id='i1' style='width:100%;height:60pt;break-after:avoid'>1</div>"
                    + "<div id='i2' style='width:100%;height:60pt;break-after:avoid'>2</div>"
                    + "<div id='i3' style='width:100%;height:60pt;break-inside:avoid'>3</div>"
                    + "</div>"),
                pageHeight: PageHeight);

            var i1 = LayoutHarness.FindById(root, "i1")!;
            var i2 = LayoutHarness.FindById(root, "i2")!;
            var i3 = LayoutHarness.FindById(root, "i3")!;

            // 60 + 60 + 60 = 180 does not fit a 160pt band, so the head is trimmed: line 1 stays and
            // lines 2 and 3 travel together.
            Assert.Equal(0, SlotOf(container, i1));
            Assert.Equal(1, SlotOf(container, i2));
            Assert.Equal(1, SlotOf(container, i3));
        }

        // The run has to start on the page being left. A chain reaching the fragmentainer's own content
        // top names every line on it, and moving all of them leaves the page blank and asks the same
        // question again forever - so the avoid is relaxed and the break is taken as written.
        [Fact]
        public async Task AKeepWithNextChainReachingTheTopOfItsOwnPage_IsRelaxed()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='c' style='display:flex; flex-wrap:wrap; width:300pt'>"
                    + "<div id='i1' style='width:100%;height:100pt;break-after:avoid'>1</div>"
                    + "<div id='i2' style='width:100%;height:100pt;break-inside:avoid'>2</div>"
                    + "</div>"),
                pageHeight: PageHeight);

            var i1 = LayoutHarness.FindById(root, "i1")!;
            var i2 = LayoutHarness.FindById(root, "i2")!;

            // Line 1 begins at the page's own content top; taking it along would empty the page.
            Assert.Equal(0, SlotOf(container, i1));
            Assert.Equal(1, SlotOf(container, i2));
        }

        // §3.1: a forced break value on either side of a pair takes precedence over an avoidance value on
        // the other, so a chain never reaches back through one.
        [Fact]
        public async Task AForcedBreakInsideAChain_StopsTheRunThere()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='height:40pt'>filler</div>"
                    + "<div id='c' style='display:flex; flex-wrap:wrap; width:300pt'>"
                    + "<div id='i1' style='width:100%;height:20pt;break-after:avoid'>1</div>"
                    + "<div id='i2' style='width:100%;height:20pt;break-before:page;break-after:avoid'>2</div>"
                    + "<div id='i3' style='width:100%;height:20pt;break-inside:avoid'>3</div>"
                    + "</div>"),
                pageHeight: PageHeight);

            var i1 = LayoutHarness.FindById(root, "i1")!;
            var i2 = LayoutHarness.FindById(root, "i2")!;

            // The forced break between lines 1 and 2 is the chain's end: line 1 stays on slot 0.
            Assert.Equal(0, SlotOf(container, i1));
            Assert.Equal(1, SlotOf(container, i2));
        }

        // A forced break at the very break point an avoid names wins outright (§3.1), so the earlier line
        // is not pulled along by its own break-after: avoid.
        [Fact]
        public async Task AForcedBreakAtTheSameBreakPointAsAnAvoid_Wins()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='height:40pt'>filler</div>"
                    + "<div id='c' style='display:flex; flex-wrap:wrap; width:300pt'>"
                    + "<div id='a' style='width:100%;height:20pt;break-after:avoid'>A</div>"
                    + "<div id='b' style='width:100%;height:20pt;break-before:page'>B</div>"
                    + "</div>"),
                pageHeight: PageHeight);

            Assert.Equal(0, SlotOf(container, LayoutHarness.FindById(root, "a")!));
            Assert.Equal(1, SlotOf(container, LayoutHarness.FindById(root, "b")!));
        }

        // A container whose lines the avoidance moved has to report a height that still holds them.
        [Theory]
        [InlineData("display:flex; flex-wrap:wrap")]
        [InlineData("display:grid; grid-template-columns:1fr")]
        public async Task AContainerWhoseAvoidMovedALine_ReportsAHeightThatHoldsIt(string containerStyle)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='height:120pt'>filler</div>"
                    + $"<div id='c' style='{containerStyle}; width:300pt'>"
                    + "<div id='a' style='width:100%;height:40pt;break-after:avoid'>A</div>"
                    + "<div id='b' style='width:100%;height:40pt'>B</div>"
                    + "</div>"),
                pageHeight: PageHeight);

            var c = LayoutHarness.FindById(root, "c")!;
            var b = LayoutHarness.FindById(root, "b")!;

            Assert.True(c.ActualBottom >= b.ActualBottom - 0.5,
                $"container ends at {c.ActualBottom:F1} but its content reaches {b.ActualBottom:F1}");
        }

        // An avoid between two lines that a boundary never falls between changes nothing.
        [Theory]
        [InlineData("display:flex; flex-wrap:wrap")]
        [InlineData("display:grid; grid-template-columns:1fr")]
        public async Task AnAvoidWithNoBoundaryBetweenTheLines_MovesNothing(string containerStyle)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    $"<div id='c' style='{containerStyle}; width:300pt'>"
                    + "<div id='a' style='width:100%;height:20pt;break-after:avoid'>A</div>"
                    + "<div id='b' style='width:100%;height:20pt'>B</div>"
                    + "</div>"),
                pageHeight: PageHeight);

            Assert.Equal(0, SlotOf(container, LayoutHarness.FindById(root, "a")!));
            Assert.Equal(0, SlotOf(container, LayoutHarness.FindById(root, "b")!));
        }

        // ─── Block-axis order is not list order ───────────────────────────────────

        // Two full-width lines in a wrap-reverse container: the *second* in flow is the one on top,
        // because the cross offsets are reversed after they are assigned. Filler puts the container's
        // top line across the boundary in the first fixture and its bottom line across it in the second.
        private static string WrapReverse(double filler, string aStyle, string bStyle) =>
            LayoutHarness.Wrap(
                $"<div style='height:{filler}pt'>filler</div>"
                + "<div id='c' style='display:flex; flex-wrap:wrap-reverse; width:300pt'>"
                + $"<div id='a' style='width:100%;height:40pt;{aStyle}'>A</div>"
                + $"<div id='b' style='width:100%;height:40pt;{bStyle}'>B</div>"
                + "</div>");

        // The line below a relocated one follows it — and under wrap-reverse the line below is the one
        // *earlier* in the list. Walking the list instead leaves it where it was, overlapping.
        [Fact]
        public async Task UnderWrapReverse_TheLineBelowARelocatedOne_FollowsIt()
        {
            // Container at 170; the top line (B, second in flow) spans 170–210 and crosses slot 0's
            // bottom at 180, so it moves to 180. A, below it at 210–250, has to move with it.
            var (root, container) = await LayoutHarness.LayoutAsync(
                WrapReverse(150, string.Empty, "break-inside:avoid"), pageHeight: PageHeight);

            var a = LayoutHarness.FindById(root, "a")!;
            var b = LayoutHarness.FindById(root, "b")!;

            // The fixture is only meaningful while the second line in flow really is the upper one.
            Assert.True(b.Location.Y < a.Location.Y,
                $"wrap-reverse should put B above A; B is at {b.Location.Y:F1} and A at {a.Location.Y:F1}");

            Assert.Equal(container.PageTopOf(1), b.Location.Y, 1);
            Assert.True(a.Location.Y >= b.ActualBottom - 0.5,
                $"A overlaps the line that moved: it starts at {a.Location.Y:F1} while B ends at "
                + $"{b.ActualBottom:F1}");
        }

        // The mirror image: the line that moves is the *last* in the list, so nothing above it may be
        // displaced. Accumulating onto the earlier entries pushes the line physically above it down.
        [Fact]
        public async Task UnderWrapReverse_ALineAboveARelocatedOne_StaysWhereItIs()
        {
            // Container at 120; B (upper) spans 120–160 entirely inside slot 0, and A (lower) spans
            // 160–200 across the boundary, so only A moves.
            var (root, container) = await LayoutHarness.LayoutAsync(
                WrapReverse(100, "break-inside:avoid", string.Empty), pageHeight: PageHeight);

            var a = LayoutHarness.FindById(root, "a")!;
            var b = LayoutHarness.FindById(root, "b")!;

            Assert.Equal(container.PageTopOf(1), a.Location.Y, 1);
            Assert.Equal(120, b.Location.Y, 1);
            Assert.Equal(0, SlotOf(container, b));
        }

        // §3.1 identifies a break point by *flow* order, and that identification still holds under
        // wrap-reverse: `break-after` on the first line's items and `break-before` on the second's name
        // the same break point. Only the geometry is reversed — that boundary sits *above* the first
        // line in flow, so it is that line which starts the new fragmentainer.
        [Theory]
        [InlineData("break-after:page", "")]
        [InlineData("page-break-after:always", "")]
        [InlineData("", "break-before:page")]
        public async Task UnderWrapReverse_AForcedBreakBetweenTheLines_MovesTheLowerOne(
            string aStyle, string bStyle)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                WrapReverse(40, aStyle, bStyle), pageHeight: PageHeight);

            var a = LayoutHarness.FindById(root, "a")!;
            var b = LayoutHarness.FindById(root, "b")!;

            Assert.Equal(0, SlotOf(container, b));
            Assert.Equal(1, SlotOf(container, a));
            Assert.Equal(container.PageTopOf(1), a.Location.Y, 1);

            // A slot index can be right while a page is lost downstream in the emitter, so the page
            // count is asserted too rather than inferred from it.
            Assert.NotNull(container.FragmentTree);
            Assert.Equal(2, container.FragmentTree!.Fragmentainers.Count);
        }

        // §3.1's break point before a container's first in-flow child *is* the break point before the
        // container, so it is the first line **in flow** that speaks for it — even under wrap-reverse,
        // where that line is the last one down the page. Forcing it starts the whole container on the
        // next page rather than tearing the bottom line off the rest.
        [Fact]
        public async Task UnderWrapReverse_AForcedBreakBeforeTheFirstLineInFlow_MovesTheWholeContainer()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                WrapReverse(40, "break-before:page", string.Empty), pageHeight: PageHeight);

            var a = LayoutHarness.FindById(root, "a")!;
            var b = LayoutHarness.FindById(root, "b")!;

            Assert.Equal(1, SlotOf(container, a));
            Assert.Equal(1, SlotOf(container, b));

            // And in the order wrap-reverse put them in: B above A, still touching.
            Assert.Equal(container.PageTopOf(1), b.Location.Y, 1);
            Assert.Equal(b.ActualBottom, a.Location.Y, 1);

            Assert.NotNull(container.FragmentTree);
            Assert.Equal(2, container.FragmentTree!.Fragmentainers.Count);
        }

        // Two wrap-reverse lines of *unequal* cross size. Whether a fragmentainer boundary falls between
        // two lines is only a question worth asking while they occupy separate block-axis ranges: B
        // (60pt) fills 120–180 and A (20pt) begins at 180, so the boundary is exactly at the break point
        // above A, and A's `break-after: avoid` forbids it — which brings B onto the next page too.
        // While the offsets were permuted the two lines were *nested* (B 120–180 with A inside it at
        // 140–160), no boundary fell between them, and the avoid had nothing to answer.
        [Fact]
        public async Task UnderWrapReverse_AnAvoidBetweenLinesOfUnequalCrossSize_MovesBoth()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='height:100pt'>filler</div>"
                    + "<div id='c' style='display:flex; flex-wrap:wrap-reverse; width:300pt'>"
                    + "<div id='a' style='width:100%;height:20pt;break-after:avoid'>A</div>"
                    + "<div id='b' style='width:100%;height:60pt'>B</div>"
                    + "</div>"),
                pageHeight: PageHeight);

            var a = LayoutHarness.FindById(root, "a")!;
            var b = LayoutHarness.FindById(root, "b")!;

            // The fixture is only meaningful while the two lines really are stacked, not nested.
            Assert.True(b.ActualBottom <= a.Location.Y + 0.5,
                $"the lines overlap: B runs to {b.ActualBottom:F1} and A starts at {a.Location.Y:F1}");

            Assert.Equal(1, SlotOf(container, a));
            Assert.Equal(1, SlotOf(container, b));
            Assert.Equal(container.PageTopOf(1), b.Location.Y, 1);
            Assert.Equal(b.ActualBottom, a.Location.Y, 1);

            Assert.NotNull(container.FragmentTree);
            Assert.Equal(2, container.FragmentTree!.Fragmentainers.Count);
        }

        // A wrapping column flex container stacks its lines along the *inline* axis: they sit side by
        // side sharing one block-axis range. Two items 40pt tall in a 60pt-tall column container take a
        // line each, beside one another.
        private static string WrappingColumn(double filler, string aStyle, string bStyle) =>
            LayoutHarness.Wrap(
                $"<div style='height:{filler}pt'>filler</div>"
                + "<div id='c' style='display:flex; flex-direction:column; flex-wrap:wrap;"
                + " height:60pt; width:300pt'>"
                + $"<div id='a' style='width:100pt;height:40pt;{aStyle}'>A</div>"
                + $"<div id='b' style='width:100pt;height:40pt;{bStyle}'>B</div>"
                + "</div>");

        // No fragmentainer boundary falls between two lines that share a block-axis range, so a break
        // value at that point names nothing this pass can act on.
        [Theory]
        [InlineData("break-after:page", "")]
        [InlineData("", "break-before:page")]
        public async Task InAWrappingColumnContainer_ABreakValueBetweenTheLines_MovesNothing(
            string aStyle, string bStyle)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                WrappingColumn(120, aStyle, bStyle), pageHeight: PageHeight);

            var a = LayoutHarness.FindById(root, "a")!;
            var b = LayoutHarness.FindById(root, "b")!;

            // The fixture asserts nothing unless the two items really are on separate lines beside
            // each other rather than stacked.
            Assert.True(b.Location.X > a.Location.X,
                $"expected B on a second line beside A; both are at x {a.Location.X:F1}");

            Assert.Equal(a.Location.Y, b.Location.Y, 1);
            Assert.Equal(0, SlotOf(container, a));
            Assert.Equal(0, SlotOf(container, b));
        }

        // The geometric half survives: a line holding something that may not be cut still moves, and
        // the line beside it moves with it rather than being left behind out of alignment.
        [Fact]
        public async Task InAWrappingColumnContainer_ALineThatMayNotBeCut_MovesEveryLineWithIt()
        {
            // Container at 170; both lines span 170–210 and cross slot 0's bottom at 180.
            var (root, container) = await LayoutHarness.LayoutAsync(
                WrappingColumn(150, string.Empty, "break-inside:avoid"), pageHeight: PageHeight);

            var a = LayoutHarness.FindById(root, "a")!;
            var b = LayoutHarness.FindById(root, "b")!;

            Assert.True(b.Location.X > a.Location.X,
                $"expected B on a second line beside A; both are at x {a.Location.X:F1}");

            Assert.Equal(container.PageTopOf(1), b.Location.Y, 1);
            Assert.Equal(b.Location.Y, a.Location.Y, 1);
        }

        // The one value a column container *does* read at the boundary above it is the break point
        // before its first in-flow child, which §3.1 makes the break point before the container. A
        // break-before on any *later* item names a boundary inside the container — between items in
        // the block axis — which this pass does not take. Reading the whole first line instead moves
        // content that sits before the break point along with it.
        [Fact]
        public async Task InAColumnContainer_AForcedBreakBeforeANonFirstItem_MovesNothing()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='height:60pt'>filler</div>"
                    + "<div id='c' style='display:flex; flex-direction:column; flex-wrap:wrap;"
                    + " height:60pt; width:300pt'>"
                    + "<div id='a' style='width:100pt;height:30pt'>A</div>"
                    + "<div id='b' style='width:100pt;height:30pt;break-before:page'>B</div>"
                    + "<div id='d' style='width:100pt;height:30pt'>D</div>"
                    + "</div>"),
                pageHeight: PageHeight);

            var a = LayoutHarness.FindById(root, "a")!;
            var b = LayoutHarness.FindById(root, "b")!;
            var d = LayoutHarness.FindById(root, "d")!;

            // The fixture asserts nothing unless A and B really share the first line and D wrapped.
            Assert.Equal(a.Location.X, b.Location.X, 1);
            Assert.True(d.Location.X > a.Location.X,
                $"expected D on a second line beside the first; it is at x {d.Location.X:F1}");

            Assert.Equal(80, a.Location.Y, 1);
            Assert.Equal(110, b.Location.Y, 1);
            Assert.Equal(80, d.Location.Y, 1);
            Assert.Equal(0, SlotOf(container, a));
            Assert.Equal(0, SlotOf(container, b));
            Assert.Equal(0, SlotOf(container, d));
        }

        // A column container that did not wrap has one line, which is all of its content — that line is
        // stacked in the block axis like any other and is relocated exactly as a row container's is.
        [Fact]
        public async Task InASingleLineColumnContainer_ALineThatMayNotBeCut_StillMoves()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                Document("display:flex; flex-direction:column", "break-inside: avoid"),
                pageHeight: PageHeight);

            var a = LayoutHarness.FindById(root, "a")!;

            Assert.Equal(1, SlotOf(container, a));
            Assert.True(a.ActualBottom <= container.PageBottomOf(1) + 0.5,
                $"expected the item whole in slot 1, it runs to {a.ActualBottom}");
        }
    }
}
