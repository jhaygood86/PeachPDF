using System.Linq;
using PeachBrackets.Html.Dom;
using System.Threading.Tasks;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// The automatic minimum size also floors an item a line grows (CSS Flexbox 1 sections 4.5 and 9.7): <c>flex: 1</c> items
    /// with a zero basis no longer split the line equally when one holds content wider (or taller) than its share.
    /// </summary>
    public class FlexGrowAutoMinimumTests
    {
        private static string Row(string firstStyle = "") =>
            Wrap("<div style='display:flex;width:240pt;font-size:20pt'>" +
                 $"<div id='a' style='flex:1;{firstStyle}'>wwwwwwwwwwwww</div><div id='b' style='flex:1'>x</div><div id='c' style='flex:1'>y</div></div>");

        [Fact]
        public async Task ARowItemWithAWideWordKeepsItsWidthAndTheOthersShareTheRest()
        {
            var (root, _) = await LayoutAsync(Row());
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;
            var c = FindById(root, "c")!;

            Assert.True(a.ActualBoxSizingWidth > 80, $"a keeps its word's width, was {a.ActualBoxSizingWidth}");
            Assert.Equal(b.ActualBoxSizingWidth, c.ActualBoxSizingWidth, 0.5);
            Assert.Equal(240, a.ActualBoxSizingWidth + b.ActualBoxSizingWidth + c.ActualBoxSizingWidth, 0.5);
        }

        [Fact]
        public async Task AZeroMinWidthOrAScrollContainerLetsTheRowSplitEqually()
        {
            foreach (var style in new[] { "min-width:0", "overflow:hidden" })
            {
                var (root, _) = await LayoutAsync(Row(style));
                Assert.Equal(80, FindById(root, "a")!.ActualBoxSizingWidth, 0.5);
            }
        }

        [Fact]
        public async Task ARowWhoseItemsFitTheirSharesIsUnchanged()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='display:flex;width:240pt;font-size:10pt'><div id='a' style='flex:1'>one</div><div id='b' style='flex:2'>two</div></div>"));

            Assert.Equal(80, FindById(root, "a")!.ActualBoxSizingWidth, 0.5);
            Assert.Equal(160, FindById(root, "b")!.ActualBoxSizingWidth, 0.5);
        }

        [Fact]
        public async Task ColumnItemsWithAZeroBasisKeepTheirContentHeight()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<style>.i{line-height:20pt;margin:0;flex:1}</style><div style='display:flex;flex-direction:column;height:100pt;width:200pt'>"
                + string.Concat(Enumerable.Range(0, 3).Select(n => $"<div id='i{n}' class='i'>a<br>b<br>c</div>")) + "</div>"));

            for (var n = 0; n < 3; n++)
            {
                Assert.Equal(60, FindById(root, $"i{n}")!.ActualBoxSizingHeight, 1);
            }
        }

        [Fact]
        public async Task ColumnItemsThatFitTheirSharesStillSplitTheHeightEqually()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<style>.i{line-height:10pt;margin:0;flex:1}</style><div style='display:flex;flex-direction:column;height:120pt;width:200pt'>"
                + string.Concat(Enumerable.Range(0, 3).Select(n => $"<div id='i{n}' class='i'>a</div>")) + "</div>"));

            for (var n = 0; n < 3; n++)
            {
                Assert.Equal(40, FindById(root, $"i{n}")!.ActualBoxSizingHeight, 1);
            }
        }
    }
}
