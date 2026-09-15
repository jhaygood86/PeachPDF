using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// CSS 2.1 <see href="https://www.w3.org/TR/CSS21/visudet.html#line-height">§10.8/§10.8.1</see>:
    /// every inline box on a line hangs its own content area from one shared baseline, with the
    /// <b>leading</b> — <c>line-height</c> less the font's own height — split in half above and below it.
    /// </summary>
    /// <remarks>
    /// Every expectation here was taken from headless Chrome on the same markup before it was written.
    /// Where a value is <i>specified</i> (a declared <c>line-height</c>) it is asserted literally; where it
    /// depends on the font actually resolved — which is not Chrome's on this machine — the relationship is
    /// asserted instead, with each test first asserting the metric precondition that makes it non-vacuous,
    /// so none can pass by a fallback font happening to be the right shape.
    /// </remarks>
    public class BaselineAlignmentLayoutIntegrationTests
    {
        [Fact]
        public async Task MixedFontSizesOnOneLine_ShareOneBaseline()
        {
            // Chrome: the zero-height probes after the 30pt "Y" and after the 10pt "x" report the same
            // bottom (36px), i.e. one baseline, while the two content areas start 36px apart.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='d' style='font:10pt Arial'>x<span id='big' style='font-size:30pt'>Y</span></div>"));

            var block = LayoutHarness.FindById(root, "d")!;
            var line = Assert.Single(block.LineBoxes);

            var small = WordOf(line, "x");
            var big = WordOf(line, "Y");

            var smallAscent = small.OwnerBox.ActualFont.Ascent;
            var bigAscent = big.OwnerBox.ActualFont.Ascent;

            // Non-vacuity: the two really are different sizes, so sharing a baseline cannot be the same
            // thing as sharing a top.
            Assert.True(bigAscent > smallAscent + 1,
                $"fixture must mix font sizes: small ascent={smallAscent}, big ascent={bigAscent}");

            Assert.Equal(big.Top + bigAscent, small.Top + smallAscent, 3);
            Assert.True(small.Top > big.Top + 1,
                $"the smaller box must hang lower than the larger one's top (small={small.Top}, big={big.Top})");
        }

        [Fact]
        public async Task ALineHeightTallerThanTheFont_CentresTheContentAreaInIt()
        {
            // Chrome: a 10pt Arial line at line-height:30pt is exactly 40px (= 30pt) tall and puts its
            // baseline 24px below the line's top - the font's ascent plus half the leading, not the
            // ascent alone.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='d' style='font:10pt Arial;line-height:30pt'>x</div>"));

            var block = LayoutHarness.FindById(root, "d")!;
            var line = Assert.Single(block.LineBoxes);
            var word = WordOf(line, "x");
            var font = word.OwnerBox.ActualFont;

            Assert.True(font.Height < 30, $"fixture needs positive leading; font height={font.Height}");

            var halfLeading = (30 - font.Height) / 2;

            Assert.Equal(30, line.BaselineExtent!.Value.Height, 3);
            Assert.Equal(line.LineTop + halfLeading, word.Top, 3);
            Assert.Equal(line.LineTop + halfLeading + font.Ascent, line.BaselineY!.Value, 3);
        }

        [Fact]
        public async Task ALineHeightShorterThanTheFont_KeepsItsInkInsideItsLineBox()
        {
            // The one place this deliberately parts company with Chrome, which lets the glyphs overflow
            // the line box on BOTH sides when the leading is negative (its 10pt/5pt line is 7px tall with
            // its content area starting 5px ABOVE the line). This engine decides which fragmentainer a
            // word belongs to from the word's own rectangle, so ink that leaves its line box leaves the
            // page the line was placed on - see the accepted-gap note. The line's own height is still the
            // declared line-height either way, which is what governs where the next line goes.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='d' style='font:10pt Arial;line-height:5pt'>x</div>"));

            var block = LayoutHarness.FindById(root, "d")!;
            var line = Assert.Single(block.LineBoxes);
            var word = WordOf(line, "x");

            Assert.True(word.OwnerBox.ActualFont.Height > 5,
                $"fixture needs negative leading; font height={word.OwnerBox.ActualFont.Height}");

            Assert.Equal(5, line.BaselineExtent!.Value.Height, 3);
            Assert.Equal(line.LineTop, word.Top, 3);
        }

        [Fact]
        public async Task ALinesTwoSidesAreMaximisedIndependently_SoItCanExceedEveryLineHeightOnIt()
        {
            // The reason a line box is a pair of extents rather than a height: the box reaching highest
            // above the baseline need not be the one reaching lowest below it. Chrome makes this line
            // 56.328px tall - more than either the 40px (30pt) line-height of the block or the 53.328px
            // (40pt) one of the span - which a max-of-line-heights model cannot produce.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='d' style='font:30pt Arial;line-height:30pt'>A" +
                "<span id='s' style='font-size:10pt;line-height:40pt'>b</span></div>"));

            var block = LayoutHarness.FindById(root, "d")!;
            var line = Assert.Single(block.LineBoxes);

            Assert.True(line.BaselineExtent!.Value.Height > 40.5,
                $"the line must be taller than the largest line-height on it (40pt), was {line.BaselineExtent.Value.Height}");

            // The tall side comes from the 30pt block text and the deep side from the 40pt line-height on
            // the small span - neither box supplies both.
            var big = WordOf(line, "A");
            var small = WordOf(line, "b");

            Assert.Equal(big.Top + big.OwnerBox.ActualFont.Ascent, small.Top + small.OwnerBox.ActualFont.Ascent, 3);
        }

        [Fact]
        public async Task LineHeightNormal_LeavesItsInkWhereTheFlowPutIt()
        {
            // The guard that the half-leading is not a gratuitous shift: `line-height: normal` resolves
            // from the font's own ascent + descent + gap, so the leading is ~0 and an ordinary paragraph
            // must not move at all. This is what keeps the change confined to lines that actually mix
            // sizes or declare a line-height.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='d' style='font:10pt Arial'>x</div>"));

            var block = LayoutHarness.FindById(root, "d")!;
            var line = Assert.Single(block.LineBoxes);
            var word = WordOf(line, "x");

            Assert.Equal(line.LineTop, word.Top, 1);
        }

        [Fact]
        public async Task AReplacedElementsBottomMarginEdge_SharesTheTextBaseline()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='d' style='font:10pt Arial;line-height:40pt'>x" +
                $"<img id='a' src='{RasterPngFixture.OnePixelDataUri}' " +
                "style='width:10pt;height:10pt;margin:2pt 0 3pt'></div>"));

            var block = LayoutHarness.FindById(root, "d")!;
            var line = Assert.Single(block.LineBoxes);
            var text = WordOf(line, "x");
            var atomic = LayoutHarness.FindById(root, "a")!;
            var image = Assert.Single(line.Words, w => w.IsImage);

            var textBaseline = text.Top + text.OwnerBox.ActualFont.Ascent;

            Assert.Equal(textBaseline, line.BaselineY!.Value, 3);
            Assert.Equal(textBaseline, image.Bottom + atomic.ActualMarginBottom, 3);
            Assert.True(image.Top > line.LineTop + 1,
                $"positive leading must move the image below the line top ({line.LineTop}), was {image.Top}");
        }

        [Theory]
        [InlineData("<svg id='a' style='width:10pt;height:10pt'></svg>")]
        [InlineData("<math id='a'><mi>x</mi></math>")]
        [InlineData("<input id='a' type='checkbox' style='width:10pt;height:10pt;margin:0'>")]
        public async Task OtherAtomicReplacedElements_ShareTheTextBaseline(string markup)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='d' style='font:10pt Arial;line-height:40pt'>x{markup}</div>"));

            var block = LayoutHarness.FindById(root, "d")!;
            var line = Assert.Single(block.LineBoxes);
            var text = WordOf(line, "x");
            var atomic = LayoutHarness.FindById(root, "a")!;
            var atomicWord = Assert.Single(line.Words, w => ReferenceEquals(w.OwnerBox, atomic));

            Assert.True(atomicWord.IsImage, $"{atomic.GetType().Name} must use the atomic replaced-word path");
            Assert.Equal(text.Top + text.OwnerBox.ActualFont.Ascent,
                atomicWord.Bottom + atomic.ActualMarginBottom, 3);
        }

        [Fact]
        public async Task AnInlineWrapperMovesWithItsBaselineAlignedImage()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='d' style='font:10pt Arial;line-height:40pt'>x" +
                $"<span id='s' style='padding:2pt'><img id='a' src='{RasterPngFixture.OnePixelDataUri}' " +
                "style='width:10pt;height:10pt'></span></div>"));

            var block = LayoutHarness.FindById(root, "d")!;
            var line = Assert.Single(block.LineBoxes);
            var wrapper = LayoutHarness.FindById(root, "s")!;
            var atomic = LayoutHarness.FindById(root, "a")!;
            var wrapperRect = line.Rectangles[wrapper];
            var atomicRect = line.Rectangles[atomic];

            Assert.True(wrapperRect.Top <= atomicRect.Top);
            Assert.True(wrapperRect.Bottom >= atomicRect.Bottom);
        }

        [Fact]
        public async Task ATallReplacedElement_ReservesTheDescentBelowItsBaseline()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='d' style='font:10pt Arial'>x" +
                $"<img id='a' src='{RasterPngFixture.OnePixelDataUri}' " +
                "style='width:10pt;height:30pt;margin-bottom:3pt'></div>"));

            var block = LayoutHarness.FindById(root, "d")!;
            var line = Assert.Single(block.LineBoxes);
            var text = WordOf(line, "x");
            var atomic = LayoutHarness.FindById(root, "a")!;
            var image = Assert.Single(line.Words, w => w.IsImage);

            Assert.Equal(image.Bottom + atomic.ActualMarginBottom,
                text.Top + text.OwnerBox.ActualFont.Ascent, 3);
            var alignedLineBottom = line.BaselineY!.Value + line.BaselineExtent!.Value.BelowBaseline;
            Assert.True(block.ActualBottom >= alignedLineBottom,
                $"block bottom {block.ActualBottom} must reserve the aligned line through {alignedLineBottom}");
        }

        [Fact]
        public async Task AnOutsideMarkerInALargerFont_SitsOnTheItemsOwnBaseline()
        {
            // Chrome puts the marker's baseline on the item's first line's baseline, so a ::marker
            // font-size override makes the marker bigger without leaving its own digits hanging below the
            // text they number.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>#l li::marker { font-size: 20pt }</style>" +
                "<ol id='l' style='margin:0;padding-left:40pt;font:8.5pt Arial'><li id='i'>Item</li></ol>"));

            var item = LayoutHarness.FindById(root, "i")!;
            var line = Assert.Single(item.LineBoxes);
            var marker = item.Boxes.Single(b => b.IsMarkerPseudoElement);
            var markerWord = Assert.Single(marker.Words);

            Assert.True(marker.ActualFont.Ascent > item.ActualFont.Ascent + 1,
                $"fixture needs a larger marker font: marker={marker.ActualFont.Ascent}, item={item.ActualFont.Ascent}");

            Assert.Equal(line.BaselineY!.Value, markerWord.Top + marker.ActualFont.Ascent, 3);
        }

        [Fact]
        public async Task AnOutsideMarkerInALargerFont_GrowsTheItemsFirstLine()
        {
            // Chrome: the same <li> is 12px tall with no ::marker override and 26px with one at 20pt. The
            // marker is not in the item's inline flow, but it does sit on that line's baseline, so the
            // line has to be tall enough to hold it - otherwise it reaches up out of the line and collides
            // with whatever precedes the item. css-lists-3 §3.5 leaves this expressly undefined; this
            // follows browsers.
            const string List = "<ol id='l' style='margin:0;padding-left:40pt;font:8.5pt Arial'><li id='i'>Item</li></ol>";

            var (withMarker, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>#l li::marker { font-size: 20pt }</style>" + List));
            var (plain, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(List));

            var grown = LineHeightOfFirstLine(LayoutHarness.FindById(withMarker, "i")!);
            var ordinary = LineHeightOfFirstLine(LayoutHarness.FindById(plain, "i")!);

            Assert.True(grown > ordinary + 5,
                $"the larger marker must grow the item's first line: with={grown}, without={ordinary}");
        }

        [Fact]
        public async Task UnderNegativeLeading_TheLinesRecordedBaselineIsTheOneItsBoxesSitOn()
        {
            // The floor that keeps a short line-height's ink inside its line box moves every box on the
            // line down together. CssLineBox.BaselineY has to move with them: CssBoxMarker reads it to sit
            // an outside marker on the item's first baseline, so a stale value puts the marker half a
            // negative leading above the text it numbers.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<ol id='l' style='margin:0;padding-left:40pt;font:20pt Arial;line-height:10pt'>" +
                "<li id='i'>Item</li></ol>"));

            var item = LayoutHarness.FindById(root, "i")!;
            var line = item.LineBoxes[0];
            var text = Assert.Single(line.Words, w => w.Text == "Item");
            var marker = item.Boxes.Single(b => b.IsMarkerPseudoElement);

            Assert.True(text.OwnerBox.ActualFont.Height > 10,
                $"fixture needs negative leading; font height={text.OwnerBox.ActualFont.Height}");

            Assert.Equal(text.Top + text.OwnerBox.ActualFont.Ascent, line.BaselineY!.Value, 3);
            Assert.Equal(line.BaselineY.Value, Assert.Single(marker.Words).Top + marker.ActualFont.Ascent, 3);
        }

        [Fact]
        public async Task VerticalAlignTop_AlignsToTheLineBox_NotToTheTopmostInk()
        {
            // CSS 2.1 §10.8.1 aligns `vertical-align: top` with the top of the LINE BOX. Its content sits
            // half a leading below that, so reading the line's extent off its boxes' rectangles - which is
            // all there was to read while words were placed flush with the line's top - now answers a
            // different question.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='d' style='font:10pt Arial;line-height:40pt'>x" +
                "<span id='t' style='vertical-align:top;font-size:8pt'>up</span></div>"));

            var block = LayoutHarness.FindById(root, "d")!;
            var line = Assert.Single(block.LineBoxes);
            var aligned = WordOf(line, "up");
            var ordinary = WordOf(line, "x");

            Assert.True(ordinary.Top > line.LineTop + 1,
                $"fixture needs positive leading, so the line's ink starts below its top ({line.LineTop}) - got {ordinary.Top}");

            Assert.Equal(line.LineTop, aligned.Top, 3);
        }

        private static double LineHeightOfFirstLine(CssBox item) =>
            item.LineBoxes[0].BaselineExtent!.Value.Height;

        private static CssRect WordOf(CssLineBox line, string text) =>
            Assert.Single(line.Words, w => w.Text == text);
    }
}
