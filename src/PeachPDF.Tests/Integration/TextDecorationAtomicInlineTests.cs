using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Paint;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <see href="https://www.w3.org/TR/css-text-decor-3/#line-decoration">css-text-decor-3 §2.4</see>:
    /// "Atomic inlines, such as images and inline blocks, are not decorated." A decoration line has to
    /// break around one and resume after it, rather than running straight through.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Do not try to verify this by rasterizing.</b> The deviation these tests cover was invisible on
    /// a rendered page for exactly one reason: the common atomic inlines paint opaque content over the
    /// place the wrong line was, so a raster shows the gap the spec wants and hides the reason it is
    /// there. The honest check is the sequence of draw calls, which is what
    /// <see cref="TestRecordingGraphics"/> records - so every assertion here is on segment count and
    /// extent, never on pixels.
    /// </para>
    /// <para>
    /// The fixtures use <c>inline-block</c>/<c>inline-table</c> rather than <c>&lt;img&gt;</c> for the
    /// same reason the issue's own repro did: no image asset is needed to make the point, and an
    /// explicit width makes the expected gap a literal number.
    /// </para>
    /// </remarks>
    public class TextDecorationAtomicInlineTests
    {
        [Fact]
        public async Task BlockUnderline_BreaksAroundAnAtomicInlineInTheMiddleOfTheLine()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(Wrap(
                "<div id='d' style='width:400pt; text-decoration:underline'>AA "
                + "<span id='a' style='display:inline-block; width:60pt'>hidden</span> BB</div>"));
            var d = LayoutHarness.FindById(root, "d")!;
            var a = LayoutHarness.FindById(root, "a")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, d, g);

            var lines = Lines(g);

            Assert.Equal(2, lines.Count);
            Assert.Equal(lines[0].Y1, lines[1].Y1, 3);

            Assert.True(lines[0].X2 <= Left(a) + 0.01,
                $"the leading segment ({lines[0].X2}) should stop at the atomic inline's left edge ({Left(a)})");
            Assert.True(lines[1].X1 >= Right(a) - 0.01,
                $"the trailing segment ({lines[1].X1}) should resume at the atomic inline's right edge");
            Assert.Equal(Right(a) - Left(a), lines[1].X1 - lines[0].X2, 1);
        }

        [Fact]
        public async Task BlockUnderline_BreaksAroundAnAtomicInlineWhoseContentIsBlockLevel()
        {
            // The other of the two layout paths an inline-block can take: block-level content routes it
            // through CssLayoutEngine.FlowAtomicBlockContentChild, which registers its own rectangle in
            // the line directly rather than through the inline UpdateRectangle walk. Both must be
            // excluded, and recognizing an atomic inline by display type rather than by fragment shape
            // is what makes one predicate cover both.
            var (root, container) = await LayoutHarness.LayoutAsync(Wrap(
                "<div id='d' style='width:400pt; text-decoration:underline'>AA "
                + "<span id='a' style='display:inline-block; width:60pt'><div>y</div></span> BB</div>"));
            var d = LayoutHarness.FindById(root, "d")!;
            var a = LayoutHarness.FindById(root, "a")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, d, g);

            var lines = Lines(g);

            Assert.Equal(2, lines.Count);
            Assert.True(lines[0].X2 <= Left(a) + 0.01);
            Assert.True(lines[1].X1 >= Right(a) - 0.01);
            Assert.Equal(60, Right(a) - Left(a), 1);
            Assert.Equal(60, lines[1].X1 - lines[0].X2, 1);
        }

        [Fact]
        public async Task BlockUnderline_ExcludesTheAtomicInlinesMarginBoxNotItsBorderBox()
        {
            // §2.4 says only that an atomic inline is not decorated, never which of its boxes bounds the
            // gap - taking the margin box is this engine's own choice (see DecorationContent.
            // RecordExclusions). Every rectangle layout records is a border box, so the margins have to
            // be added back, or the line runs under them.
            var (root, container) = await LayoutHarness.LayoutAsync(Wrap(
                "<div id='d' style='width:400pt; text-decoration:underline'>AA "
                + "<span id='a' style='display:inline-block; width:60pt; margin:0 12pt'>x</span> BB</div>"));
            var d = LayoutHarness.FindById(root, "d")!;
            var a = LayoutHarness.FindById(root, "a")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, d, g);

            var lines = Lines(g);

            Assert.Equal(2, lines.Count);
            Assert.Equal(Left(a) - 12, lines[0].X2, 1);
            Assert.Equal(Right(a) + 12, lines[1].X1, 1);
        }

        [Fact]
        public async Task BlockUnderline_StopsBeforeAnAtomicInlineThatEndsTheLine()
        {
            // Nothing follows it, so there is no trailing segment to leave behind: the span never
            // reaches the atomic inline in the first place, and one unbroken line is still correct.
            var (root, container) = await LayoutHarness.LayoutAsync(Wrap(
                "<div id='d' style='width:400pt; text-decoration:underline'>AA "
                + "<span id='a' style='display:inline-block; width:60pt'>x</span></div>"));
            var d = LayoutHarness.FindById(root, "d")!;
            var a = LayoutHarness.FindById(root, "a")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, d, g);

            var line = Assert.Single(Lines(g));

            Assert.True(line.X2 <= Left(a) + 0.01,
                $"the underline ({line.X2}) should stop at the atomic inline at {Left(a)}");
        }

        [Fact]
        public async Task BlockUnderline_WithOnlyAnAtomicInlineOnTheLine_DrawsNothing()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(Wrap(
                "<div id='d' style='width:400pt; text-decoration:underline'>"
                + "<span style='display:inline-block; width:60pt'>x</span></div>"));
            var d = LayoutHarness.FindById(root, "d")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, d, g);

            Assert.Empty(Lines(g));
        }

        [Fact]
        public async Task BlockUnderline_BreaksAroundAnAtomicInlineNestedInsideAnInlineBox()
        {
            // The <span> is line-hosted, so its own rectangle is what widens the line's span - but the
            // inline-block inside it is still an atomic inline the line has to break around. Ending the
            // walk at the first line-hosted box, which is what it used to do, hid exactly this case.
            var (root, container) = await LayoutHarness.LayoutAsync(Wrap(
                "<div id='d' style='width:400pt; text-decoration:underline'>AA <span>xx "
                + "<span id='a' style='display:inline-block; width:60pt'>y</span> zz</span> BB</div>"));
            var d = LayoutHarness.FindById(root, "d")!;
            var a = LayoutHarness.FindById(root, "a")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, d, g);

            var lines = Lines(g);

            Assert.Equal(2, lines.Count);
            Assert.True(lines[0].X2 <= Left(a) + 0.01);
            Assert.True(lines[1].X1 >= Right(a) - 0.01);
            Assert.True(lines[1].X2 > lines[1].X1, "the trailing segment must still cover \"zz BB\"");
        }

        [Fact]
        public async Task InlineUnderline_BreaksAroundAnAtomicInlineInsideIt()
        {
            // The ordinary per-line inline path, not the block-propagated one: the underlined <span>'s
            // own per-line rectangle already covers the inline-block, so the exclusion is the only
            // thing that can break the line there.
            var (root, container) = await LayoutHarness.LayoutAsync(Wrap(
                "<div style='width:400pt'><span id='s' style='text-decoration:underline'>AA "
                + "<span id='a' style='display:inline-block; width:60pt'>x</span> BB</span></div>"));
            var s = LayoutHarness.FindById(root, "s")!;
            var a = LayoutHarness.FindById(root, "a")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, s, g);

            var lines = Lines(g);

            Assert.Equal(2, lines.Count);
            Assert.True(lines[0].X2 <= Left(a) + 0.01);
            Assert.True(lines[1].X1 >= Right(a) - 0.01);
        }

        [Fact]
        public async Task BlockUnderline_BreaksAroundAnInlineTable()
        {
            // An atomic inline is recognized by its own display type, not by the layout path it took -
            // an inline-table reaches paint through the table engine, not the inline-block one.
            var (root, container) = await LayoutHarness.LayoutAsync(Wrap(
                "<div id='d' style='width:400pt; text-decoration:underline'>AA "
                + "<table id='a' style='display:inline-table; width:60pt'><tr><td>x</td></tr></table> BB</div>"));
            var d = LayoutHarness.FindById(root, "d")!;
            var a = LayoutHarness.FindById(root, "a")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, d, g);

            var lines = Lines(g);

            Assert.Equal(2, lines.Count);
            Assert.True(lines[0].X2 <= Left(a) + 0.01);
        }

        [Fact]
        public async Task BlockUnderline_WithNoAtomicInline_StillDrawsOneUnbrokenLine()
        {
            // The subtraction has to be a no-op for the overwhelmingly common case.
            var (root, container) = await LayoutHarness.LayoutAsync(Wrap(
                "<div id='d' style='width:400pt; text-decoration:underline'>AA <span>BB</span> CC</div>"));
            var d = LayoutHarness.FindById(root, "d")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, d, g);

            Assert.Single(Lines(g));
        }

        [Fact]
        public async Task BlockUnderlineOverline_BothLinesBreakAroundTheSameAtomicInline()
        {
            // Every keyword in text-decoration-line gets the same segments - the subtraction happens
            // once, above the per-keyword loop.
            var (root, container) = await LayoutHarness.LayoutAsync(Wrap(
                "<div id='d' style='width:400pt; text-decoration:underline overline'>AA "
                + "<span style='display:inline-block; width:60pt'>x</span> BB</div>"));
            var d = LayoutHarness.FindById(root, "d")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, d, g);

            var lines = Lines(g);

            Assert.Equal(4, lines.Count);
            Assert.Equal(2, lines.Select(l => System.Math.Round(l.Y1, 2)).Distinct().Count());
            Assert.Equal(2, lines.Select(l => System.Math.Round(l.X1, 2)).Distinct().Count());
        }

        [Fact]
        public async Task BlockUnderline_BreaksAroundEachOfSeveralAtomicInlinesOnOneLine()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(Wrap(
                "<div id='d' style='width:400pt; text-decoration:underline'>AA "
                + "<span style='display:inline-block; width:40pt'>x</span> BB "
                + "<span style='display:inline-block; width:40pt'>y</span> CC</div>"));
            var d = LayoutHarness.FindById(root, "d")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, d, g);

            Assert.Equal(3, Lines(g).Count);
        }

        [Fact]
        public async Task BlockUnderline_BreaksAroundAnAtomicInlineOnTheLineItIsOn_NotEveryLine()
        {
            // Exclusions are per line box: the atomic inline on line 1 must not punch a hole in line 2.
            var (root, container) = await LayoutHarness.LayoutAsync(Wrap(
                "<div id='d' style='width:120pt; font-size:10pt; text-decoration:underline'>AA "
                + "<span id='a' style='display:inline-block; width:60pt'>x</span> BB<br>"
                + "second line here</div>"));
            var d = LayoutHarness.FindById(root, "d")!;
            var a = LayoutHarness.FindById(root, "a")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, d, g);

            var lines = Lines(g);
            var firstLineY = lines.Min(l => l.Y1);
            var secondLine = lines.Where(l => l.Y1 > firstLineY + 1).ToList();

            Assert.True(lines.Count(l => System.Math.Abs(l.Y1 - firstLineY) <= 1) == 2,
                "the line holding the atomic inline should be drawn as two segments");
            Assert.Single(secondLine);
            Assert.True(secondLine[0].X1 < Left(a),
                "the second line's own span must not be cut by the first line's atomic inline");
        }

        [Fact]
        public async Task VerticalWritingMode_DrawsItsDecorationUncut()
        {
            // Both decoration subtractions reason along the x-axis, so neither is applied under a vertical
            // writing mode: there a box's physical x-range is the column's thickness rather than its
            // extent along the line, and subtracting it would delete the decoration instead of breaking
            // it. Vertical decoration geometry is out of scope as a whole, and this pins that the change
            // does not make it worse.
            //
            // The guard in PaintDecoration is DEFENSIVE, not currently reachable: CssLayoutEngine's
            // vertical path records no per-line rectangle for an atomic inline at all, so the walk finds
            // nothing to exclude in the first place (verified by probing CssBox.Rectangles for the
            // inline-block below - it is empty). It is kept because that is a layout fact, not a paint
            // one, and a future vertical-layout improvement that starts recording those rectangles would
            // otherwise silently start deleting vertical decorations.
            var (root, container) = await LayoutHarness.LayoutAsync(Wrap(
                "<div id='d' style='writing-mode:vertical-rl; height:300pt; text-decoration:underline'>AA "
                + "<span style='display:inline-block; width:40pt; height:14pt'><div>x</div></span> BB</div>"));
            var d = LayoutHarness.FindById(root, "d")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, d, g);

            var line = Assert.Single(Lines(g));
            Assert.True(line.X2 - line.X1 > 1, "the vertical block's decoration should still be drawn with real extent");
        }

        // ─── The subtraction itself ──────────────────────────────────────────────

        [Fact]
        public void Subtract_WithNoExclusions_ReturnsTheWholeSpan()
        {
            var segments = DecorationSegments.Subtract(new DecorationInterval(0, 100), []);

            var only = Assert.Single(segments);
            Assert.Equal(new DecorationInterval(0, 100), only);
        }

        [Fact]
        public void Subtract_WithAnExclusionInTheMiddle_ReturnsTwoSegments()
        {
            var segments = DecorationSegments.Subtract(new DecorationInterval(0, 100),
                [new DecorationInterval(40, 60)]);

            Assert.Equal([new DecorationInterval(0, 40), new DecorationInterval(60, 100)], segments);
        }

        [Fact]
        public void Subtract_MergesOverlappingAndUnorderedExclusions()
        {
            // Skip-ink dilates every ink interval by its clearance, so neighbouring letters routinely
            // produce exclusions that overlap - and nothing promises they arrive in order.
            var segments = DecorationSegments.Subtract(new DecorationInterval(0, 100),
                [new DecorationInterval(70, 80), new DecorationInterval(30, 50),
                 new DecorationInterval(45, 60), new DecorationInterval(75, 78)]);

            Assert.Equal([
                new DecorationInterval(0, 30),
                new DecorationInterval(60, 70),
                new DecorationInterval(80, 100)], segments);
        }

        [Fact]
        public void Subtract_DropsSegmentsShorterThanTheMinimum()
        {
            // Two exclusions a hair apart would otherwise leave a meaningless tick between them.
            var segments = DecorationSegments.Subtract(new DecorationInterval(0, 100),
                [new DecorationInterval(0, 50), new DecorationInterval(50.05, 100)]);

            Assert.Empty(segments);
        }

        [Fact]
        public void Subtract_IgnoresExclusionsThatMissTheSpan()
        {
            var segments = DecorationSegments.Subtract(new DecorationInterval(40, 60),
                [new DecorationInterval(0, 20), new DecorationInterval(80, 100)]);

            Assert.Equal([new DecorationInterval(40, 60)], segments);
        }

        [Fact]
        public void Subtract_WithAnExclusionCoveringTheWholeSpan_ReturnsNothing()
        {
            Assert.Empty(DecorationSegments.Subtract(new DecorationInterval(10, 90),
                [new DecorationInterval(0, 100)]));
        }

        [Fact]
        public void Subtract_WithAnExclusionOverlappingOneEnd_TrimsRatherThanSplits()
        {
            Assert.Equal([new DecorationInterval(30, 100)],
                DecorationSegments.Subtract(new DecorationInterval(0, 100),
                    [new DecorationInterval(-10, 30)]));

            Assert.Equal([new DecorationInterval(0, 70)],
                DecorationSegments.Subtract(new DecorationInterval(0, 100),
                    [new DecorationInterval(70, 120)]));
        }

        [Fact]
        public void Subtract_DoesNotMutateTheCallersExclusionList()
        {
            // One exclusion list per line box is reused across every keyword a text-decoration-line
            // value names, so sorting it in place would be a real defect.
            List<DecorationInterval> exclusions =
                [new DecorationInterval(70, 80), new DecorationInterval(30, 50)];

            DecorationSegments.Subtract(new DecorationInterval(0, 100), exclusions);

            Assert.Equal([new DecorationInterval(70, 80), new DecorationInterval(30, 50)], exclusions);
        }

        [Fact]
        public void Subtract_WithAnEmptySpan_ReturnsNothing()
        {
            Assert.Empty(DecorationSegments.Subtract(new DecorationInterval(50, 50), []));
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        /// <summary>
        /// The left edge of <paramref name="box"/>'s own border box on the line it sits on. Read from
        /// its per-line rectangle rather than <c>CssBox.Location</c>, which for an inline-block whose
        /// content is inlines-only stays at a line-local value layout never updates (see
        /// <c>CssLineBox.AssignRectanglesToBoxes</c>) - these rectangles are the box's real geometry.
        /// </summary>
        private static double Left(CssBox box) => box.Rectangles.Values.Single().Left;

        /// <summary>The right edge of <paramref name="box"/>'s own border box; see <see cref="Left"/>.</summary>
        private static double Right(CssBox box) => box.Rectangles.Values.Single().Right;

        private static List<TestRecordingGraphics.DrawLineCall> Lines(TestRecordingGraphics g) =>
            g.Log.OfType<TestRecordingGraphics.DrawLineCall>().OrderBy(l => l.Y1).ThenBy(l => l.X1).ToList();

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body style='margin:0'>{body}</body></html>";
    }
}
