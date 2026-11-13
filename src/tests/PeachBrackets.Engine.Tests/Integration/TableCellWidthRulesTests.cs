using System.Threading.Tasks;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// Three rules for the width of a table cell that the default cell padding made visible (each cell with padding had been sized as if it had none, and
    /// the figures came out right by accident). Every number is what headless Chrome gives for the same markup, in pt.
    /// </summary>
    public class TableCellWidthRulesTests
    {
        private const string Content60 = "<div style='display:inline-block;width:60px;height:10px'></div>";
        private const string Content20 = "<div style='display:inline-block;width:20px;height:10px'></div>";

        private static async Task<double> WidthOfAsync(string body, string id)
        {
            var (root, _) = await LayoutAsync(Wrap(body));
            var box = FindById(root, id)!;
            return box.ActualRight - box.Location.X;
        }

        [Fact]
        public async Task ASpanningCellHasTheBorderSpacingBetweenItsColumnsAlreadyInItsWidth()
        {
            // The cell needs 60px of content and 1px of padding either side; the two columns it spans and the 2px of spacing between them make that
            // up, so the spacing is not a second thing to add.
            var body = $"<table id='t'><tr><td id='big' rowspan='2' colspan='2'>{Content60}</td><td>{Content20}</td></tr><tr><td>{Content20}</td></tr></table>";

            Assert.Equal(46.5, await WidthOfAsync(body, "big"), 0.05);
            Assert.Equal(67.5, await WidthOfAsync(body, "t"), 0.05);
        }

        [Fact]
        public async Task ASpanningCellWithNoSpacingBetweenItsColumnsNeedsNothingCredited()
        {
            var body = $"<table id='t' style='border-spacing:0'><tr><td id='big' rowspan='2' colspan='2' style='padding:0'>{Content60}</td><td style='padding:0'>{Content20}</td></tr>" +
                       $"<tr><td style='padding:0'>{Content20}</td></tr></table>";

            Assert.Equal(45, await WidthOfAsync(body, "big"), 0.05);
            Assert.Equal(60, await WidthOfAsync(body, "t"), 0.05);
        }

        [Theory]
        [InlineData("padding:20px", 225, 225)]
        [InlineData("padding:0", 225, 225)]
        [InlineData("padding:20px;box-sizing:border-box", 225, 225)]
        public async Task APercentageCellWidthIsTheCellsWholeShareOfTheTable(string cellStyle, double first, double second)
        {
            var body = $"<table id='t' style='width:600px;border-spacing:0'><tr><td id='a' style='width:50%;{cellStyle}'>x</td><td id='b' style='padding:0'>y</td></tr></table>";

            Assert.Equal(first, await WidthOfAsync(body, "a"), 0.05);
            Assert.Equal(second, await WidthOfAsync(body, "b"), 0.05);
        }

        [Fact]
        public async Task APercentageCellWidthIsAShareOfTheGridNotTheSpacing()
        {
            // 50% of the 445.5pt the 450pt table has left after its three 1.5pt spacings, with the default 1px of padding inside it.
            var body = "<table style='width:600px'><tr><td id='a' style='width:50%'>x</td><td id='b'>y</td></tr></table>";

            Assert.Equal(222.75, await WidthOfAsync(body, "a"), 0.05);
            Assert.Equal(222.75, await WidthOfAsync(body, "b"), 0.05);
        }

        [Fact]
        public async Task ALengthCellWidthStillHasItsPaddingOutsideIt()
        {
            var body = "<table style='border-spacing:0'><tr><td id='a' style='width:100px;padding:10px'>x</td></tr></table>";

            Assert.Equal(90, await WidthOfAsync(body, "a"), 0.05);
        }

        [Fact]
        public async Task ADeclaredWidthOfANestedTablesCellIsNotAMinimumForTheOuterCell()
        {
            // The outer cell asks for 80px; what it holds would like 80px + its own padding and spacing, but the column only has to be as wide as the
            // cell's content cannot shrink below, and a cell's declared width is not that. Chrome: the outer cell is 80px plus its 1px padding.
            var body = $"<table id='t'><tr><td id='o' width='80'><table><tr><td width='80'>{Content20}</td></tr></table></td></tr></table>";

            Assert.Equal(61.5, await WidthOfAsync(body, "o"), 0.05);
            Assert.Equal(64.5, await WidthOfAsync(body, "t"), 0.05);
        }

        [Fact]
        public async Task ANestedCellWiderThanTheOuterCellsDeclaredWidthStillWidensIt()
        {
            // Its content cannot shrink below 120px, so that is the minimum whatever the outer cell asked for.
            var body = "<table id='t'><tr><td id='o' width='80'><table><tr><td width='80'><div style='display:inline-block;width:120px;height:10px'></div></td></tr></table></td></tr></table>";

            Assert.Equal(96, await WidthOfAsync(body, "o"), 0.05);
        }

        private static string Block(int px) => $"<div style='display:inline-block;width:{px}px;height:10px'></div>";

        [Fact]
        public async Task APercentageColumnNarrowerThanItsContentTakesTheDifferenceFromTheUnsizedOnesNotTheTable()
        {
            // 10% of 400px is 40px, less than the 90px the cell's content and padding need, so the column is raised to that, and the table is still 400px.
            var body = $"<table id='t' style='width:400px;border-spacing:0'><tr><td id='a' style='width:10%;padding:40px'>{Block(10)}</td><td id='b'>{Block(10)}</td></tr></table>";

            Assert.Equal(300, await WidthOfAsync(body, "t"), 0.05);
            Assert.Equal(67.5, await WidthOfAsync(body, "a"), 0.05);
            Assert.Equal(232.5, await WidthOfAsync(body, "b"), 0.05);
        }

        [Fact]
        public async Task AColumnThatAskedForAShareKeepsItWhenAnotherIsRaisedToItsContent()
        {
            var body = $"<table id='t' style='width:400px;border-spacing:0'><tr><td id='a' style='width:5%;padding:10px'>{Block(20)}</td><td id='b'>{Block(100)}</td><td id='c' style='width:20%'>{Block(20)}</td></tr></table>";

            Assert.Equal(300, await WidthOfAsync(body, "t"), 0.05);
            Assert.Equal(30, await WidthOfAsync(body, "a"), 0.05);
            Assert.Equal(210, await WidthOfAsync(body, "b"), 0.05);
            Assert.Equal(60, await WidthOfAsync(body, "c"), 0.05);
        }

        [Fact]
        public async Task AContentColumnWiderThanItsPercentageStillLeavesTheTableItsDeclaredWidth()
        {
            var body = $"<table id='t' style='width:400px;border-spacing:0'><tr><td id='a' style='width:5%;padding:0'>{Block(60)}</td><td id='b' style='padding:0'>{Block(10)}</td></tr></table>";

            Assert.Equal(300, await WidthOfAsync(body, "t"), 0.05);
            Assert.Equal(45, await WidthOfAsync(body, "a"), 0.05);
            Assert.Equal(255, await WidthOfAsync(body, "b"), 0.05);
        }
    }
}
