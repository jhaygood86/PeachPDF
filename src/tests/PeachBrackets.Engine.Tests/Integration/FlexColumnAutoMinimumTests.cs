using System.Linq;
using PeachBrackets.Html.Dom;
using System.Threading.Tasks;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// <c>min-height: auto</c> on an item of a column flex container (CSS Flexbox 1 section 4.5): an item of an overflowing
    /// line is not shrunk below its content height, or below its definite <c>height</c> when that is smaller.
    /// </summary>
    public class FlexColumnAutoMinimumTests
    {
        private const string ThreeLines = "a<br>b<br>c";

        private static string Container(string itemStyle, int items = 3) =>
            Wrap("<style>.i{line-height:20pt;margin:0}</style><div style='display:flex;flex-direction:column;height:100pt;width:200pt'>"
                 + string.Concat(Enumerable.Range(0, items).Select(n => $"<div id='i{n}' class='i' style='{itemStyle}'>{ThreeLines}</div>"))
                 + "</div>");

        [Fact]
        public async Task ItemsKeepTheirContentHeightWhenTheColumnOverflows()
        {
            var (root, _) = await LayoutAsync(Container(""));

            for (var n = 0; n < 3; n++)
            {
                Assert.Equal(60, FindById(root, $"i{n}")!.ActualBoxSizingHeight, 1);
            }

            Assert.True(FindById(root, "i1")!.Location.Y >= FindById(root, "i0")!.ActualBottom - 0.5, "the items do not overlap");
            Assert.True(FindById(root, "i2")!.Location.Y >= FindById(root, "i1")!.ActualBottom - 0.5);
        }

        [Fact]
        public async Task AnAuthoredZeroMinHeightShrinksAsBefore()
        {
            var (root, _) = await LayoutAsync(Container("min-height:0"));
            Assert.Equal(100.0 / 3, FindById(root, "i0")!.ActualBoxSizingHeight, 1);
        }

        [Fact]
        public async Task ANonShrinkingItemKeepsItsContentHeight()
        {
            var (root, _) = await LayoutAsync(Container("flex-shrink:0"));
            Assert.Equal(60, FindById(root, "i0")!.ActualBoxSizingHeight, 1);
        }

        [Fact]
        public async Task AFittingColumnIsUnchanged()
        {
            var (root, _) = await LayoutAsync(Container("", items: 1));

            Assert.Equal(60, FindById(root, "i0")!.ActualBoxSizingHeight, 1);
        }

        [Fact]
        public async Task ADefiniteHeightSmallerThanTheContentIsTheFloor()
        {
            var (root, _) = await LayoutAsync(Container("height:50pt"));

            // 3 x 50pt in 100pt would shrink to 33.3pt each; the specified size (50pt) is below the content (60pt), so it is
            // the floor and the items stay at 50pt.
            for (var n = 0; n < 3; n++)
            {
                Assert.Equal(50, FindById(root, $"i{n}")!.ActualBoxSizingHeight, 1);
            }
        }

        [Fact]
        public async Task AFlexBasisItemIsFlooredAtItsContentHeightAndLaidOutAtThatSize()
        {
            var (root, _) = await LayoutAsync(Container("flex-basis:90pt"));

            var first = FindById(root, "i0")!;
            Assert.Equal(60, first.ActualBoxSizingHeight, 1);
            Assert.True(first.LineBoxes.Count == 3, "the content is laid out, not collapsed");
        }
    }
}
