using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
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
        /// A percentage <c>height</c> is excluded from this fix, exactly as a percentage <c>width</c>
        /// originally was (#1091, before #1097 added it): such a box is sized entirely by its content,
        /// as though <c>height: auto</c> were declared. See
        /// <c>.claude/accepted-gaps/percentage-height-on-an-inline-content-inline-block-is-ignored.md</c>.
        /// </summary>
        [Fact]
        public async Task APercentageHeightIsIgnored()
        {
            var (natural, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt'><span id='box' style='display:inline-block'>x</span></div>"));
            var (percent, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt;height:400pt'><span id='box' style='display:inline-block;height:50%'>x</span></div>"));

            var naturalHeight = PaintedRectOf(FindById(natural, "box")!).Height;
            var percentHeight = PaintedRectOf(FindById(percent, "box")!).Height;

            Assert.Equal(naturalHeight, percentHeight, 3);
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
        /// The border-box rectangle this box's decorations are drawn over - its one per-line rectangle,
        /// which is where an inline box's real geometry lives. Asserting a single one also states that
        /// the box did not wrap, which every fixture here depends on.
        /// </summary>
        private static RRect PaintedRectOf(CssBox box) => Assert.Single(box.Rectangles).Value;
    }
}
