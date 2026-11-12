using System.Threading.Tasks;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// An item that grows and has an <c>auto</c> flex-basis starts from its content size and shares the free space on top of it
    /// (CSS Flexbox 1 sections 7.1 and 9.7), where it used to start from 0 and split the line by the grow factors alone. Every
    /// expected width is what headless Chrome gave for the same markup, in pt.
    /// </summary>
    public class FlexGrowAutoBasisTests
    {
        private static string Block(double width, double height = 10) => $"<div style='width:{width}pt;height:{height}pt'></div>";

        private static async Task<(double A, double B)> WidthsAsync(string containerWidth, string firstStyle, string firstContent,
            string secondStyle, string secondContent)
        {
            var (root, _) = await LayoutAsync(Wrap(
                $"<div style='display:flex;width:{containerWidth}'><div id='a' style='{firstStyle}'>{firstContent}</div>" +
                $"<div id='b' style='{secondStyle}'>{secondContent}</div></div>"));

            return (FindById(root, "a")!.ActualBoxSizingWidth, FindById(root, "b")!.ActualBoxSizingWidth);
        }

        [Fact]
        public async Task TheFreeSpaceIsSharedOnTopOfEachItemsContent()
        {
            // 400pt less 100pt and 40pt of content is 260pt, half to each.
            var (a, b) = await WidthsAsync("400pt", "flex:1 1 auto", Block(100), "flex:1 1 auto", Block(40));

            Assert.Equal(230, a, 0.01);
            Assert.Equal(170, b, 0.01);
        }

        [Fact]
        public async Task TheGrowFactorsWeighTheShareNotTheContent()
        {
            var (a, b) = await WidthsAsync("400pt", "flex-grow:1", Block(100), "flex-grow:2", Block(40));

            Assert.Equal(186.67, a, 0.01);
            Assert.Equal(213.33, b, 0.01);
        }

        [Fact]
        public async Task PaddingIsPartOfTheContentSizeTheShareIsAddedTo()
        {
            var (a, b) = await WidthsAsync("400pt", "flex-grow:1;padding:0 20pt", Block(100), "flex-grow:1", Block(40));

            Assert.Equal(250, a, 0.01);
            Assert.Equal(150, b, 0.01);
        }

        [Fact]
        public async Task AZeroBasisStillSplitsTheLineByTheGrowFactorsAlone()
        {
            var (a, b) = await WidthsAsync("400pt", "flex:1", Block(100), "flex:1", Block(40));

            Assert.Equal(200, a, 0.01);
            Assert.Equal(200, b, 0.01);
        }

        [Fact]
        public async Task AMinWidthDoesNotRaiseTheBaseTheFreeSpaceIsSharedOn()
        {
            // The second item's content is 20pt, below its 120pt minimum, but its flex base size is still 20pt: 400 - 100 - 20 =
            // 280pt of free space, 140pt each. Taking 120pt as its base would leave 180pt and give 190pt / 210pt.
            var (a, b) = await WidthsAsync("400pt", "flex-grow:1", Block(100), "flex-grow:1;min-width:120pt", Block(20));

            Assert.Equal(240, a, 0.01);
            Assert.Equal(160, b, 0.01);
        }

        [Fact]
        public async Task AMaxWidthHoldsAGrowingItemAndTheRestTakesWhatItCannotUse()
        {
            // The first item's 200pt of content is wider than its 100pt maximum, so it is frozen there rather than sharing
            // the free space, and the second takes the other 200pt.
            var (a, b) = await WidthsAsync("300pt", "flex-grow:1;max-width:100pt", Block(200), "flex-grow:1", Block(10));

            Assert.Equal(100, a, 0.01);
            Assert.Equal(200, b, 0.01);
        }

        [Fact]
        public async Task ContentFarWiderThanTheLineButHeldByAMaxWidthStillLetsTheLineGrow()
        {
            // 1000pt of content would overflow the 300pt line, but the item is held to 100pt, so the line has room and the other item takes it.
            var (a, b) = await WidthsAsync("300pt", "flex-grow:1;max-width:100pt", Block(1000), "flex-grow:1", Block(10));

            Assert.Equal(100, a, 0.01);
            Assert.Equal(200, b, 0.01);
        }

        [Fact]
        public async Task AnExplicitBasisHeldByAMaxWidthLeavesTheRestToTheOtherItems()
        {
            var (a, b) = await WidthsAsync("600pt", "flex:1 1 40pt;max-width:80pt", Block(10), "flex:1 1 auto", Block(10));

            Assert.Equal(80, a, 0.01);
            Assert.Equal(520, b, 0.01);
        }

        [Fact]
        public async Task AMinWidthViolationIsMadeUpByTheItemsThatGrow()
        {
            // The first item's 40pt basis is under its 60pt minimum, so it is raised and the growing item has 20pt less.
            var (a, b) = await WidthsAsync("600pt", "flex:0 1 40pt;min-width:60pt", Block(10), "flex:1 1 auto", Block(10));

            Assert.Equal(60, a, 0.01);
            Assert.Equal(540, b, 0.01);
        }

        [Fact]
        public async Task AGrowingZeroBasisItemOnAnOverflowingLineKeepsItsContent()
        {
            // The first item's 250pt of content overflows the 200pt line, so the second (a basis of 0) is not left at nothing:
            // it is raised to the 30pt of its own content.
            var (a, b) = await WidthsAsync("200pt", "flex:1 1 auto", Block(250), "flex:1 1 0%", Block(30));

            Assert.Equal(250, a, 0.01);
            Assert.Equal(30, b, 0.01);
        }

        [Fact]
        public async Task AGrowingZeroBasisItemThatCannotShrinkKeepsItsContentOnAnOverflowingLine()
        {
            var (a, b) = await WidthsAsync("200pt", "flex:1 1 auto", Block(250), "flex:1 0 0%", Block(30));

            Assert.Equal(250, a, 0.01);
            Assert.Equal(30, b, 0.01);
        }

        [Fact]
        public async Task AWrappingRowThatFitsOneLineIsSizedTheSame()
        {
            // Both items fit one line (140pt of content in 400pt), so wrapping changes nothing.
            var (root, _) = await LayoutAsync(Wrap(
                $"<div style='display:flex;flex-wrap:wrap;width:400pt'><div id='a' style='flex:1 1 auto'>{Block(100)}</div>" +
                $"<div id='b' style='flex:1 1 auto'>{Block(40)}</div></div>"));

            Assert.Equal(230, FindById(root, "a")!.ActualBoxSizingWidth, 0.01);
            Assert.Equal(170, FindById(root, "b")!.ActualBoxSizingWidth, 0.01);
        }

        [Fact]
        public async Task ABorderBoxItemIsHeldToItsMaxWidthAsAWholeNotItsContent()
        {
            // max-width on a border-box item is the border box: 100pt, not 100pt plus the 40pt of padding.
            var (a, b) = await WidthsAsync("400pt", "flex-grow:1;box-sizing:border-box;padding:20pt;max-width:100pt", Block(1000),
                "flex-grow:1", Block(10));

            Assert.Equal(100, a, 0.01);
            Assert.Equal(300, b, 0.01);
        }

        [Fact]
        public async Task APercentageMaxWidthResolvesAgainstTheLine()
        {
            var (a, b) = await WidthsAsync("400pt", "flex-grow:1;max-width:25%", Block(300), "flex-grow:1", Block(10));

            Assert.Equal(100, a, 0.01);
            Assert.Equal(300, b, 0.01);
        }

        [Fact]
        public async Task MarginsAreOutsideTheContentTheShareIsAddedTo()
        {
            // 400 - (100 + 40 content) - 40 of margin = 220pt of free space, 110 each.
            var (a, b) = await WidthsAsync("400pt", "flex:1 1 auto;margin:0 20pt", Block(100), "flex:1 1 auto", Block(40));

            Assert.Equal(210, a, 0.01);
            Assert.Equal(150, b, 0.01);
        }

        [Fact]
        public async Task AWrappingRowSizesEachLineFromItsOwnContent()
        {
            // 250 + 100 fit the 400pt line (50pt free, 25 each); the third item's 100pt does not and has a line to itself.
            var (root, _) = await LayoutAsync(Wrap(
                $"<div style='display:flex;flex-wrap:wrap;width:400pt'><div id='a' style='flex:1 1 auto'>{Block(250)}</div>" +
                $"<div id='b' style='flex:1 1 auto'>{Block(100)}</div><div id='c' style='flex:1 1 auto'>{Block(100)}</div></div>"));

            Assert.Equal(275, FindById(root, "a")!.ActualBoxSizingWidth, 0.01);
            Assert.Equal(125, FindById(root, "b")!.ActualBoxSizingWidth, 0.01);
            Assert.Equal(400, FindById(root, "c")!.ActualBoxSizingWidth, 0.01);
        }

        [Fact]
        public async Task AColumnItemThatGrowsStartsFromItsContentHeight()
        {
            // 300pt tall, 40pt and 100pt of content: 160pt of free space, 80 each (120 / 180 in Chrome).
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='display:flex;flex-direction:column;height:300pt;width:100pt'>" +
                $"<div id='a' style='flex-grow:1'>{Block(50, 40)}</div><div id='b' style='flex-grow:1'>{Block(50, 100)}</div></div>"));

            Assert.Equal(120, FindById(root, "a")!.ActualBoxSizingHeight, 0.01);
            Assert.Equal(180, FindById(root, "b")!.ActualBoxSizingHeight, 0.01);
        }
    }
}
