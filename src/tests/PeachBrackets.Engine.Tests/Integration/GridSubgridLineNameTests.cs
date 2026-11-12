using System.Threading.Tasks;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// A subgrid inherits the names of the parent's lines it spans, in addition to the names its own <c>subgrid [name]</c> list adds
    /// (CSS Grid Level 2 section 9.2), so its items can be placed by the parent's line names.
    /// </summary>
    public class GridSubgridLineNameTests
    {
        // Parent lines: [a] 100pt [b] 100pt [c] 100pt [d]; the subgrid spans columns 2 and 3 (lines b, c, d).
        private static string Parent(string subgridTemplate, string items, string parentExtra = "") =>
            Wrap("<style>body{margin:0}</style>"
                 + "<div id='p' style='display:grid;width:300pt;grid-template-columns:[a] 100pt [b] 100pt [c] 100pt [d];" + parentExtra + "'>"
                 + $"<div id='sub' style='grid-column:2 / 4;display:grid;grid-template-columns:{subgridTemplate}'>{items}</div></div>");

        private static double ColumnOf(PeachBrackets.Html.Dom.CssBox root, string id) =>
            FindById(root, id)!.Location.X - FindById(root, "p")!.Location.X;

        [Fact]
        public async Task AnItemIsPlacedByTheParentsLineName()
        {
            // c is the parent's third line, the subgrid's second: the item sits in the subgrid's second column, parent column 3.
            var (root, _) = await LayoutAsync(Parent("subgrid", "<div id='x' style='grid-column:c / d;height:10pt'></div>"));

            Assert.Equal(200, ColumnOf(root, "x"), 1);
        }

        [Fact]
        public async Task TheSubgridsOwnNamesAreAddedToTheInheritedOnes()
        {
            var (root, _) = await LayoutAsync(Parent("subgrid [own] [mid] [end]",
                "<div id='x' style='grid-column:mid / end;height:10pt'></div><div id='y' style='grid-column:c / d;height:10pt'></div>"));

            // `mid` is the subgrid's second line and `c` (inherited) is the same line.
            Assert.Equal(ColumnOf(root, "x"), ColumnOf(root, "y"), 1);
            Assert.Equal(200, ColumnOf(root, "x"), 1);
        }

        [Fact]
        public async Task ANameOutsideTheSpannedLinesIsNotInherited()
        {
            // `a` is the parent's first line, which the subgrid does not span: the item falls back to auto placement in the first column.
            var (root, _) = await LayoutAsync(Parent("subgrid", "<div id='x' style='grid-column:a / b;height:10pt'></div>"));

            Assert.Equal(100, ColumnOf(root, "x"), 1);
        }

        [Fact]
        public async Task AreaNamesOfTheParentAreInherited()
        {
            var (root, _) = await LayoutAsync(Wrap("<style>body{margin:0}</style>"
                + "<div id='p' style='display:grid;width:300pt;grid-template-columns:100pt 100pt 100pt;grid-template-areas:\"l m r\"'>"
                + "<div id='sub' style='grid-column:2 / 4;display:grid;grid-template-columns:subgrid'>"
                + "<div id='x' style='grid-column:r;height:10pt'></div></div></div>"));

            Assert.Equal(200, ColumnOf(root, "x"), 1);
        }

        [Fact]
        public async Task NamesAreInheritedThroughANestedSubgrid()
        {
            // The inner subgrid spans both of the outer one's columns; auto placement would put the item in the first, the name puts it in the second.
            var (root, _) = await LayoutAsync(Parent("subgrid",
                "<div id='in' style='grid-column:1 / 3;display:grid;grid-template-columns:subgrid'>"
                + "<div id='x' style='grid-column:c / d;height:10pt'></div></div>"));

            Assert.Equal(200, ColumnOf(root, "x"), 1);
        }

        [Fact]
        public async Task RowNamesAreInheritedToo()
        {
            var (root, _) = await LayoutAsync(Wrap("<style>body{margin:0}</style>"
                + "<div id='p' style='display:grid;width:100pt;grid-template-rows:[t] 30pt [m] 40pt [b];'>"
                + "<div id='sub' style='grid-row:1 / 3;display:grid;grid-template-rows:subgrid'>"
                + "<div id='x' style='grid-row:m / b;height:10pt'></div></div></div>"));

            Assert.Equal(30, FindById(root, "x")!.Location.Y - FindById(root, "p")!.Location.Y, 1);
        }
    }
}
