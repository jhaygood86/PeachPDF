using System.Threading.Tasks;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// A column subgrid's items take part in the parent's sizing of the auto columns they span (CSS Grid Level 2 section 9.3): each contributes to the
    /// parent column it sits over, rather than the subgrid counting as one ordinary item that spans them. The parent uses
    /// <c>justify-content: start</c> so the auto columns keep the size their contributions give them instead of being stretched to the container.
    /// </summary>
    public class GridSubgridColumnContributionTests
    {
        // Where the second column starts, which is how wide the first one is.
        private static double SecondColumnX(PeachBrackets.Html.Dom.CssBox root) =>
            FindById(root, "b")!.Location.X - FindById(root, "p")!.Location.X;

        private static string Parent(string subgridStyle, string items, string columns = "auto auto") =>
            Wrap("<style>body{margin:0}</style>"
                 + $"<div id='p' style='display:grid;width:300pt;justify-content:start;grid-template-columns:{columns}'>"
                 + $"<div id='sub' style='grid-column:1 / 3;display:grid;grid-template-columns:subgrid;{subgridStyle}'>{items}</div></div>");

        [Fact]
        public async Task AutoColumnsSizeFromTheSubgridsItems()
        {
            var (root, _) = await LayoutAsync(Parent("",
                "<div id='a' style='width:100pt;height:10pt'></div><div id='b' style='width:50pt;height:10pt'></div>"));

            Assert.Equal(100, SecondColumnX(root), 1);
        }

        [Fact]
        public async Task TheSubgridsEdgeIsAddedToTheFirstAndLastColumn()
        {
            // 20pt of padding at the start: the first column holds the 100pt item and that edge.
            var (root, _) = await LayoutAsync(Parent("padding:0 10pt 0 20pt",
                "<div id='a' style='width:100pt;height:10pt'></div><div id='b' style='width:50pt;height:10pt'></div>"));

            Assert.Equal(120, SecondColumnX(root), 1);
            Assert.Equal(180, FindById(root, "sub")!.ActualRight - FindById(root, "p")!.Location.X, 1);
        }

        [Fact]
        public async Task AnItemSpanningTheSubgridsColumnsSharesWhatTheOtherColumnsDoNotCover()
        {
            // The 250pt item spans a 10pt auto column and a 100pt fixed one, so the auto column takes the other 140pt.
            var (root, _) = await LayoutAsync(Parent("",
                "<div id='a' style='width:10pt;height:10pt'></div><div id='b' style='height:10pt'></div>"
                + "<div id='w' style='grid-column:1 / 3;width:250pt;height:10pt'></div>", "auto 100pt"));

            Assert.Equal(150, SecondColumnX(root), 1);
        }

        [Fact]
        public async Task FixedColumnsAreUnaffected()
        {
            var (root, _) = await LayoutAsync(Parent("",
                "<div id='a' style='height:10pt'></div><div id='b' style='height:10pt'></div>", "120pt 80pt"));

            Assert.Equal(120, SecondColumnX(root), 1);
        }

        [Fact]
        public async Task ANestedSubgridContributesToTheOutermostColumns()
        {
            var (root, _) = await LayoutAsync(Wrap("<style>body{margin:0}</style>"
                + "<div id='p' style='display:grid;width:300pt;justify-content:start;grid-template-columns:auto auto'>"
                + "<div style='grid-column:1 / 3;display:grid;grid-template-columns:subgrid'>"
                + "<div style='grid-column:1 / 3;display:grid;grid-template-columns:subgrid'>"
                + "<div id='a' style='width:100pt;height:10pt'></div><div id='b' style='width:50pt;height:10pt'></div></div></div></div>"));

            Assert.Equal(100, SecondColumnX(root), 1);
        }
    }
}
