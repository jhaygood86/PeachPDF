using System.Threading.Tasks;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// A subgrid's margin, border and padding do not move its adopted tracks off the parent's lines (CSS Grid Level 2 section 9.2): the tracks stay
    /// where the parent has them, and the items in the first and last track are inset by the subgrid's own edge instead.
    /// </summary>
    public class GridSubgridInsetTests
    {
        private static string Columns(string subgridStyle) =>
            Wrap("<style>body{margin:0}</style>"
                 + "<div id='p' style='display:grid;width:300pt;grid-template-columns:100pt 100pt 100pt'>"
                 + $"<div id='sub' style='grid-column:1 / 4;display:grid;grid-template-columns:subgrid;{subgridStyle}'>"
                 + "<div id='a' style='height:10pt'></div><div id='b' style='height:10pt'></div><div id='c' style='height:10pt'></div></div></div>");

        private static double X(PeachBrackets.Html.Dom.CssBox root, string id) =>
            FindById(root, id)!.Location.X - FindById(root, "p")!.Location.X;

        [Fact]
        public async Task PaddingInsetsTheEdgeItemsAndLeavesTheMiddleTrackOnItsLine()
        {
            var (root, _) = await LayoutAsync(Columns("padding:0 10pt 0 20pt"));

            Assert.Equal(20, X(root, "a"), 1);
            Assert.Equal(100, X(root, "b"), 1);
            Assert.Equal(200, X(root, "c"), 1);
            Assert.Equal(80, FindById(root, "a")!.ActualBoxSizingWidth, 1);
            Assert.Equal(100, FindById(root, "b")!.ActualBoxSizingWidth, 1);
            Assert.Equal(90, FindById(root, "c")!.ActualBoxSizingWidth, 1);
        }

        [Fact]
        public async Task BorderAndMarginCountToo()
        {
            // A 5pt border and 10pt margin at the start: the margin is the parent cell's own and the border insets the first item, so the
            // tracks still start at the parent's first line.
            var (root, _) = await LayoutAsync(Columns("margin-left:10pt;border-left:5pt solid black"));

            Assert.Equal(15, X(root, "a"), 1);
            Assert.Equal(100, X(root, "b"), 1);
            Assert.Equal(200, X(root, "c"), 1);
        }

        [Fact]
        public async Task ASubgridWithoutEdgesIsUnchanged()
        {
            var (root, _) = await LayoutAsync(Columns(""));

            Assert.Equal(0, X(root, "a"), 1);
            Assert.Equal(100, X(root, "b"), 1);
            Assert.Equal(100, FindById(root, "a")!.ActualBoxSizingWidth, 1);
        }

        [Fact]
        public async Task RowPaddingInsetsTheFirstAndLastRow()
        {
            var (root, _) = await LayoutAsync(Wrap("<style>body{margin:0}</style>"
                + "<div id='p' style='display:grid;width:100pt;grid-template-rows:50pt 50pt'>"
                + "<div id='sub' style='grid-row:1 / 3;display:grid;grid-template-rows:subgrid;padding:8pt 0 4pt'>"
                + "<div id='a'></div><div id='b'></div></div></div>"));
            var top = FindById(root, "p")!.Location.Y;

            Assert.Equal(8, FindById(root, "a")!.Location.Y - top, 1);
            Assert.Equal(42, FindById(root, "a")!.ActualBoxSizingHeight, 1);
            Assert.Equal(50, FindById(root, "b")!.Location.Y - top, 1);
            Assert.Equal(46, FindById(root, "b")!.ActualBoxSizingHeight, 1);
        }

        [Fact]
        public async Task AnAutoRowGrowsToFitTheSubgridsPadding()
        {
            // The subgrid's rows report their content (10pt + 10pt); the parent's auto rows must also hold the 8pt and 4pt edges.
            var (root, _) = await LayoutAsync(Wrap("<style>body{margin:0}</style>"
                + "<div id='p' style='display:grid;width:100pt;grid-template-rows:auto auto'>"
                + "<div id='sub' style='grid-row:1 / 3;display:grid;grid-template-rows:subgrid;padding:8pt 0 4pt'>"
                + "<div id='a' style='height:10pt'></div><div id='b' style='height:10pt'></div></div></div>"));
            var top = FindById(root, "p")!.Location.Y;

            Assert.Equal(8, FindById(root, "a")!.Location.Y - top, 1);
            Assert.Equal(8 + 10 + 10 + 4, FindById(root, "p")!.ActualBottom - top, 1);
        }
    }
}
