using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Issue #1146: <c>text-underline-position</c>'s <c>left</c>/<c>right</c> half
    /// (<see href="https://www.w3.org/TR/css-text-decor-4/#text-underline-position-property">css-text-decor-4
    /// §2.5</see>) pins the underline to a literal physical edge under a true vertical writing mode,
    /// switching a same-line overline to the opposite edge when the two would otherwise collide. See
    /// <see cref="TextUnderlineOffsetPositionTests"/> for the <c>auto</c>/<c>from-font</c>/<c>under</c>
    /// half and <c>CssUtilsTests</c> for the compound grammar's own parser tests.
    /// </summary>
    public class TextUnderlinePositionSideTests
    {
        [Theory]
        [InlineData("vertical-rl", "left")]
        [InlineData("vertical-rl", "right")]
        [InlineData("vertical-lr", "left")]
        [InlineData("vertical-lr", "right")]
        public async Task Underline_PinnedToLiteralSide_DrawsInwardFromThatPhysicalEdge(string writingMode, string side)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div style=\"writing-mode:{writingMode}; height:300pt\">" +
                $"<span id='s' style='text-decoration:underline; text-underline-position:{side}'>text</span></div>"),
                margin: 0);
            var s = LayoutHarness.FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, s, g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            var rect = s.Rectangles.Values.Single();
            var edge = side == "left" ? rect.Left : rect.Right;
            var inwardSign = side == "left" ? 1 : -1;

            // A true vertical mode's cross-axis coordinate (physical X) is carried on both X1 and X2 -
            // see StrokeDecorationSegment's isVertical Draw local, which calls DrawLine(pen, at, x1, at, x2).
            Assert.Equal(line.X1, line.X2, 3);
            Assert.True(inwardSign * (line.X1 - edge) > 0,
                $"expected the underline to sit inward from the pinned '{side}' edge ({edge}), got X={line.X1}");
        }

        [Theory]
        [InlineData("vertical-rl", "right")] // vertical-rl's own "over" side is physical right
        [InlineData("vertical-lr", "left")]  // vertical-lr's own "over" side is physical left
        public async Task PinningTheUnderlineToTheOverSide_SwitchesTheOverlineToTheOppositeEdge(string writingMode, string conflictingSide)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div style=\"writing-mode:{writingMode}; height:300pt\">" +
                "<span id='s' style='text-decoration:underline overline; " +
                $"text-underline-position:{conflictingSide}'>text</span></div>"),
                margin: 0);
            var s = LayoutHarness.FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, s, g);

            var lines = g.Log.OfType<TestRecordingGraphics.DrawLineCall>().ToList();
            Assert.Equal(2, lines.Count);

            var rect = s.Rectangles.Values.Single();
            var pinnedEdge = conflictingSide == "left" ? rect.Left : rect.Right;
            var oppositeEdge = conflictingSide == "left" ? rect.Right : rect.Left;

            // The underline pinned to the "over" edge would collide with an unmoved overline still
            // drawn there - css-text-decor-4 §2.5 moves the overline to the opposite ("under") edge
            // instead, so one of the two drawn lines should sit exactly there (no clearance adjustment
            // applies to an overline's plain position), and none should remain at the original over edge.
            Assert.Contains(lines, l => System.Math.Abs(l.X1 - oppositeEdge) < 0.01);
            Assert.DoesNotContain(lines, l => System.Math.Abs(l.X1 - pinnedEdge) < 0.01);
        }

        [Theory]
        [InlineData("vertical-rl", "left")]  // vertical-rl's own "under" side is physical left - no conflict
        [InlineData("vertical-lr", "right")] // vertical-lr's own "under" side is physical right - no conflict
        public async Task PinningTheUnderlineToTheUnderSide_LeavesTheOverlineWhereItAlwaysWas(string writingMode, string nonConflictingSide)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div style=\"writing-mode:{writingMode}; height:300pt\">" +
                "<span id='s' style='text-decoration:underline overline; " +
                $"text-underline-position:{nonConflictingSide}'>text</span></div>"),
                margin: 0);
            var s = LayoutHarness.FindById(root, "s")!;

            var withSide = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, s, withSide);

            var (rootAuto, containerAuto) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div style=\"writing-mode:{writingMode}; height:300pt\">" +
                "<span id='s' style='text-decoration:underline overline'>text</span></div>"),
                margin: 0);
            var sAuto = LayoutHarness.FindById(rootAuto, "s")!;

            var withoutSide = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(containerAuto, sAuto, withoutSide);

            var overlineWith = withSide.Log.OfType<TestRecordingGraphics.DrawLineCall>()
                .OrderBy(l => l.X1).First();
            var overlineWithout = withoutSide.Log.OfType<TestRecordingGraphics.DrawLineCall>()
                .OrderBy(l => l.X1).First();

            Assert.Equal(overlineWithout.X1, overlineWith.X1, 3);
        }

        [Theory]
        [InlineData("left")]
        [InlineData("right")]
        public async Task HorizontalWritingMode_SideValue_ParsesButHasNoVisibleEffect(string side)
        {
            var withoutSide = await HorizontalUnderlineXAsync("");
            var withSide = await HorizontalUnderlineXAsync($"text-underline-position:{side}");

            Assert.Equal(withoutSide, withSide, 3);
        }

        [Theory]
        [InlineData("vertical-rl", "left")]
        [InlineData("vertical-rl", "right")]
        [InlineData("vertical-lr", "left")]
        [InlineData("vertical-lr", "right")]
        public async Task TextUnderlineOffset_MovesThePinnedUnderlineFurtherFromTheText(string writingMode, string side)
        {
            // A positive text-underline-offset must move the underline further from the text - i.e.
            // further past the pinned edge (outward), not back toward the glyphs (inward), regardless of
            // which literal side it is pinned to. An earlier version of this fix had the offset and
            // clearance terms sharing the same sign, which moved the line the wrong way whenever offset
            // was non-zero (invisible when offset was 0, which is why the other tests above don't catch
            // it - see ResolveUnderlineCross's own remarks on the clearance/offset sign relationship).
            var withoutOffset = await VerticalUnderlineXAsync(writingMode, side, "");
            var withOffset = await VerticalUnderlineXAsync(writingMode, side, "text-underline-offset:5pt");

            var outwardSign = side == "left" ? -1 : 1;
            Assert.Equal(withoutOffset + outwardSign * 5, withOffset, 3);
        }

        [Theory]
        [InlineData("vertical-rl", "right")] // conflicts with vertical-rl's own "over" (physical right) side
        [InlineData("vertical-lr", "left")]  // conflicts with vertical-lr's own "over" (physical left) side
        public async Task DoubleStyle_SwitchedOverline_GrowsAwayFromTheText_NotBackIntoIt(string writingMode, string conflictingSide)
        {
            // Issue found during #1146's own review: the double-style's second stroke direction was
            // computed from the keyword name alone (overline grows toward "over"), not from where the
            // keyword's line actually ended up - so a switched overline (now physically on the "under"
            // edge) grew its second stroke back toward "over", i.e. back through the interior where the
            // pinned underline now sits, instead of continuing outward past its own new edge.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div style=\"writing-mode:{writingMode}; height:300pt\">" +
                "<span id='s' style='text-decoration:underline overline; text-decoration-style:double; " +
                $"text-underline-position:{conflictingSide}'>text</span></div>"),
                margin: 0);
            var s = LayoutHarness.FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, s, g);

            var lines = g.Log.OfType<TestRecordingGraphics.DrawLineCall>().ToList();
            Assert.Equal(4, lines.Count); // double style: 2 strokes each for underline and overline

            var rect = s.Rectangles.Values.Single();
            var oppositeEdge = conflictingSide == "left" ? rect.Right : rect.Left;
            var outwardSign = conflictingSide == "left" ? 1 : -1; // "outward" past the opposite edge

            // The switched overline's two strokes both sit at or beyond the opposite edge - neither
            // one crosses back over it into the interior, where the pinned underline's own strokes are.
            var overlineLines = lines.Where(l => outwardSign * (l.X1 - oppositeEdge) >= -0.5).ToList();
            Assert.Equal(2, overlineLines.Count);
            Assert.All(overlineLines, l => Assert.True(outwardSign * (l.X1 - oppositeEdge) >= -0.5,
                $"expected the switched overline's stroke at X={l.X1} to stay at or outward of the opposite edge ({oppositeEdge})"));
        }

        [Theory]
        [InlineData("vertical-rl", "right", "padding-right")] // vertical-rl's block-start (over) edge is physical right
        [InlineData("vertical-lr", "left", "padding-left")]   // vertical-lr's block-start (over) edge is physical left
        public async Task PinnedToTheBlockStartSide_UsesThatSidesOwnPaddingForItsInset(string writingMode, string side, string paddingProperty)
        {
            // Issue found during #1146's own review: the block-end padding/border inset was always
            // taken from the block-end physical side regardless of which edge a keyword's line actually
            // landed on - so a pinned-to-block-start underline (issue #1146) ignored that side's own
            // padding and used the opposite (block-end) side's instead.
            var withPadding = await VerticalUnderlineXAsync(writingMode, side, $"{paddingProperty}:20pt");
            var withoutPadding = await VerticalUnderlineXAsync(writingMode, side, "");

            var inwardSign = side == "left" ? 1 : -1;
            Assert.Equal(withoutPadding + inwardSign * 20, withPadding, 3);
        }

        [Theory]
        [InlineData("vertical-rl", "right")] // vertical-rl's own "over" side is physical right
        [InlineData("vertical-lr", "left")]  // vertical-lr's own "over" side is physical left
        public async Task OverlineAlone_WithAConflictingSideValue_DoesNotSwitch_NoUnderlineToCollideWith(string writingMode, string conflictingSide)
        {
            // The spec's own switch note ("if this causes the underline to be drawn on the over side...")
            // is conditioned on an underline actually being drawn - a box with only "overline" declared
            // has no underline to collide with, so text-underline-position must not move its overline.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div style=\"writing-mode:{writingMode}; height:300pt\">" +
                $"<span id='s' style='text-decoration:overline; text-underline-position:{conflictingSide}'>text</span></div>"),
                margin: 0);
            var s = LayoutHarness.FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, s, g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            var rect = s.Rectangles.Values.Single();
            var expectedOverPos = writingMode == "vertical-rl" ? rect.Right : rect.Left;

            Assert.Equal(expectedOverPos, line.X1, 3);
        }

        [Fact]
        public async Task InheritedThroughTheFullCascade_SurvivesOnANonDeclaringDescendant()
        {
            // Regression for the customSetter/RMW shape this property shares with position/
            // RunningElementName: TextUnderlineSide has no css-properties.json entry of its own, so it
            // is never independently reasserted by DomParser.CascadeApplyStyles's defaulting loop - but
            // proving that needs the REAL cascade (CascadeApplyStyles then CssBox.InheritStyle), not a
            // direct CssUtils.SetPropertyValue call, which bypasses both entirely.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='parent' style='text-underline-position:under left'>" +
                "<span id='child' style='color:red'>text</span></div>"),
                margin: 0);
            _ = container;
            var child = LayoutHarness.FindById(root, "child")!;

            Assert.Equal(global::PeachPDF.CSS.TextUnderlinePosition.Under, child.TextUnderlinePosition.Value);
            Assert.Equal(global::PeachPDF.CSS.TextUnderlineSide.Left, child.TextUnderlineSide.Value);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static async Task<double> VerticalUnderlineXAsync(string writingMode, string side, string extraStyle)
        {
            var style = string.IsNullOrEmpty(extraStyle)
                ? $"text-decoration:underline; text-underline-position:{side}"
                : $"text-decoration:underline; text-underline-position:{side}; {extraStyle}";
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div style=\"writing-mode:{writingMode}; height:300pt\">" +
                $"<span id='s' style='{style}'>text</span></div>"),
                margin: 0);
            var s = LayoutHarness.FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, s, g);

            return Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>()).X1;
        }

        private static async Task<double> HorizontalUnderlineXAsync(string extraStyle)
        {
            var style = string.IsNullOrEmpty(extraStyle)
                ? "text-decoration:underline"
                : $"text-decoration:underline; {extraStyle}";
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap($"<span id='s' style='{style}'>text</span>"), margin: 0);
            var s = LayoutHarness.FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, s, g);

            // Horizontal decoration geometry carries its cross-axis coordinate on Y (see
            // TextUnderlineOffsetPositionTests) - X1 here is only the span start, which text-underline-
            // position never moves, but reading Y proves the position itself is unaffected by 'left'/
            // 'right' the same way X1 would for the span.
            return Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>()).Y1;
        }
    }
}
