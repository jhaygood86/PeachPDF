using PeachBrackets.Html.Dom;
using System.Threading.Tasks;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// A multi-column container is its column repeated side by side, so its shrink-to-fit width is the column's times the columns plus
    /// the gaps between them. It used to be the width of one column, so a float (or flex item, or table cell) holding the container
    /// was too narrow for its own columns. css-multicol-1 leaves the used width to the sizing specs; the formulas
    /// asserted here are the ones headless Chrome applies (see TryMeasureMulticolWidth).
    /// </summary>
    public class MulticolIntrinsicWidthTests
    {
        private const string Block = "<div style='width:75pt;height:10pt'></div>";

        private static async Task<CssBox> FloatAsync(string style, string content, string? wrapperStyle = null)
        {
            var floated = $"<div id='f' style='float:left;{style}'>{content}</div>";
            var body = wrapperStyle is null ? floated : $"<div style='{wrapperStyle}'>{floated}</div>";
            var (root, _) = await LayoutAsync(Wrap(body));
            return FindById(root, "f")!;
        }

        private static double WidthOf(CssBox box) => box.ActualRight - box.Location.X;

        private static async Task<double> WidthOfIdAsync(string body)
        {
            var (root, _) = await LayoutAsync(Wrap(body));
            return WidthOf(FindById(root, "f")!);
        }

        [Fact]
        public async Task AnInlineBlock_WithColumnsAndAutoWidth_ShrinkWrapsItsColumns()
        {
            var width = await WidthOfIdAsync(
                "<div id='f' style='display:inline-block;columns:2;column-gap:7.5pt'>" + Block + Block + "</div>");

            Assert.Equal(157.5, width, 0.01);
        }

        [Theory]
        [InlineData("float:left")]
        [InlineData("display:inline-block")]
        [InlineData("overflow:hidden")]
        [InlineData("")]
        public async Task AnAutoHeightContainer_BalancesItsColumns_WhateverFormattingContextItIs(string kind)
        {
            var (root, _) = await LayoutAsync(Wrap(
                $"<div id='f' style='{kind};width:300pt;columns:2;column-gap:10pt'>" +
                "<div id='a' style='width:100pt;height:40pt'></div><div id='b' style='width:100pt;height:40pt'></div></div>"));

            var f = FindById(root, "f")!;
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;

            Assert.Equal(a.Location.Y, b.Location.Y, 0.01);
            Assert.Equal(155, b.Location.X - a.Location.X, 0.01);
            Assert.Equal(40, f.ActualBottom - f.Location.Y, 0.5);
        }

        [Fact]
        public async Task TwoColumns_AreTwiceTheColumnPlusTheGap()
        {
            var box = await FloatAsync("columns:2;column-gap:7.5pt", Block + Block);

            Assert.Equal(157.5, WidthOf(box), 0.01);
        }

        [Fact]
        public async Task ThreeColumnsOfOneBlock_ReserveAllThreeColumnsAndBothGaps()
        {
            var box = await FloatAsync("columns:3;column-gap:7.5pt", Block);

            Assert.Equal(240, WidthOf(box), 0.01);
        }

        [Fact]
        public async Task AChildsMaxWidthCapsTheColumnNotTheContainer()
        {
            // The reduction of the report: each child is capped at 75pt, so the column is 75pt and two of them fit side by side.
            var box = await FloatAsync("columns:2;column-gap:7.5pt",
                "<div style='width:172.5pt;max-width:75pt'>s</div><div style='width:172.5pt;max-width:75pt'>s</div>");

            Assert.Equal(157.5, WidthOf(box), 0.01);
        }

        [Fact]
        public async Task BorderAndPaddingBelongToTheContainerAndAreAddedOnce()
        {
            var box = await FloatAsync("columns:2;column-gap:7.5pt;padding:0 5pt;border:2pt solid", Block);

            Assert.Equal(157.5 + 14, WidthOf(box), 0.01);
        }

        [Fact]
        public async Task AChildsOwnPaddingIsPartOfEachColumn()
        {
            var box = await FloatAsync("columns:2;column-gap:7.5pt", "<div style='width:75pt;padding:0 4pt'>s</div>");

            Assert.Equal(2 * 83 + 7.5, WidthOf(box), 0.01);
        }

        [Fact]
        public async Task APercentageGapHasNothingToResolveAgainstAndCountsAsNone()
        {
            var box = await FloatAsync("columns:2;column-gap:20%", Block);

            Assert.Equal(150, WidthOf(box), 0.01);
        }

        [Fact]
        public async Task ANormalGapIsOneEm()
        {
            var box = await FloatAsync("columns:2;font-size:10pt", Block);

            Assert.Equal(160, WidthOf(box), 0.01);
        }

        [Fact]
        public async Task OneColumnIsUnchanged()
        {
            var box = await FloatAsync("columns:1;column-gap:7.5pt", Block);

            Assert.Equal(75, WidthOf(box), 0.01);
        }

        [Fact]
        public async Task AColumnWidthIsAFloorOnTheMaxContentColumn()
        {
            // With a column-width and no count the container is one column wide, but never narrower than the column-width.
            var box = await FloatAsync("columns:60pt;column-gap:7.5pt", "<div style='width:20pt;height:10pt'></div>");

            Assert.Equal(60, WidthOf(box), 0.01);
        }

        [Fact]
        public async Task AColumnWidthWithACount_RepeatsTheFlooredColumn()
        {
            var box = await FloatAsync("columns:3 60pt;column-gap:7.5pt", "<div style='width:20pt;height:10pt'></div>");

            Assert.Equal(3 * 60 + 2 * 7.5, WidthOf(box), 0.01);
        }

        [Fact]
        public async Task AContentWiderThanTheColumnWidthWidensTheColumn()
        {
            var box = await FloatAsync("columns:2 60pt;column-gap:7.5pt", Block);

            Assert.Equal(157.5, WidthOf(box), 0.01);
        }

        [Fact]
        public async Task ShrunkToItsMinimumAColumnCountMultipliesTheMinimum()
        {
            // In a 1pt box the float takes its min-content width: the content cannot be narrower than its column, once per column.
            var box = await FloatAsync("columns:2;column-gap:7.5pt", Block + Block, wrapperStyle: "width:1pt");

            Assert.Equal(157.5, WidthOf(box), 0.01);
        }

        [Fact]
        public async Task ShrunkToItsMinimumAColumnWidthCapsTheMinimumAtTheColumnWidth()
        {
            // With a column-width the container can drop to one column, and a block wider than it overflows that column: 60pt, not 75pt.
            var box = await FloatAsync("columns:60pt;column-gap:7.5pt", Block, wrapperStyle: "width:1pt");

            Assert.Equal(60, WidthOf(box), 0.01);
        }

        [Fact]
        public async Task AMulticolContainerInsideABlockInsideAFloatIsMeasuredToo()
        {
            var box = await FloatAsync("", "<div><div style='columns:2;column-gap:7.5pt'>" + Block + Block + "</div></div>");

            Assert.Equal(157.5, WidthOf(box), 0.01);
        }

        [Fact]
        public async Task AFlexItemWithAutoBasisIsItsColumnsWide()
        {
            var width = await WidthOfIdAsync(
                "<div style='display:flex;width:400pt'><div id='f' style='flex:none;columns:2;column-gap:7.5pt'>" + Block + Block + "</div></div>");

            Assert.Equal(157.5, width, 0.01);
        }

        [Fact]
        public async Task AMulticolContainerInATableCellSizesItsColumn()
        {
            var width = await WidthOfIdAsync(
                "<table id='f' style='border-spacing:0'><tr><td style='padding:0'><div style='columns:2;column-gap:7.5pt'>" + Block + Block + "</div></td></tr></table>");

            Assert.Equal(157.5, width, 0.01);
        }

        [Fact]
        public async Task AnEmptyContainerStillReservesItsGaps()
        {
            // Chrome gives an empty `columns: 3` float the two gaps, nothing else.
            var box = await FloatAsync("columns:3;column-gap:7.5pt", "");

            Assert.Equal(15, WidthOf(box), 0.01);
        }

        [Fact]
        public async Task ColumnsOnAnInlineBox_ChangeNothing()
        {
            var with = await FloatAsync("", "<span style='columns:2;column-gap:7.5pt'>hello world</span>");
            var without = await FloatAsync("", "<span>hello world</span>");

            Assert.Equal(WidthOf(without), WidthOf(with), 0.01);
        }

        [Fact]
        public async Task ColumnsOnAFlexColumnContainer_ChangeNothing()
        {
            var box = await FloatAsync("display:flex;flex-direction:column;columns:2;column-gap:7.5pt", Block);

            Assert.Equal(75, WidthOf(box), 0.01);
        }

        [Fact]
        public async Task ColumnsOnATable_ChangeNothing()
        {
            var with = await FloatAsync("", "<table style='columns:2;column-gap:7.5pt;border-spacing:0'><tr><td style='padding:0'>" + Block + "</td></tr></table>");
            var without = await FloatAsync("", "<table style='border-spacing:0'><tr><td style='padding:0'>" + Block + "</td></tr></table>");

            Assert.Equal(WidthOf(without), WidthOf(with), 0.01);
        }

        [Fact]
        public async Task AVerticalWritingModeContainerIsLeftAlone()
        {
            // A vertical box is one line (about a line height) wide whatever its columns; measured as the horizontal text it holds it would be
            // two columns of "text text" plus a gap.
            var box = await FloatAsync("writing-mode:vertical-rl;columns:2;column-gap:7.5pt", "text text");

            Assert.InRange(WidthOf(box), 1, 30);
        }

        private static string Spanner(string width) => $"<div style='column-span:all;width:{width};height:10pt'></div>";

        [Fact]
        public async Task AColumnSpannerWiderThanTheColumnsSetsTheWidth()
        {
            // 37.5pt columns would give 2 * 37.5 + 7.5 = 82.5pt, but the spanner is 225pt and spans them all.
            var box = await FloatAsync("columns:2;column-gap:7.5pt", "<div style='width:37.5pt;height:10pt'></div>" + Spanner("225pt")
                + "<div style='width:37.5pt;height:10pt'></div>");

            Assert.Equal(225, WidthOf(box), 0.01);
        }

        [Fact]
        public async Task AColumnSpannerNarrowerThanTheColumnsIsNotPartOfThem()
        {
            // Measured with the columns' content it would be a third column's worth: 3 * 75 + 2 * 7.5.
            var box = await FloatAsync("columns:2;column-gap:7.5pt", Block + Spanner("112.5pt") + Block);

            Assert.Equal(157.5, WidthOf(box), 0.01);
        }

        [Fact]
        public async Task ShrunkToItsMinimumASpannerStillSetsAFloor()
        {
            var box = await FloatAsync("columns:2;column-gap:7.5pt", "<div style='width:37.5pt;height:10pt'></div>" + Spanner("225pt"),
                wrapperStyle: "width:1pt");

            Assert.Equal(225, WidthOf(box), 0.01);
        }

        [Fact]
        public async Task AColumnWidthMinimumIsNeverBelowOnePixel()
        {
            var box = await FloatAsync("columns:60pt;column-gap:7.5pt", "", wrapperStyle: "width:0");

            Assert.Equal(0.75, WidthOf(box), 0.01);
        }
    }
}
