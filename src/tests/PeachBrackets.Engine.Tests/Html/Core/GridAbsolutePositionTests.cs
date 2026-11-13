using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Html.Core
{
    /// <summary>
    /// An absolutely positioned child of a grid container that is its containing block is placed against the grid area its <c>grid-column</c> and
    /// <c>grid-row</c> lines name (css-grid-1 section 9): the area is where its insets and percentages resolve, and where it sits when the insets are auto.
    /// </summary>
    public class GridAbsolutePositionTests
    {
        // 20pt of border + padding on the left and top: the content box starts at (client.X, client.Y).
        private static string Grid(string children, string extra = "") =>
            LayoutHarness.Wrap(
                "<div id='g' style='display:grid;grid:100pt 80pt / 120pt 150pt;position:relative;width:400pt;height:300pt;padding:10pt;border:4pt solid;" + extra + "'>"
                + children + "</div>");

        private static async Task<(CssBox G, CssBox Child)> LayoutAsync(string children, string extra = "")
        {
            var (root, _) = await LayoutHarness.LayoutAsync(Grid(children, extra));
            return (LayoutHarness.FindById(root, "g")!, LayoutHarness.FindById(root, "a")!);
        }

        [Fact]
        public async Task WithAutoInsets_TheBoxSitsAtTheCornerOfItsArea()
        {
            var (g, a) = await LayoutAsync("<div id='a' style='position:absolute;grid-column:2/3;grid-row:2/3;width:10pt;height:10pt'></div>");

            Assert.Equal(g.ClientLeft + 120, a.Location.X, 2); // after the 120pt first column
            Assert.Equal(g.ClientTop + 100, a.Location.Y, 2);  // after the 100pt first row
        }

        [Fact]
        public async Task AnInset_IsMeasuredFromTheArea_NotThePaddingBox()
        {
            var (g, a) = await LayoutAsync("<div id='a' style='position:absolute;grid-column:2/3;grid-row:1/2;left:7pt;top:5pt;width:10pt;height:10pt'></div>");

            Assert.Equal(g.ClientLeft + 120 + 7, a.Location.X, 2);
            Assert.Equal(g.ClientTop + 5, a.Location.Y, 2);
        }

        [Fact]
        public async Task APercentageSize_IsOfTheArea()
        {
            var (_, a) = await LayoutAsync("<div id='a' style='position:absolute;grid-column:2/3;grid-row:1/2;width:50%;height:25%'></div>");

            Assert.Equal(75, a.ActualWidth, 2);   // half of the 150pt column
            Assert.Equal(25, a.ActualHeight, 2);  // a quarter of the 100pt row
        }

        [Fact]
        public async Task ALineThatDoesNotExist_IsThePaddingEdge()
        {
            // Lines 1 / 9: there are only 3 column lines, so the area runs from the first column to the grid's padding edge (its right edge).
            var (g, a) = await LayoutAsync("<div id='a' style='position:absolute;grid-column:1/9;grid-row:1/2;left:0;right:0;height:10pt'></div>");

            Assert.Equal(g.ClientLeft, a.Location.X, 2);
            Assert.Equal(g.ClientRight + 10, a.ActualRight, 2); // the padding edge: content right + 10pt padding
        }

        [Fact]
        public async Task AChildWithNoGridLines_StillSitsInTheAreaOfTheWholePaddingBox()
        {
            var (g, a) = await LayoutAsync("<div id='a' style='position:absolute;width:10pt;height:10pt'></div>");

            // auto lines on both axes: the padding box, so the static corner is the padding box's corner.
            Assert.Equal(g.ClientLeft - 10, a.Location.X, 2);
            Assert.Equal(g.ClientTop - 10, a.Location.Y, 2);
        }
    }
}
