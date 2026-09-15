using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using static PeachPDF.Tests.TestSupport.LayoutHarness;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// The painted half of CSS 2.1 §10.3.9: a declared <c>width</c> sizes a non-replaced inline-block's
    /// own box, not only the room it reserves on the line. Its sibling
    /// <see cref="InlineBlockDeclaredWidthTests"/> covers the room; this covers the box, which used to
    /// stay at whatever its content happened to measure — nothing at all, for an empty one — so
    /// backgrounds, borders and overflow clips were painted at a width the following text had already
    /// been advanced past.
    /// <para>
    /// Asserted on the box's per-line rectangle, which for an inline box <i>is</i> its painted geometry —
    /// its own <c>Location</c>/<c>Size</c> never name a position on the page — and which
    /// <c>FragmentEmitter</c> turns directly into the <c>LineFragment</c> rectangles paint draws
    /// decorations over. <see cref="TheEmittedFragmentCarriesTheDeclaredWidth"/> pins that last step.
    /// Page margins are 20pt, so the first inline box on the first line starts at x=20.
    /// </para>
    /// </summary>
    public class InlineBlockDeclaredWidthGeometryTests
    {
        /// <summary>
        /// The issue's own reproduction, in points: inline-blocks that differ only in what (if anything)
        /// is inside them all paint the one width they declare.
        /// </summary>
        [Theory]
        // Inline content that comes nowhere near filling the declared width.
        [InlineData("<span id='box' style='display:inline-block;width:60pt'>x</span>", 60.0)]
        // Nothing inside at all — no words to measure, so this one used to paint no rectangle.
        [InlineData("<span id='box' style='display:inline-block;width:60pt'></span>", 60.0)]
        // ...and with a border, which used to be the whole of its painted width.
        [InlineData("<span id='box' style='display:inline-block;width:60pt;border:1pt solid'></span>", 62.0)]
        // Padding and border on a box that does hold content: 60 + 2×4 padding + 2×2 border.
        [InlineData("<span id='box' style='display:inline-block;width:60pt;padding:4pt;border:2pt solid'>x</span>", 72.0)]
        public async Task ADeclaredWidthSizesThePaintedBorderBox(string markup, double expectedWidth)
        {
            var (root, _) = await LayoutAsync(Wrap($"<div style='width:400pt'>{markup}</div>"));
            var box = FindById(root, "box")!;

            Assert.Equal(expectedWidth, PaintedRectOf(box).Width, 3);
            Assert.Equal(expectedWidth, box.ActualBoxSizingWidth, 3);
        }

        /// <summary>
        /// ...and that rectangle survives into the fragment tree, which is what paint reads. Stated on a
        /// box with a border, since one with no decoration at all has zero height and so contributes no
        /// printable content for a fragment to carry.
        /// </summary>
        [Fact]
        public async Task TheEmittedFragmentCarriesTheDeclaredWidth()
        {
            var (root, container) = await LayoutAsync(Wrap(
                "<div style='width:400pt'>" +
                "<span id='box' style='display:inline-block;width:60pt;border:1pt solid'></span></div>"));

            var fragment = FragmentPaintHarness.FirstFragmentOf(container, FindById(root, "box")!);
            var line = Assert.Single(fragment.Lines);

            Assert.Equal(20.0, line.Rect.X, 3);
            Assert.Equal(62.0, line.Rect.Width, 3);
        }

        /// <summary>
        /// The border box starts at the box's own border edge, which is one leading border+padding left
        /// of where its content starts.
        /// </summary>
        [Fact]
        public async Task ThePaintedBoxStartsAtTheBoxesOwnBorderEdge()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt'>" +
                "<span id='box' style='display:inline-block;width:60pt;padding:4pt;border:2pt solid'>x</span>" +
                "</div>"));

            var rect = PaintedRectOf(FindById(root, "box")!);

            Assert.Equal(20.0, rect.X, 3);
            Assert.Equal(92.0, rect.Right, 3);
        }

        /// <summary>
        /// The issue's headline: two boxes styled identically, one holding inline content and one empty,
        /// paint the same rectangle. They differed by the width of a glyph.
        /// </summary>
        [Fact]
        public async Task AnEmptyInlineBlockPaintsTheSameBoxAsOneHoldingInlineContent()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<style>.box{display:inline-block;width:60pt;padding:3pt;border:1.5pt solid}</style>" +
                "<div style='width:400pt'><span id='filled' class='box'>x</span></div>" +
                "<div style='width:400pt'><span id='empty' class='box'></span></div>"));

            var filled = PaintedRectOf(FindById(root, "filled")!);
            var empty = PaintedRectOf(FindById(root, "empty")!);

            Assert.Equal(filled.Width, empty.Width, 3);
            Assert.Equal(filled.X, empty.X, 3);
        }

        /// <summary>
        /// Under <c>box-sizing: border-box</c> the declared width already covers the padding and border,
        /// so the painted box is the declared width itself rather than the declared width plus them.
        /// </summary>
        [Fact]
        public async Task BorderBoxPaintsTheDeclaredWidthItself()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt'>" +
                "<span id='box' style='display:inline-block;box-sizing:border-box;width:60pt;" +
                "padding:4pt;border:2pt solid'>x</span></div>"));

            var box = FindById(root, "box")!;

            Assert.Equal(60.0, PaintedRectOf(box).Width, 3);
            Assert.Equal(60.0, box.ActualBoxSizingWidth, 3);
        }

        /// <summary>
        /// css-sizing-3 §3.2: a <c>border-box</c> width cannot shrink the content area past zero, so a
        /// declared width narrower than the box's own border and padding is floored at their sum rather
        /// than reserving less room on the line than the border itself occupies.
        /// </summary>
        [Fact]
        public async Task BorderBoxIsFlooredAtItsOwnBorderAndPadding()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt'>" +
                "<span id='box' style='display:inline-block;box-sizing:border-box;width:2pt;" +
                "border:3pt solid'></span><span id='after'>AFTER</span></div>"));

            var rect = PaintedRectOf(FindById(root, "box")!);

            Assert.Equal(6.0, rect.Width, 3);
            Assert.Equal(6.0, FindById(root, "box")!.ActualBoxSizingWidth, 3);
            Assert.Equal(26.0, FirstWordLeftOf(FindById(root, "after")!), 3);
        }

        /// <summary>
        /// A box that holds no word of its own can still hold something that took room on the line — an
        /// empty padded inline-block nested inside another — and its own painted box has to cover it.
        /// </summary>
        [Fact]
        public async Task AnEmptyInlineBlockCoversWhatItsOwnEmptyChildReserved()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt'><span id='outer' style='display:inline-block'>" +
                "<span style='display:inline-block;padding:5pt'></span></span></div>"));

            Assert.Equal(10.0, PaintedRectOf(FindById(root, "outer")!).Width, 3);
        }

        /// <summary>
        /// The declared width is the box's width, and breakable content wider than it <b>wraps inside
        /// it</b> rather than widening it — the box establishes its own formatting context and its lines
        /// are broken at its own measure (css-display-3 §2.3). Verified against Chrome on the same markup:
        /// a 15pt box holding "Wider than the box" comes out 15pt wide with one word per line.
        /// </summary>
        [Fact]
        public async Task ContentWiderThanTheDeclaredWidthWrapsInsideItRatherThanWideningIt()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt'>" +
                "<span id='declared' style='display:inline-block;width:20pt'>Wider than the box</span></div>"));

            var declared = FindById(root, "declared")!;

            Assert.Equal(20.0, PaintedRectOf(declared).Width, 3);
            Assert.True(declared.LineBoxes.Count > 1,
                $"the box's own content must wrap onto lines of its own, got {declared.LineBoxes.Count}");
        }

        /// <summary>
        /// An unbreakable word has nowhere to wrap, so it does overflow — and the box is still its
        /// declared width, exactly as Chrome renders the same markup. This is the case
        /// <c>overflow: visible</c> actually describes.
        /// </summary>
        [Fact]
        public async Task AnUnbreakableWordOverflowsTheDeclaredWidthWithoutWideningTheBox()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt'>" +
                "<span id='declared' style='display:inline-block;width:20pt'>Unbreakableverylongword</span></div>"));

            var declared = FindById(root, "declared")!;
            var rect = PaintedRectOf(declared);

            Assert.Equal(20.0, rect.Width, 3);
            Assert.True(FirstWordLeftOf(declared) + WordWidthOf(declared) > rect.Right,
                "the unbreakable word must overflow the box it cannot fit in");
        }

        /// <summary>
        /// CSS 2.1 §10.2: the percentage is resolved against the containing block's content width and
        /// sizes the inline-block's painted border box just like an absolute declared width.
        /// </summary>
        [Fact]
        public async Task APercentageWidthSizesThePaintedBoxFromItsContainingBlock()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt'><span id='percent' style='display:inline-block;width:50%'>x</span></div>"));

            var percent = PaintedRectOf(FindById(root, "percent")!);

            Assert.Equal(200.0, percent.Width, 3);
        }

        /// <summary>
        /// The painted box moves with its line. An empty inline-block's rectangle is stated by the flow
        /// rather than derived from its words, so <c>text-align</c> has to shift it along with everything
        /// else on the line instead of leaving it where the flow first put it.
        /// </summary>
        [Fact]
        public async Task AnEmptyInlineBlocksPaintedBoxFollowsTextAlign()
        {
            const string Content = "<span id='box' style='display:inline-block;width:60pt;border:1pt solid'></span>" +
                                   "<span id='after'>AFTER</span>";

            var (leftRoot, _) = await LayoutAsync(Wrap($"<div style='width:400pt'>{Content}</div>"));
            var (centerRoot, _) = await LayoutAsync(Wrap(
                $"<div style='width:400pt;text-align:center'>{Content}</div>"));

            // However far centering moved the line's text, the box in front of it moved with it.
            var textShift = FirstWordLeftOf(FindById(centerRoot, "after")!)
                            - FirstWordLeftOf(FindById(leftRoot, "after")!);

            Assert.True(textShift > 1, $"the fixture must actually be centered, shift was {textShift}");

            var left = PaintedRectOf(FindById(leftRoot, "box")!);
            var centered = PaintedRectOf(FindById(centerRoot, "box")!);

            Assert.Equal(20.0, left.X, 3);
            Assert.Equal(left.X + textShift, centered.X, 3);
            Assert.Equal(62.0, centered.Width, 3);
        }

        /// <summary>
        /// The same box-model arithmetic with no declared width at all: an empty <c>auto</c>-width
        /// inline-block is sized entirely by its own padding and border. The advance is
        /// <see cref="InlineLeftSpacingAdvanceTests"/>' subject and is asserted here only to hold the two
        /// halves together; what was missing is the box itself, which such a box painted nowhere at all —
        /// it has no words for its rectangle to be derived from.
        /// </summary>
        [Fact]
        public async Task AnEmptyAutoWidthInlineBlockPaintsAndReservesOnlyItsOwnPaddingAndBorder()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='width:400pt'>" +
                "<span id='box' style='display:inline-block;padding:4pt;border:2pt solid'></span>" +
                "<span id='after'>AFTER</span></div>"));

            var rect = PaintedRectOf(FindById(root, "box")!);

            Assert.Equal(20.0, rect.X, 3);
            Assert.Equal(12.0, rect.Width, 3);
            Assert.Equal(32.0, FirstWordLeftOf(FindById(root, "after")!), 3);
        }

        /// <summary>
        /// The border-box rectangle this box's decorations are drawn over — its one per-line rectangle,
        /// which is where an inline box's real geometry lives. Asserting a single one also states that the
        /// box did not wrap, which every fixture here depends on.
        /// </summary>
        private static RRect PaintedRectOf(CssBox box) => Assert.Single(box.Rectangles).Value;

        private static double WordWidthOf(CssBox box)
        {
            foreach (var descendant in Descendants(box))
            {
                foreach (var word in descendant.Words)
                {
                    return word.Width;
                }
            }

            Assert.Fail($"box '{box}' placed no words");
            return 0;
        }

        private static double FirstWordLeftOf(CssBox box)
        {
            foreach (var descendant in Descendants(box))
            {
                foreach (var word in descendant.Words)
                {
                    return word.Left;
                }
            }

            Assert.Fail($"box '{box}' placed no words");
            return 0;
        }
    }
}
