using PeachBrackets.Html.Dom;
using System.Threading.Tasks;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// Flexible rows are expanded against a definite container height (CSS Grid section 12.7) from their base sizes: a bare <c>fr</c> row is
    /// <c>minmax(auto, fr)</c>, so its items' automatic minimum (section 6.6) is the floor, which a scroll container, an authored
    /// <c>min-height</c> and <c>minmax(0, 1fr)</c> opt out of.
    /// </summary>
    public class GridFlexibleRowsTests
    {
        private static async Task<(CssBox Root, double RowAHeight, double RowBTop, double RowCTop)> Rows(
            string template, string height, string aStyle = "", string aContent = "x")
        {
            var (root, _) = await LayoutAsync(Wrap("<style>body{margin:0}</style>"
                + $"<div id='g' style='display:grid;width:100pt;grid-template-rows:{template};{height}'>"
                + $"<div id='a' style='{aStyle}'>{aContent}</div><div id='b'>y</div>"
                // Only as many items as rows, or the extra one makes an implicit auto row of its own.
                + (template.Split(' ').Length > 2 ? "<div id='c'>z</div>" : "") + "</div>"));
            var top = FindById(root, "g")!.Location.Y;
            var c = FindById(root, "c");
            return (root, FindById(root, "b")!.Location.Y - top, FindById(root, "b")!.Location.Y - top, c is null ? 0 : c.Location.Y - top);
        }

        private const string Tall = "<div style='height:150pt'></div>";

        [Fact]
        public async Task TwoFrRowsShareTheDefiniteHeight()
        {
            var (_, aHeight, bTop, _) = await Rows("1fr 1fr", "height:100pt");

            Assert.Equal(50, bTop, 1);
            Assert.Equal(50, aHeight, 1);
        }

        [Fact]
        public async Task FrRowsShareWhatAnAutoRowLeaves()
        {
            // The auto row is its content (one line); the 1fr and 2fr rows split the rest 1:2.
            var (root, _, bTop, cTop) = await Rows("1fr auto 2fr", "height:100pt");
            var autoRow = cTop - bTop;

            Assert.Equal((100 - autoRow) / 3, bTop, 1);
            Assert.Equal(100, cTop + (100 - autoRow) * 2 / 3, 1);
        }

        [Fact]
        public async Task ABareFrRowKeepsItsItemsAutomaticMinimum()
        {
            // 150pt of content cannot be shared down to 50pt: the row keeps it and the second row takes only its own minimum.
            var (_, _, bTop, _) = await Rows("1fr 1fr", "height:100pt", aContent: Tall);

            Assert.Equal(150, bTop, 1);
        }

        [Theory]
        [InlineData("1fr 1fr", "overflow:hidden")]
        [InlineData("1fr 1fr", "min-height:0")]
        [InlineData("minmax(0,1fr) 1fr", "")]
        public async Task AnExemptRowShrinksToItsShare(string template, string aStyle)
        {
            var (_, _, bTop, _) = await Rows(template, "height:100pt", aStyle, Tall);

            Assert.Equal(50, bTop, 1);
        }

        [Fact]
        public async Task AFixedMinimumIsTheFloorOfAMinmaxRow()
        {
            var (_, _, bTop, _) = await Rows("minmax(80pt, 1fr) 1fr", "height:100pt");

            Assert.Equal(80, bTop, 1);
        }

        [Fact]
        public async Task AnAutoHeightGridKeepsContentSizedFrRows()
        {
            var (_, _, bTop, cTop) = await Rows("1fr 1fr", "");

            Assert.True(bTop < 30, $"the first row is its content, was {bTop}");
            Assert.True(cTop - bTop < 30, $"the second row is its content, was {cTop - bTop}");
        }

        [Fact]
        public async Task AFlexFactorSumBelowOneTakesThatFractionOfTheSpace()
        {
            var (root, _) = await LayoutAsync(Wrap("<style>body{margin:0}</style>"
                + "<div style='display:grid;width:100pt;grid-template-columns:0.5fr'><div id='a'>x</div></div>"));

            Assert.Equal(50, FindById(root, "a")!.ActualBoxSizingWidth, 1);
        }
    }
}
