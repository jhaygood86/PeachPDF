using System.Threading.Tasks;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// Flexible rows of a grid with no definite height (CSS Grid section 12.7.1, the indefinite free space case): the flex fraction is the
    /// largest ratio any row's content asks for, so <c>1fr 2fr</c> over equal content is a 1:2 pair of rows rather than two rows of their own
    /// content.
    /// </summary>
    public class GridFlexibleRowsIndefiniteHeightTests
    {
        private static async Task<(double RowA, double Total)> Rows(string template, string aStyle, string bStyle, string gridStyle = "")
        {
            var (root, _) = await LayoutAsync(Wrap("<style>body{margin:0}</style>"
                + $"<div id='g' style='display:grid;width:100pt;grid-template-rows:{template};{gridStyle}'>"
                + $"<div id='a' style='{aStyle}'></div><div id='b' style='{bStyle}'></div></div>"));
            var g = FindById(root, "g")!;
            return (FindById(root, "b")!.Location.Y - g.Location.Y, g.ActualBoxSizingHeight);
        }

        [Fact]
        public async Task EqualFactorsOverEqualContentStayContentSized()
        {
            var (rowA, total) = await Rows("1fr 1fr", "height:30pt", "height:30pt");

            Assert.Equal(30, rowA, 1);
            Assert.Equal(60, total, 1);
        }

        [Fact]
        public async Task OneFrAndTwoFrOverEqualContentAreInRatio()
        {
            var (rowA, total) = await Rows("1fr 2fr", "height:30pt", "height:30pt");

            Assert.Equal(30, rowA, 1);
            Assert.Equal(90, total, 1);
        }

        [Fact]
        public async Task TheRowAskingForTheLargestRatioSetsTheFraction()
        {
            // 60pt in the 1fr row is a fraction of 60, so the 2fr row is 120pt even though its own content is 30pt.
            var (rowA, total) = await Rows("1fr 2fr", "height:60pt", "height:30pt");

            Assert.Equal(60, rowA, 1);
            Assert.Equal(180, total, 1);
        }

        [Fact]
        public async Task AFlexFactorUnderOneCountsAsOneForTheRatio()
        {
            // 0.5fr over 30pt asks for a fraction of 30 (not 60), and its half share of that is under its content, which it keeps.
            var (rowA, total) = await Rows("0.5fr 1fr", "height:30pt", "height:30pt");

            Assert.Equal(30, rowA, 1);
            Assert.Equal(60, total, 1);
        }

        [Fact]
        public async Task ARowCannotBeSmallerThanItsContent()
        {
            // Fraction 40 (from the 2fr row's 80pt content over 2) makes the 1fr row 40pt, above its own 10pt.
            var (rowA, total) = await Rows("1fr 2fr", "height:10pt", "height:80pt");

            Assert.Equal(40, rowA, 1);
            Assert.Equal(120, total, 1);
        }

        [Fact]
        public async Task AnItemSpanningFlexibleRowsSetsTheFractionForThem()
        {
            // The spanning item needs 90pt across 1fr + 2fr: a fraction of 30, so 30pt and 60pt.
            var (root, _) = await LayoutAsync(Wrap("<style>body{margin:0}</style>"
                + "<div id='g' style='display:grid;width:100pt;grid-template-rows:1fr 2fr;grid-template-columns:50pt 50pt'>"
                + "<div id='a' style='grid-row:1;grid-column:1;height:10pt'></div>"
                + "<div id='b' style='grid-row:2;grid-column:1;height:10pt'></div>"
                + "<div id='c' style='grid-row:1 / 3;grid-column:2;height:90pt'></div></div>"));
            var g = FindById(root, "g")!;

            Assert.Equal(30, FindById(root, "b")!.Location.Y - g.Location.Y, 1);
            Assert.Equal(90, g.ActualBoxSizingHeight, 1);
        }

        [Fact]
        public async Task ATallerMinHeightIsSharedByTheFactors()
        {
            var (rowA, total) = await Rows("1fr 2fr", "height:10pt", "height:10pt", "min-height:120pt");

            Assert.Equal(40, rowA, 1);
            Assert.Equal(120, total, 1);
        }

        [Fact]
        public async Task ADefiniteHeightIsUnchanged()
        {
            var (rowA, total) = await Rows("1fr 2fr", "", "", "height:90pt");

            Assert.Equal(30, rowA, 1);
            Assert.Equal(90, total, 1);
        }

        [Fact]
        public async Task AutoRowsAreNotInflated()
        {
            var (rowA, total) = await Rows("auto 2fr", "height:10pt", "height:30pt");

            Assert.Equal(10, rowA, 1);
            Assert.Equal(10 + 30, total, 1);
        }
    }
}
