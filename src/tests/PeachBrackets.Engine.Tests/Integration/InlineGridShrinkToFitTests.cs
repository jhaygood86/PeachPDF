using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// CSS Grid 1 §3 with css-sizing-3 §5.1: an <c>inline-grid</c>'s auto inline size is fit-content,
    /// <c>min(max-content, max(min-content, available))</c>, like an inline-block's - not the stretch width.
    /// </summary>
    public class InlineGridShrinkToFitTests
    {
        private static async Task<(double Width, double GridY, double AfterY, double ContainerWidth)> MeasureAsync(string gridStyle, string content)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='p' style='width:300pt; font-size:12pt'>" +
                $"<span id='g' style='display:inline-grid;{gridStyle}'>{content}</span><span id='a'> after</span></div>"));

            var grid = LayoutHarness.FindById(root, "g")!;
            var after = LayoutHarness.FindById(root, "a")!;
            var container = LayoutHarness.FindById(root, "p")!;
            return (grid.ActualRight - grid.Location.X, grid.Location.Y, after.Rectangles.Values.Count > 0 ? after.Rectangles.Values.ToArray()[0].Y : after.Location.Y,
                container.ActualRight - container.Location.X);
        }

        [Fact]
        public async Task AutoWidth_TextOnly_ShrinksToContentAndLeavesTextOnTheLine()
        {
            var (width, gridY, afterY, container) = await MeasureAsync("", "Badge");

            Assert.InRange(width, 10, 60);
            Assert.True(width < container / 2, $"inline-grid filled the line: {width} of {container}");
            Assert.Equal(gridY, afterY, 3.0);
        }

        [Fact]
        public async Task AutoWidth_TwoColumns_IsTheSumOfTracksPlusGapAndDecoration()
        {
            var (plain, _, _, _) = await MeasureAsync("grid-template-columns:auto auto", "<i>One</i><i>Two</i>");
            var (gapped, _, _, _) = await MeasureAsync("grid-template-columns:auto auto;column-gap:10pt;padding:0 5pt;border:2pt solid", "<i>One</i><i>Two</i>");

            Assert.Equal(plain + 10 + 10 + 4, gapped, 1.0);
        }

        [Fact]
        public async Task AutoWidth_LongContent_IsClampedToTheAvailableWidth()
        {
            var (width, _, _, container) = await MeasureAsync("", string.Join(' ', System.Linq.Enumerable.Repeat("word", 200)));

            Assert.Equal(container, width, 1.0);
        }

        [Fact]
        public async Task AutoWidth_MaxWidth_StillClamps()
        {
            var (width, _, _, _) = await MeasureAsync("max-width:60pt", "Clamp me please");

            Assert.Equal(60, width, 1.0);
        }

        [Fact]
        public async Task ExplicitWidth_IsUnchanged()
        {
            var (width, _, _, _) = await MeasureAsync("width:120pt", "Badge");

            Assert.Equal(120, width, 1.0);
        }
    }
}
