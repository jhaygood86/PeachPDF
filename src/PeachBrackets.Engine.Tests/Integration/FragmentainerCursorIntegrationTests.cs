using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A forced break steps a layout pass over slots without ending it, so the fragmentainer the pass is
    /// filling is not in general the one it opened with
    /// (<see href="https://www.w3.org/TR/css-break-3/#break-between">css-break-3 §3.1</see>). These pin
    /// what goes wrong when the cursor that names it does not follow: every later question about "the
    /// fragmentainer being filled" answers about a band the pass has already left.
    /// </summary>
    public class FragmentainerCursorIntegrationTests
    {
        private const double PageHeight = 200;
        private const double Margin = 20;

        /// <summary>Enough text for the block to run over several bands.</summary>
        private static string LongText() =>
            string.Join(" ", Enumerable.Range(0, 600).Select(i => "word" + i));

        [Fact]
        public async Task AForcedBreak_LandsOnThePageItNames_EvenWhenTheBoxCannotMeetItsOrphansMinimum()
        {
            // `orphans: 99` is a minimum no band here can satisfy, which is §5.4 read through §4.3's
            // relaxation ladder: the constraint is given up rather than acted on pointlessly. The gate that
            // gives it up is "is there anything above this box in the fragmentainer being filled?" - moving
            // a box that already starts at the top of one cannot buy it a single extra line.
            //
            // A box placed by a forced break *is* at the top of one. Read against the band the pass opened
            // with rather than the band the break stepped it to, it looked like a box with a whole page
            // above it, so the mover fired and pushed it one page further - leaving the page the forced
            // break named blank, which is exactly the page the author asked the content to start on.
            var html = LayoutHarness.Wrap(
                "<div id='a' style='height:50pt'>A</div>" +
                $"<div id='b' style='break-before: page; orphans: 99; font: 10pt Arial'>{LongText()}</div>");

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight, margin: Margin);

            var b = LayoutHarness.FindById(root, "b");
            Assert.NotNull(b);

            Assert.Equal(container.PageTopOf(1), b!.Location.Y, 6);

            // And no blank page in the middle: every fragmentainer from the first to the last carries
            // content. A cursor that stepped one slot too far would show up here the same way, as a slot
            // emitted with nothing in it.
            var slots = container.FragmentTree!.Fragmentainers.Select(f => f.SlotIndex).ToArray();
            Assert.Equal(Enumerable.Range(0, slots.Length).ToArray(), slots);
        }

        [Fact]
        public async Task AboveTheForcedBreaksBox_IsStillRoomAbove_ForWhatFollowsItOnThatPage()
        {
            // The other side of the same gate, and what a cursor that ran *ahead* of the pass would break.
            // Here the box with the unsatisfiable `orphans` minimum is not the one the forced break placed
            // - it follows it on the same page - so there genuinely is something above it in this
            // fragmentainer, and §5.4's mover is entitled to fire and start it in the next one.
            //
            // A cursor one slot too far on would put the band top below this box and answer "nothing above
            // it", silently giving up a constraint that was satisfiable.
            var html = LayoutHarness.Wrap(
                "<div id='a' style='height:50pt'>A</div>" +
                "<div id='b' style='break-before: page; font: 10pt Arial'>B</div>" +
                $"<div id='c' style='orphans: 99; font: 10pt Arial'>{LongText()}</div>");

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight, margin: Margin);

            var b = LayoutHarness.FindById(root, "b");
            var c = LayoutHarness.FindById(root, "c");
            Assert.NotNull(b);
            Assert.NotNull(c);

            Assert.Equal(container.PageTopOf(1), b!.Location.Y, 6);
            Assert.Equal(container.PageTopOf(2), c!.Location.Y, 6);
        }

        [Fact]
        public async Task AForcedBreakThatStepsOverAPage_StillLeavesTheSteppedOverPageBlank()
        {
            // The complement, and the reason the cursor cannot simply be "advance one per break": §3.1's
            // `right` forces one *or two* page breaks, and the page deliberately stepped over stays empty.
            // The cursor has to name the slot the box actually landed in - two on from the pass's own here,
            // not one.
            var html = LayoutHarness.Wrap(
                "<div id='a' style='height:50pt'>A</div>" +
                "<div id='b' style='break-before: right; font: 10pt Arial'>B</div>");

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight, margin: Margin);

            var b = LayoutHarness.FindById(root, "b");
            Assert.NotNull(b);

            // Slot 1 is the verso the `right` value refuses; the box opens slot 2.
            Assert.Equal(container.PageTopOf(2), b!.Location.Y, 6);

            var slots = container.FragmentTree!.Fragmentainers.Select(f => f.SlotIndex).ToArray();
            Assert.Equal([0, 1, 2], slots);
        }

        // The shapes most likely to have been relying on the cursor *not* advancing: content that
        // overflows the fragmentainer the break stepped to, and an engine that paginates its own children
        // starting there. A cursor that is wrong in one direction can be compensating for something wrong
        // in the other, and removing it leaves that one unopposed - so these pin the geometry rather than
        // merely asserting that layout completes.
        [Fact]
        public async Task AfterAForcedBreak_ABoxTallerThanTheBand_StillStartsOnThePageTheBreakNamed()
        {
            var html = LayoutHarness.Wrap(
                "<div id='x' style='height:50pt'>X</div>" +
                "<div id='tall' style='break-before: page; height: 900pt'>TALL</div>" +
                "<div id='after' style='font: 10pt Arial'>after</div>");

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight, margin: Margin);

            var tall = LayoutHarness.FindById(root, "tall");
            var after = LayoutHarness.FindById(root, "after");
            Assert.NotNull(tall);
            Assert.NotNull(after);

            // It overflows several bands rather than being broken - and the box after it picks up at its
            // bottom, not at the bottom of the band the pass opened with.
            Assert.Equal(container.PageTopOf(1), tall!.Location.Y, 6);
            Assert.Equal(tall.ActualBottom, after!.Location.Y, 6);

            // No contiguity assertion here, deliberately: the bands this box merely spans hold nothing to
            // paint - it has no background or border - so the emitter has nothing to put in them, which is
            // its own rule and not this cursor's business. What matters is that `after` is emitted in the
            // band it actually lands in rather than in one the pass named earlier.
            var slots = container.FragmentTree!.Fragmentainers.Select(f => f.SlotIndex).ToArray();
            Assert.Equal(container.SlotStartingAt(after.Location.Y), slots[^1]);
        }

        [Fact]
        public async Task AfterADirectionalBreak_ARepeatingHeaderTable_StartsOnTheSideItAsksFor()
        {
            var rows = string.Join("", Enumerable.Range(0, 40).Select(i =>
                $"<tr><td>row {i} cell a</td><td>row {i} cell b</td></tr>"));

            var html = LayoutHarness.Wrap(
                "<div id='x' style='height:50pt'>X</div>" +
                "<table id='t' style='break-before: right; font: 10pt Arial; width: 100%'>" +
                "<thead><tr><th>H1</th><th>H2</th></tr></thead><tbody>" + rows + "</tbody></table>");

            // A realistic page, where the table engine's own per-row pagination and repeated header are
            // the thing being stepped over to.
            var (root, container) = await LayoutHarness.LayoutAsync(html, pageHeight: 842, margin: Margin);

            var table = LayoutHarness.FindById(root, "t");
            Assert.NotNull(table);

            Assert.Equal(container.PageTopOf(2), table!.Location.Y, 6);
            Assert.True(table.ActualBottom > table.Location.Y, "the table laid out no rows at all");
        }
    }
}
