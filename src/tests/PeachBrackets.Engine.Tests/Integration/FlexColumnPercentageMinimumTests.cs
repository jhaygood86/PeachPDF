using PeachBrackets.Html.Dom;
using System.Threading.Tasks;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// A percentage <c>height</c> is the specified size that caps a column item's automatic minimum (CSS Flexbox 1 section 4.5) when the
    /// container's height is definite, measured against headless Edge.
    /// </summary>
    public class FlexColumnPercentageMinimumTests
    {
        private static double Height(CssBox box) => box.ActualBoxSizingHeight;

        private static string Column(string containerHeight) =>
            Wrap("<style>body{margin:0}div div{width:20pt}</style>"
                 + $"<div style='display:flex;flex-direction:column;{containerHeight}width:100pt'>"
                 + "<div id='a' style='height:30%;flex:1 1 0;background:#f00'><div style='height:150pt'></div></div>"
                 + "<div id='b' style='height:50%;background:#0f0'></div>"
                 + "<div id='c' style='height:30%;background:#00f'></div></div>");

        [Fact]
        public async Task APercentageHeightOfADefiniteColumnCapsTheMinimum()
        {
            // 240pt column: 30% is 72pt, so the first item may shrink to 72pt and the others give up only what they must (72/105/63 in Edge).
            var (root, _) = await LayoutAsync(Column("height:240pt;"));

            Assert.Equal(72, Height(FindById(root, "a")!), 1);
            Assert.Equal(105, Height(FindById(root, "b")!), 1);
            Assert.Equal(63, Height(FindById(root, "c")!), 1);
        }

        [Fact]
        public async Task ALengthHeightGivesTheSameNumbersAsThePercentage()
        {
            var (root, _) = await LayoutAsync(Wrap("<style>body{margin:0}div div{width:20pt}</style>"
                + "<div style='display:flex;flex-direction:column;height:240pt;width:100pt'>"
                + "<div id='a' style='height:72pt;flex:1 1 0;background:#f00'><div style='height:150pt'></div></div>"
                + "<div id='b' style='height:120pt;background:#0f0'></div>"
                + "<div id='c' style='height:72pt;background:#00f'></div></div>"));

            Assert.Equal(72, Height(FindById(root, "a")!), 1);
        }

        [Fact]
        public async Task APercentageHeightOfAnAutoHeightColumnDoesNotCapTheMinimum()
        {
            // The container's height is auto, so the percentage behaves as auto: the first item keeps its 150pt of content.
            var (root, _) = await LayoutAsync(Column(""));

            Assert.True(Height(FindById(root, "a")!) >= 149.5, $"first item was {Height(FindById(root, "a")!)}pt");
        }
    }
}
