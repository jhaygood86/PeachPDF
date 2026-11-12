using PeachBrackets.Html.Dom;
using System.Threading.Tasks;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// <c>direction: rtl</c> mirrors a row's main axis and a column's cross axis (CSS Flexbox 1 section 5.1), measured against headless Edge.
    /// </summary>
    public class FlexDirectionRtlTests
    {
        private static string Row(string containerStyle, string items = "") =>
            Wrap("<style>body{margin:0}</style>"
                 + $"<div id='c' style='display:flex;width:300pt;{containerStyle}'>"
                 + "<div id='a' style='width:100pt;height:10pt'></div><div id='b' style='width:50pt;height:10pt'></div>" + items + "</div>");

        [Fact]
        public async Task ARtlRowStartsAtTheRightEdge()
        {
            var (root, _) = await LayoutAsync(Row("direction:rtl"));
            var c = FindById(root, "c")!;
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;

            Assert.Equal(c.ActualRight, a.ActualRight, 1);
            Assert.Equal(a.Location.X, b.ActualRight, 1);
        }

        [Fact]
        public async Task ALtrRowIsUnchanged()
        {
            var (root, _) = await LayoutAsync(Row("direction:ltr"));
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;

            Assert.Equal(FindById(root, "c")!.Location.X, a.Location.X, 1);
            Assert.Equal(a.ActualRight, b.Location.X, 1);
        }

        [Fact]
        public async Task ARtlRowReverseStartsAtTheLeftEdge()
        {
            var (root, _) = await LayoutAsync(Row("direction:rtl;flex-direction:row-reverse"));
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;

            Assert.Equal(FindById(root, "c")!.Location.X, a.Location.X, 1);
            Assert.Equal(a.ActualRight, b.Location.X, 1);
        }

        [Fact]
        public async Task FlexEndInARtlRowPacksTheItemsAgainstTheLeftEdge()
        {
            var (root, _) = await LayoutAsync(Row("direction:rtl;justify-content:flex-end"));
            var c = FindById(root, "c")!;
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;

            Assert.Equal(c.Location.X, b.Location.X, 1);
            Assert.Equal(b.ActualRight, a.Location.X, 1);
        }

        [Fact]
        public async Task PhysicalLeftInARtlRowStaysOnTheLeft()
        {
            var (root, _) = await LayoutAsync(Row("direction:rtl;justify-content:left"));
            var c = FindById(root, "c")!;
            var b = FindById(root, "b")!;

            Assert.Equal(c.Location.X, b.Location.X, 1);
        }

        [Fact]
        public async Task ARtlColumnAlignsItsItemsFromTheRight()
        {
            var (root, _) = await LayoutAsync(Wrap("<style>body{margin:0}</style>"
                + "<div id='c' style='display:flex;flex-direction:column;align-items:flex-start;direction:rtl;width:300pt'>"
                + "<div id='a' style='width:100pt;height:10pt'></div></div>"));

            Assert.Equal(FindById(root, "c")!.ActualRight, FindById(root, "a")!.ActualRight, 1);
        }

        [Fact]
        public async Task TheItemsKeepTheirSourceOrderFromTheStartSide()
        {
            var (root, _) = await LayoutAsync(Row("direction:rtl", "<div id='d' style='width:20pt;height:10pt'></div>"));
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            var d = FindById(root, "d")!;

            Assert.True(a.Location.X > b.Location.X && b.Location.X > d.Location.X, "a is rightmost, then b, then d");
        }
    }
}
