using PeachPDF.Html.Core.Dom;
using System.Linq;
using System.Threading.Tasks;
using static PeachPDF.Tests.TestSupport.LayoutHarness;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A block-level box whose content is inline (a block, a grid item, a flex item, a table cell) is
    /// flowed as <c>FlowBox(blockBox, blockBox)</c>, so the per-box exit bookkeeping runs for the flow
    /// ROOT as well as for any nested atomic inline. That bookkeeping compares the line's advance with
    /// the box's declared border-box height; for the root, the advance starts at the CONTENT edge, so the
    /// comparison counted the box's vertical padding and border a second time - a padded one-line block
    /// came out <c>P + max(content, declared)</c> tall instead of <c>max(content, declared) + P</c>
    /// once the declared height (even <c>min-height</c>'s initial <c>0</c>, which is a valid length)
    /// reached the branch. An explicit <c>height</c> hid it, because the block's height is applied after
    /// the flow.
    /// <para>
    /// Every expectation here is relative to an UNPADDED twin laid out in the same document (the
    /// one-line content height <c>c</c>), so nothing depends on a font's metrics.
    /// </para>
    /// </summary>
    public class BlockLevelPaddingSingleLineHeightTests
    {
        private static double HeightOf(CssBox box) => box.ActualBottom - box.Location.Y;

        // ---- Plain blocks -------------------------------------------------------------------

        [Fact]
        public async Task APaddedBlockIsItsContentPlusPaddingTall()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div id='twin' style='font-size:10pt'>x</div>" +
                "<div id='pad' style='padding:10pt;font-size:10pt'>x</div>"));

            var c = HeightOf(FindById(root, "twin")!);

            Assert.Equal(c + 20, HeightOf(FindById(root, "pad")!), 2);
        }

        [Fact]
        public async Task AnAsymmetricallyPaddedAndBorderedBlockCountsEachInsetOnce()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div id='twin' style='font-size:10pt'>x</div>" +
                "<div id='pad' style='padding:4pt 6pt 12pt;border:2pt solid;font-size:10pt'>x</div>"));

            var c = HeightOf(FindById(root, "twin")!);

            // 4 + 12 padding, 2 + 2 border.
            Assert.Equal(c + 20, HeightOf(FindById(root, "pad")!), 2);
        }

        [Fact]
        public async Task ABorderBoxPaddedBlockIsItsContentPlusPaddingTall()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div id='twin' style='font-size:10pt'>x</div>" +
                "<div id='pad' style='box-sizing:border-box;padding:10pt;border:1pt solid;font-size:10pt'>x</div>"));

            var c = HeightOf(FindById(root, "twin")!);

            // border-box only matters for a declared size; an auto-height box still grows to hold its
            // content plus 20 padding plus 2 border.
            Assert.Equal(c + 22, HeightOf(FindById(root, "pad")!), 2);
        }

        [Fact]
        public async Task AContentBoxMinHeightIsAddedToThePaddingNotCountedInsideIt()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div id='pad' style='min-height:40pt;padding:10pt;font-size:10pt'>x</div>"));

            // css-sizing-3: min-height names the content box, so the border box is 40 + 20.
            Assert.Equal(60, HeightOf(FindById(root, "pad")!), 2);
        }

        [Fact]
        public async Task AMinHeightShorterThanATwoLineContentDoesNotShrinkIt()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div id='twin' style='width:40pt;font-size:10pt'>xx xx</div>" +
                "<div id='pad' style='width:40pt;min-height:10pt;padding:10pt;font-size:10pt'>xx xx</div>"));

            var c2 = HeightOf(FindById(root, "twin")!);

            Assert.Equal(System.Math.Max(c2, 10) + 20, HeightOf(FindById(root, "pad")!), 2);
        }

        [Fact]
        public async Task ABorderBoxMinHeightFloorsTheWholeBorderBox()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div id='pad' style='box-sizing:border-box;min-height:40pt;padding:10pt;font-size:10pt'>x</div>"));

            Assert.Equal(40, HeightOf(FindById(root, "pad")!), 2);
        }

        // ---- Grid ---------------------------------------------------------------------------

        [Fact]
        public async Task PaddedGridItemsAreTheirContentPlusPaddingTall()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div id='twin' style='font-size:10pt'>x</div>" +
                "<div id='grid' style='display:grid;grid-template-columns:120pt 1fr 1fr'>" +
                "<div id='a' class='cell' style='padding:10pt;font-size:10pt'>x</div>" +
                "<div id='b' class='cell' style='padding:10pt;font-size:10pt'>y</div>" +
                "<div id='c' class='cell' style='padding:10pt;font-size:10pt'>z</div>" +
                "</div>"));

            var c = HeightOf(FindById(root, "twin")!);

            foreach (var id in new[] { "a", "b", "c" })
            {
                Assert.Equal(c + 20, HeightOf(FindById(root, id)!), 2);
            }

            // The single row track is as tall as its tallest item.
            Assert.Equal(c + 20, HeightOf(FindById(root, "grid")!), 2);
        }

        [Fact]
        public async Task GridItemsWithAContentBoxMinHeightAreThatPlusPaddingTall()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div id='grid' class='cards' style='display:grid;grid-template-columns:1fr 1fr'>" +
                "<div id='a' class='cell' style='min-height:40pt;padding:10pt;font-size:10pt'>x</div>" +
                "<div id='b' class='cell' style='min-height:40pt;padding:10pt;font-size:10pt'>y</div>" +
                "</div>"));

            Assert.Equal(60, HeightOf(FindById(root, "a")!), 2);
            Assert.Equal(60, HeightOf(FindById(root, "b")!), 2);
            Assert.Equal(60, HeightOf(FindById(root, "grid")!), 2);
        }

        // ---- Table cells --------------------------------------------------------------------

        [Fact]
        public async Task APaddedTableCellIsItsContentPlusPaddingTall()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<table style='border-spacing:0'><tr><td id='twin' style='padding:0;font-size:10pt'>x</td></tr></table>" +
                "<table style='border-spacing:0'><tr><td id='pad' style='padding:8pt;font-size:10pt'>x</td></tr></table>"));

            var c = HeightOf(FindById(root, "twin")!);

            Assert.Equal(c + 16, HeightOf(FindById(root, "pad")!), 2);
        }

        [Fact]
        public async Task ATableCellWithADeclaredHeightAndPaddingIsThatPlusPaddingTall()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<table style='border-spacing:0'>" +
                "<tr><td style='padding:8pt;font-size:10pt'>x</td></tr>" +
                "<tr><td id='tall' style='height:30pt;padding:8pt;font-size:10pt'>x</td></tr>" +
                "</table>"));

            // 30 content + 16 padding; the bug added the padding a second time (62).
            Assert.Equal(46, HeightOf(FindById(root, "tall")!), 2);
        }

        // ---- Flex ---------------------------------------------------------------------------

        [Fact]
        public async Task PaddedRowFlexItemsAreTheirContentPlusPaddingTall()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div id='twin' style='font-size:10pt'>x</div>" +
                "<div id='flex' style='display:flex'>" +
                "<div id='a' style='padding:10pt;font-size:10pt'>x</div>" +
                "<div id='b' style='padding:10pt;font-size:10pt'>y</div>" +
                "</div>"));

            var c = HeightOf(FindById(root, "twin")!);

            Assert.Equal(c + 20, HeightOf(FindById(root, "a")!), 2);
            Assert.Equal(c + 20, HeightOf(FindById(root, "b")!), 2);
            Assert.Equal(c + 20, HeightOf(FindById(root, "flex")!), 2);
        }

        [Fact]
        public async Task PaddedColumnFlexItemsAreTheirContentPlusPaddingTall()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div id='twin' style='font-size:10pt'>x</div>" +
                "<div id='flex' style='display:flex;flex-direction:column'>" +
                "<div id='a' style='padding:10pt;font-size:10pt'>x</div>" +
                "<div id='b' style='padding:10pt;font-size:10pt'>y</div>" +
                "</div>"));

            var c = HeightOf(FindById(root, "twin")!);

            Assert.Equal(c + 20, HeightOf(FindById(root, "a")!), 2);
            Assert.Equal(c + 20, HeightOf(FindById(root, "b")!), 2);
            Assert.Equal(2 * (c + 20), HeightOf(FindById(root, "flex")!), 2);
        }

        // ---- The change that exposed it must keep working -----------------------------------

        [Fact]
        public async Task ATallInlineBlockInAPaddedBlockCountsTheBlocksPaddingOnce()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div id='twin' style='font-size:10pt'><span style='display:inline-block;height:40pt'>x</span></div>" +
                "<div id='pad' style='padding:10pt;font-size:10pt'><span style='display:inline-block;height:40pt'>x</span></div>"));

            var line = HeightOf(FindById(root, "twin")!);

            Assert.True(line >= 40, $"the 40pt inline-block must still size its line, was {line}");
            Assert.Equal(line + 20, HeightOf(FindById(root, "pad")!), 2);
        }

        [Fact]
        public async Task ANestedInlineBlockWithItsOwnPaddingAndHeightStillSizesItsLine()
        {
            // The case the root-only guard deliberately keeps: for a NESTED atomic inline the compare is
            // border-box against border-box, so the line still reserves the box's padding + declared height.
            var (root, _) = await LayoutAsync(Wrap(
                "<div id='line' style='font-size:10pt'>" +
                "<span id='ib' style='display:inline-block;height:40pt;padding:5pt'>x</span></div>"));

            var ib = HeightOf(FindById(root, "ib")!);

            // content-box height 40 plus 5 + 5 padding.
            Assert.Equal(50, ib, 2);
            Assert.True(HeightOf(FindById(root, "line")!) >= 50,
                $"the line must still reserve the 50pt inline-block, was {HeightOf(FindById(root, "line")!)}");
        }

        [Fact]
        public async Task AWrappingInlineBlockThatIsItsOwnFlowRootCountsItsPaddingOnce()
        {
            // A wrapping inline-block is laid out as a box of its own, so it is a flow root too and hit the
            // same double count: its bottom padding was added to a MaxBottom that already held the padding
            // floor. Expectation relative to an unpadded block of the same width and text.
            var (root, _) = await LayoutAsync(Wrap(
                "<div id='twin' style='width:30pt;font-size:10pt'>xx xx xx</div>" +
                "<div style='font-size:10pt'>a <span id='ib' style='display:inline-block;width:30pt;padding:10pt;min-height:40pt'>xx xx xx</span></div>"));

            var c = HeightOf(FindById(root, "twin")!);

            // content-box min-height 40 plus 10 + 10 padding; the text (about three short lines) is shorter than 40.
            Assert.Equal(System.Math.Max(c, 40) + 20, HeightOf(FindById(root, "ib")!), 2);
        }

        [Fact]
        public async Task ARepeatedLayoutReproducesThePaddedBlocksHeight()
        {
            var html = Wrap(
                "<div id='twin' style='font-size:10pt'>x</div>" +
                "<div id='pad' style='padding:10pt;font-size:10pt'>x</div>");

            var results = await LayoutRepeatedlyAsync(html, 3,
                (root, _) => (Twin: HeightOf(FindById(root, "twin")!), Pad: HeightOf(FindById(root, "pad")!)));

            Assert.All(results, r => Assert.Equal(r.Twin + 20, r.Pad, 2));
            Assert.Single(results.Select(r => System.Math.Round(r.Pad, 2)).Distinct());
        }
    }
}
