using PeachBrackets.Html.Dom;
using System.Threading.Tasks;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// A bare <c>fr</c> track is <c>minmax(auto, fr)</c> (CSS Grid section 7.2.3), so a grid item's automatic minimum size
    /// (section 6.6) keeps the track from shrinking below the item's content. <c>minmax(0, 1fr)</c>, a scroll container
    /// and an authored <c>min-width</c> opt out.
    /// </summary>
    public class GridAutoMinimumTests
    {
        private static string Grid(string columns, string firstItemStyle = "") =>
            Wrap("<div id='g' style='display:grid;grid-template-columns:" + columns + ";width:100pt;font-size:20pt'>" +
                 "<div id='a' style='" + firstItemStyle + "'>wwwwwwwwwwwwwww</div><div id='b'>x</div></div>");

        [Fact]
        public async Task AFrTrackDoesNotShrinkBelowItsItemsMinContent()
        {
            var (root, _) = await LayoutAsync(Grid("1fr 1fr"));
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;

            Assert.True(a.ActualBoxSizingWidth > 150, $"the unbreakable word keeps its width, was {a.ActualBoxSizingWidth}");
            Assert.True(b.Location.X >= a.ActualRight - 0.5, "the second track starts after the first");
        }

        [Fact]
        public async Task MinmaxZeroFrLetsTheTrackShrink()
        {
            var (root, _) = await LayoutAsync(Grid("minmax(0,1fr) minmax(0,1fr)"));

            Assert.Equal(50, FindById(root, "a")!.ActualBoxSizingWidth, 1);
        }

        [Fact]
        public async Task AScrollContainerItemDoesNotFloorItsTrack()
        {
            var (root, _) = await LayoutAsync(Grid("1fr 1fr", "overflow:hidden"));

            Assert.Equal(50, FindById(root, "a")!.ActualBoxSizingWidth, 1);
        }

        [Fact]
        public async Task AnAuthoredZeroMinWidthDoesNotFloorItsTrack()
        {
            var (root, _) = await LayoutAsync(Grid("1fr 1fr", "min-width:0"));

            Assert.Equal(50, FindById(root, "a")!.ActualBoxSizingWidth, 1);
        }

        [Fact]
        public async Task TracksThatFitAreUnchanged()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='display:grid;grid-template-columns:1fr 2fr;width:300pt;font-size:10pt'>" +
                "<div id='a'>one</div><div id='b'>two</div></div>"));

            Assert.Equal(100, FindById(root, "a")!.ActualBoxSizingWidth, 1);
            Assert.Equal(200, FindById(root, "b")!.ActualBoxSizingWidth, 1);
        }
    }
}
