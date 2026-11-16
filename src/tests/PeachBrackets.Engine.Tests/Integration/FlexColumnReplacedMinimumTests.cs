using System.Linq;
using System.Threading.Tasks;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// <c>min-height: auto</c> on a replaced item of a column flex container (CSS Flexbox 1 section 4.5): the floor is the smaller of the
    /// picture's natural height (its content size suggestion) and, when its width is definite, the height its aspect ratio gives that
    /// width (its transferred size suggestion). A width the cross axis stretched is not definite, so it only has the first.
    /// </summary>
    public class FlexColumnReplacedMinimumTests
    {
        // 96 x 48 CSS px = 72 x 36 pt, a 2:1 picture.
        private const string Svg = "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='96' height='48'%3E%3Crect width='96' height='48' fill='red'/%3E%3C/svg%3E";

        private static string Container(string itemStyle, string containerStyle, int items = 3) =>
            Wrap($"<div style='display:flex;flex-direction:column;width:200pt;{containerStyle}'>"
                 + string.Concat(Enumerable.Range(0, items).Select(n => $"<img id='i{n}' src=\"{Svg}\" style='{itemStyle}' />"))
                 + "</div>");

        [Fact]
        public async Task AStretchedPictureStopsShrinkingAtItsNaturalHeight()
        {
            // Three pictures stretched to 200pt wide would be 100pt tall each; the 60pt column shrinks them to 20pt, under the
            // natural 36pt, which is the floor.
            var (root, _) = await LayoutAsync(Container("", "height:60pt"));

            for (var n = 0; n < 3; n++)
            {
                Assert.Equal(36, FindById(root, $"i{n}")!.ActualBoxSizingHeight, 0.5);
                Assert.Equal(200, FindById(root, $"i{n}")!.ActualBoxSizingWidth, 0.5);
            }
        }

        [Fact]
        public async Task AnAuthoredZeroMinHeightLetsThePictureShrinkAsBefore()
        {
            var (root, _) = await LayoutAsync(Container("min-height:0", "height:60pt"));

            Assert.Equal(20, FindById(root, "i0")!.ActualBoxSizingHeight, 0.5);
        }

        [Fact]
        public async Task AFittingColumnLeavesThePicturesAtTheirNaturalHeight()
        {
            var (root, _) = await LayoutAsync(Container("", "height:300pt"));

            Assert.Equal(36, FindById(root, "i0")!.ActualBoxSizingHeight, 0.5);
        }

        [Fact]
        public async Task ADefiniteWidthGivesTheTransferredFloorWhenItIsSmaller()
        {
            // 36pt wide is 18pt tall by the 2:1 ratio, under the natural 36pt; a 30pt column would shrink the three to 10pt.
            var (root, _) = await LayoutAsync(Container("width:36pt", "height:30pt"));

            for (var n = 0; n < 3; n++)
            {
                Assert.Equal(18, FindById(root, $"i{n}")!.ActualBoxSizingHeight, 0.5);
                Assert.Equal(36, FindById(root, $"i{n}")!.ActualBoxSizingWidth, 0.5);
            }
        }

        [Fact]
        public async Task APaddedPictureFloorsAtItsOuterNaturalHeight()
        {
            // 36pt natural height plus 2 x 5pt padding is a 46pt outer size; the 60pt column would shrink three of them to 20pt.
            var (root, _) = await LayoutAsync(Container("padding:5pt", "height:60pt"));

            Assert.Equal(46, FindById(root, "i0")!.ActualBoxSizingHeight, 0.5);
        }
    }
}
