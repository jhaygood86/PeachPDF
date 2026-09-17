using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using static PeachPDF.Tests.TestSupport.LayoutHarness;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// The block-axis counterpart of <see cref="InlineBlockDeclaredWidthGeometryTests"/>: a declared
    /// <c>height</c>/<c>min-height</c> sizes a non-replaced inline-block's own painted box, not just
    /// whatever its content happened to measure — nothing at all, for an empty one. CSS 2.1
    /// <see href="https://www.w3.org/TR/CSS22/visudet.html#normal-block">§10.6.3</see> makes a
    /// non-auto <c>height</c> the used content height regardless of content.
    /// <para>
    /// Asserted on the box's per-line rectangle, exactly as the width geometry tests are — for an
    /// inline box that <i>is</i> its painted geometry, since its own <c>Location</c>/<c>Size</c> never
    /// name a position on the page. Page margins are 20pt, so the first inline box on the first line
    /// starts at y=20.
    /// </para>
    /// </summary>
    public class InlineBlockDeclaredHeightGeometryTests
    {
        /// <summary>
        /// The issue's own reproduction, in points: inline-blocks that differ only in what (if anything)
        /// is inside them all paint the one height they declare.
        /// </summary>
        [Theory]
        // One line of text nowhere near the declared height.
        [InlineData("<span id='box' style='display:inline-block;height:40pt'>x</span>", 40.0)]
        // Nothing inside at all - no words to measure, so this one used to paint no rectangle at all.
        [InlineData("<span id='box' style='display:inline-block;height:40pt'></span>", 40.0)]
        // ...and with a border, which used to be the whole of its painted height.
        [InlineData("<span id='box' style='display:inline-block;height:40pt;border:1pt solid'></span>", 42.0)]
        // Padding and border on a box that does hold content: 40 + 2x3 padding + 2x1.5 border.
        [InlineData("<span id='box' style='display:inline-block;height:40pt;padding:3pt;border:1.5pt solid'>x</span>", 49.0)]
        public async Task ADeclaredHeightSizesThePaintedBorderBox(string markup, double expectedHeight)
        {
            var (root, _) = await LayoutAsync(Wrap($"<div style='width:400pt'>{markup}</div>"));
            var box = FindById(root, "box")!;

            Assert.Equal(expectedHeight, PaintedRectOf(box).Height, 3);
        }

        /// <summary>
        /// The issue's own note: <c>min-height</c> behaves identically to <c>height</c> on every one of
        /// its repro shapes, since none of them have content taller than the declared value.
        /// </summary>
        [Theory]
        [InlineData("<span id='box' style='display:inline-block;min-height:40pt'>x</span>", 40.0)]
        [InlineData("<span id='box' style='display:inline-block;min-height:40pt'></span>", 40.0)]
        public async Task AMinHeightSizesThePaintedBoxTheSameWayAnExplicitHeightDoes(string markup, double expectedHeight)
        {
            var (root, _) = await LayoutAsync(Wrap($"<div style='width:400pt'>{markup}</div>"));

            Assert.Equal(expectedHeight, PaintedRectOf(FindById(root, "box")!).Height, 3);
        }

        /// <summary>
        /// <c>min-height</c> is a floor, not an override: a box whose natural content is already taller
        /// than its <c>min-height</c> keeps its natural (taller) height rather than being shrunk to it.
        /// </summary>
        [Fact]
        public async Task AMinHeightShorterThanTheContentDoesNotShrinkTheBox()
        {
            var (natural, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt'><span id='box' style='display:inline-block;font-size:40pt'>x</span></div>"));
            var (floored, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt'><span id='box' style='display:inline-block;font-size:40pt;min-height:5pt'>x</span></div>"));

            var naturalHeight = PaintedRectOf(FindById(natural, "box")!).Height;
            var flooredHeight = PaintedRectOf(FindById(floored, "box")!).Height;

            Assert.Equal(naturalHeight, flooredHeight, 3);
        }

        /// <summary>
        /// ...and that rectangle survives into the fragment tree, which is what paint reads. Stated on a
        /// box with a border, since one with no decoration at all has zero content beyond its own
        /// height for a fragment to carry.
        /// </summary>
        [Fact]
        public async Task TheEmittedFragmentCarriesTheDeclaredHeight()
        {
            var (root, container) = await LayoutAsync(Wrap(
                "<div style='width:400pt'>" +
                "<span id='box' style='display:inline-block;height:40pt;border:1pt solid'></span></div>"));

            var fragment = FragmentPaintHarness.FirstFragmentOf(container, FindById(root, "box")!);
            var line = Assert.Single(fragment.Lines);

            Assert.Equal(20.0, line.Rect.Y, 3);
            Assert.Equal(42.0, line.Rect.Height, 3);
        }

        /// <summary>
        /// The box's own top stays exactly where the flow put it - the extra declared height is added
        /// below the content, not split around it or added above.
        /// </summary>
        [Fact]
        public async Task TheExtraDeclaredHeightIsAddedBelowTheContentNotAboveIt()
        {
            // Both boxes sit on the SAME line, holding identical content at the same font size, so their
            // natural (pre-correction) top edges genuinely coincide - unlike two boxes in separate,
            // vertically-stacked <div>s, where a taller first box would push the second div's own line
            // further down the page regardless of this fix.
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt'>" +
                "<span id='short' style='display:inline-block'>x</span>" +
                "<span id='tall' style='display:inline-block;height:40pt'>x</span></div>"));

            var shortRect = PaintedRectOf(FindById(root, "short")!);
            var tallRect = PaintedRectOf(FindById(root, "tall")!);

            Assert.Equal(shortRect.Y, tallRect.Y, 3);
            Assert.True(tallRect.Height > shortRect.Height,
                $"the declared height must actually grow the box, got {tallRect.Height} vs {shortRect.Height}");
        }

        /// <summary>
        /// Under <c>box-sizing: border-box</c> the declared height already covers the padding and
        /// border, so the painted box is the declared height itself rather than the declared height
        /// plus them.
        /// </summary>
        [Fact]
        public async Task BorderBoxPaintsTheDeclaredHeightItself()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt'>" +
                "<span id='box' style='display:inline-block;box-sizing:border-box;height:40pt;" +
                "padding:4pt;border:2pt solid'>x</span></div>"));

            Assert.Equal(40.0, PaintedRectOf(FindById(root, "box")!).Height, 3);
        }

        /// <summary>
        /// css-sizing-3 §6.2: a <c>border-box</c> height cannot shrink the content area past zero, so a
        /// declared height narrower than the box's own border and padding is floored at their sum.
        /// </summary>
        [Fact]
        public async Task BorderBoxIsFlooredAtItsOwnBorderAndPadding()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt'>" +
                "<span id='box' style='display:inline-block;box-sizing:border-box;height:1pt;" +
                "border:3pt solid;font-size:1pt;line-height:1pt'></span></div>"));

            Assert.Equal(6.0, PaintedRectOf(FindById(root, "box")!).Height, 3);
        }

        /// <summary>
        /// #1167: a percentage <c>height</c> now sizes the box's own painted border box, exactly like an
        /// absolute-length declared height, when the containing block's own height is definite (CSS 2.1
        /// §10.5) — resolved via <c>CssLayoutEngine.IsHeightDefinite</c>/<c>ResolveDefiniteHeightValue</c>,
        /// which are recursive and order-independent rather than dependent on the containing block's own
        /// layout epilogue having already run.
        /// </summary>
        [Fact]
        public async Task APercentageHeightSizesAgainstADefiniteContainingBlockHeight()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt;height:400pt'><span id='box' style='display:inline-block;height:50%'>x</span></div>"));

            Assert.Equal(200.0, PaintedRectOf(FindById(root, "box")!).Height, 3);
        }

        /// <summary>
        /// The genuinely spec-correct no-op case (CSS 2.1 §10.5): a percentage height still has no effect
        /// against a containing block whose own height is content-driven (indefinite), not just against
        /// one that happens not to have been laid out yet — this is the case #1167's fix keeps behaving
        /// exactly as before, not a residual limitation.
        /// </summary>
        [Fact]
        public async Task APercentageHeightAgainstAnIndefiniteContainingBlockIsIgnored()
        {
            var (natural, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt'><span id='box' style='display:inline-block'>x</span></div>"));
            var (percent, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt'><span id='box' style='display:inline-block;height:50%'>x</span></div>"));

            var naturalHeight = PaintedRectOf(FindById(natural, "box")!).Height;
            var percentHeight = PaintedRectOf(FindById(percent, "box")!).Height;

            Assert.Equal(naturalHeight, percentHeight, 3);
        }

        /// <summary>
        /// #1167's percentage resolution now also participates in Fix 1's line/flow reservation and Fix
        /// 2's anchor-aware growth, since <c>ResolveAtomicInlineDeclaredHeight</c> resolves the percentage
        /// case at the same flow-time hook as the absolute-length case, with no separate corrective pass.
        /// </summary>
        [Fact]
        public async Task APercentageHeightReservesLineSpaceLikeAnAbsoluteLengthDoes()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div id='container' style='width:80pt;height:400pt'>" +
                "<span id='tall' style='display:inline-block;height:50%'>x</span> " +
                "wwwwwwwwww wwwwwwwwww wwwwwwwwww wwwwwwwwww</div>"));

            var tallRect = PaintedRectOf(FindById(root, "tall")!);
            Assert.Equal(200.0, tallRect.Height, 3);

            var lastWord = Descendants(root).SelectMany(b => b.Words).Last();
            var lastLineTop = LineTopOf(root, lastWord);

            Assert.True(lastLineTop >= tallRect.Bottom - 0.01,
                $"a line after the percentage-tall box must clear it: line top {lastLineTop}, box bottom {tallRect.Bottom}");
        }

        /// <summary>
        /// The issue's headline: two boxes styled identically, one holding inline content and one empty,
        /// paint the same rectangle height.
        /// </summary>
        [Fact]
        public async Task AnEmptyInlineBlockPaintsTheSameHeightAsOneHoldingInlineContent()
        {
            // Two separate, vertically-stacked <div>s - their absolute Y naturally differs regardless of
            // this fix (the second div's line starts wherever the first div's box ends), so only the
            // shared Height is a fair comparison here, mirroring the width sibling test's own
            // Width/X-only (never Y) comparison for the same reason.
            var (root, _) = await LayoutAsync(Wrap(
                "<style>.box{display:inline-block;height:40pt;padding:3pt;border:1.5pt solid}</style>" +
                "<div style='width:400pt'><span id='filled' class='box'>x</span></div>" +
                "<div style='width:400pt'><span id='empty' class='box'></span></div>"));

            var filled = PaintedRectOf(FindById(root, "filled")!);
            var empty = PaintedRectOf(FindById(root, "empty")!);

            Assert.Equal(filled.Height, empty.Height, 3);
        }

        /// <summary>
        /// A declared height on a nested empty inline-block sizes that box's own rectangle regardless of
        /// nesting depth - each box's own declared height governs its own rectangle independently of its
        /// ancestors.
        /// </summary>
        [Fact]
        public async Task ANestedEmptyChildWithItsOwnDeclaredHeightPaintsItsOwnRectangle()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt'><span id='outer' style='display:inline-block'>" +
                "<span id='inner' style='display:inline-block;height:25pt'></span></span></div>"));

            Assert.Equal(25.0, PaintedRectOf(FindById(root, "inner")!).Height, 3);
        }

        /// <summary>
        /// #1166: the line/flow must reserve the box's full declared height, not just paint it — a
        /// wrapped continuation line after a tall inline-flowed inline-block, and the containing
        /// block's own height, must both clear the tall box rather than overlap it. This is the exact
        /// sibling-overlap regression an earlier, reverted in-development attempt at this fix
        /// reintroduced (see <c>.claude/recent-fixes/</c> for the #1101 entry) — CSS 2.1
        /// <see href="https://www.w3.org/TR/CSS21/visudet.html#line-height">§10.8</see>: an
        /// inline-block contributes its whole margin box to the line box it sits on.
        /// </summary>
        [Fact]
        public async Task AWrappedLineAfterATallDeclaredHeightBoxClearsIt()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div id='container' style='width:80pt'>" +
                "<span id='tall' style='display:inline-block;height:100pt'>x</span> " +
                "wwwwwwwwww wwwwwwwwww wwwwwwwwww wwwwwwwwww</div>"));

            var tallRect = PaintedRectOf(FindById(root, "tall")!);
            var container = FindById(root, "container")!;

            var lastWord = Descendants(root).SelectMany(b => b.Words).Last();
            var lastLineTop = LineTopOf(root, lastWord);

            Assert.True(lastLineTop >= tallRect.Bottom - 0.01,
                $"a line after the tall box must clear it: line top {lastLineTop}, box bottom {tallRect.Bottom}");
            Assert.True(container.ActualBottom >= tallRect.Bottom - 0.01,
                $"the container's own height must clear the tall box: {container.ActualBottom} vs {tallRect.Bottom}");
        }

        /// <summary>
        /// #1169: <c>vertical-align: top</c> anchors the box's own top regardless of declared height —
        /// unchanged from the default-baseline-with-content case, stated explicitly as its own regression
        /// guard rather than relying only on the default case's own test.
        /// </summary>
        [Fact]
        public async Task VerticalAlignTopGrowsDownwardKeepingTheTopEdgeFixed()
        {
            var (natural, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt'>" +
                "<span id='tall' style='font-size:40pt'>Tall</span>" +
                "<span id='box' style='display:inline-block;vertical-align:top'>x</span></div>"));
            var (grown, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt'>" +
                "<span id='tall' style='font-size:40pt'>Tall</span>" +
                "<span id='box' style='display:inline-block;vertical-align:top;height:80pt'>x</span></div>"));

            var naturalRect = PaintedRectOf(FindById(natural, "box")!);
            var grownRect = PaintedRectOf(FindById(grown, "box")!);

            Assert.Equal(naturalRect.Top, grownRect.Top, 3);
            Assert.Equal(80.0, grownRect.Height, 3);
        }

        /// <summary>
        /// #1169: <c>vertical-align: bottom</c> anchors the box's own bottom — CSS 2.1 §10.8.1 places
        /// the box's bottom at the line's bottom regardless of the box's own height, so growth must extend
        /// upward, keeping that bottom edge fixed, not downward past it.
        /// </summary>
        [Fact]
        public async Task VerticalAlignBottomGrowsUpwardKeepingTheBottomEdgeFixed()
        {
            var (natural, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt'>" +
                "<span id='tall' style='font-size:40pt'>Tall</span>" +
                "<span id='box' style='display:inline-block;vertical-align:bottom'>x</span></div>"));
            var (grown, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt'>" +
                "<span id='tall' style='font-size:40pt'>Tall</span>" +
                "<span id='box' style='display:inline-block;vertical-align:bottom;height:80pt'>x</span></div>"));

            var naturalRect = PaintedRectOf(FindById(natural, "box")!);
            var grownRect = PaintedRectOf(FindById(grown, "box")!);

            Assert.Equal(naturalRect.Bottom, grownRect.Bottom, 3);
            Assert.Equal(80.0, grownRect.Height, 3);
            Assert.True(grownRect.Top < naturalRect.Top,
                $"growth must extend upward, keeping the bottom fixed: natural top {naturalRect.Top}, grown top {grownRect.Top}");
        }

        /// <summary>
        /// #1169: <c>vertical-align: text-bottom</c> anchors the parent font's own bottom the same way
        /// <c>bottom</c> anchors the line's bottom.
        /// </summary>
        [Fact]
        public async Task VerticalAlignTextBottomGrowsUpwardKeepingTheBottomEdgeFixed()
        {
            var (natural, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt'>" +
                "<span id='tall' style='font-size:40pt'>Tall</span>" +
                "<span id='box' style='display:inline-block;vertical-align:text-bottom'>x</span></div>"));
            var (grown, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt'>" +
                "<span id='tall' style='font-size:40pt'>Tall</span>" +
                "<span id='box' style='display:inline-block;vertical-align:text-bottom;height:80pt'>x</span></div>"));

            var naturalRect = PaintedRectOf(FindById(natural, "box")!);
            var grownRect = PaintedRectOf(FindById(grown, "box")!);

            Assert.Equal(naturalRect.Bottom, grownRect.Bottom, 3);
            Assert.Equal(80.0, grownRect.Height, 3);
        }

        /// <summary>
        /// #1169: <c>vertical-align: middle</c> centers the box on the line using its natural height —
        /// growing symmetrically around the box's own already-centered midpoint keeps that midpoint on
        /// the line's own middle regardless of the natural height that produced it.
        /// </summary>
        [Fact]
        public async Task VerticalAlignMiddleGrowsSymmetricallyAroundItsCenter()
        {
            var (natural, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt'>" +
                "<span id='tall' style='font-size:40pt'>Tall</span>" +
                "<span id='box' style='display:inline-block;vertical-align:middle'>x</span></div>"));
            var (grown, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt'>" +
                "<span id='tall' style='font-size:40pt'>Tall</span>" +
                "<span id='box' style='display:inline-block;vertical-align:middle;height:80pt'>x</span></div>"));

            var naturalRect = PaintedRectOf(FindById(natural, "box")!);
            var grownRect = PaintedRectOf(FindById(grown, "box")!);

            var naturalCenter = naturalRect.Top + naturalRect.Height / 2;
            var grownCenter = grownRect.Top + grownRect.Height / 2;

            Assert.Equal(naturalCenter, grownCenter, 3);
            Assert.Equal(80.0, grownRect.Height, 3);
        }

        /// <summary>
        /// #1169: <c>vertical-align: sub</c>/<c>super</c> only ever reach a content-bearing box (an empty
        /// or <c>overflow</c>-hidden box's baseline offset does not depend on either), so they are grouped
        /// with default-baseline-with-content's top anchoring — stated as its own regression guard on the
        /// currently-shipped, tested behavior.
        /// </summary>
        [Fact]
        public async Task VerticalAlignSubGrowsDownwardKeepingTheTopEdgeFixed()
        {
            var (natural, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt'>" +
                "<span id='tall' style='font-size:40pt'>Tall</span>" +
                "<span id='box' style='display:inline-block;vertical-align:sub'>x</span></div>"));
            var (grown, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt'>" +
                "<span id='tall' style='font-size:40pt'>Tall</span>" +
                "<span id='box' style='display:inline-block;vertical-align:sub;height:80pt'>x</span></div>"));

            var naturalRect = PaintedRectOf(FindById(natural, "box")!);
            var grownRect = PaintedRectOf(FindById(grown, "box")!);

            Assert.Equal(naturalRect.Top, grownRect.Top, 3);
            Assert.Equal(80.0, grownRect.Height, 3);
        }

        /// <summary>
        /// #1169: an explicit <c>vertical-align</c> length/percentage offset takes the same
        /// <c>AnchorOf</c> branch as <c>sub</c>/<c>super</c> — grouped with default-baseline-with-content's
        /// top anchoring for the same reason (only ever reaches a content-bearing box).
        /// </summary>
        [Fact]
        public async Task VerticalAlignLengthOffsetGrowsDownwardKeepingTheTopEdgeFixed()
        {
            var (natural, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt'>" +
                "<span id='tall' style='font-size:40pt'>Tall</span>" +
                "<span id='box' style='display:inline-block;vertical-align:10pt'>x</span></div>"));
            var (grown, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt'>" +
                "<span id='tall' style='font-size:40pt'>Tall</span>" +
                "<span id='box' style='display:inline-block;vertical-align:10pt;height:80pt'>x</span></div>"));

            var naturalRect = PaintedRectOf(FindById(natural, "box")!);
            var grownRect = PaintedRectOf(FindById(grown, "box")!);

            Assert.Equal(naturalRect.Top, grownRect.Top, 3);
            Assert.Equal(80.0, grownRect.Height, 3);
        }

        /// <summary>
        /// #1169's still-open, genuinely circular case: an empty box's default-<c>baseline</c> "baseline"
        /// is itself computed from its still-natural rectangle inside <c>ApplyVerticalAlignment</c>, before
        /// growth runs, so growth stays exactly as it was before #1169 (downward, from the flow-assigned
        /// top). An earlier, since-reverted attempt at bottom-anchoring this case produced a measured
        /// regression; this pins the empty case's behavior alongside
        /// <see cref="TheEmittedFragmentCarriesTheDeclaredHeight"/>.
        /// </summary>
        [Fact]
        public async Task AnEmptyBoxUnderDefaultVerticalAlignStillGrowsDownwardFromItsFlowPosition()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt'>" +
                "<span id='box' style='display:inline-block;height:40pt;border:1pt solid'></span></div>"));

            var rect = PaintedRectOf(FindById(root, "box")!);

            Assert.Equal(20.0, rect.Y, 3);
            Assert.Equal(42.0, rect.Height, 3);
        }

        /// <summary>
        /// The circular case's other trigger: content whose <c>overflow</c> isn't <c>visible</c> also has
        /// no baseline of its own (<see cref="AtomicInlineBaselineOf"/>'s bottom-margin-edge fallback), so
        /// it must grow downward, unchanged, exactly like the empty case above — even though it holds
        /// content, unlike the empty case.
        /// </summary>
        [Fact]
        public async Task OverflowHiddenContentStillGrowsDownwardUnderDefaultVerticalAlign()
        {
            var (natural, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt'><span id='box' style='display:inline-block;overflow:hidden'>x</span></div>"));
            var (grown, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt'><span id='box' style='display:inline-block;overflow:hidden;height:80pt'>x</span></div>"));

            var naturalRect = PaintedRectOf(FindById(natural, "box")!);
            var grownRect = PaintedRectOf(FindById(grown, "box")!);

            Assert.Equal(naturalRect.Top, grownRect.Top, 3);
            Assert.Equal(80.0, grownRect.Height, 3);
        }

        /// <summary>
        /// The border-box rectangle this box's decorations are drawn over - its one per-line rectangle,
        /// which is where an inline box's real geometry lives. Asserting a single one also states that
        /// the box did not wrap, which every fixture here depends on.
        /// </summary>
        private static RRect PaintedRectOf(CssBox box) => Assert.Single(box.Rectangles).Value;
    }
}
