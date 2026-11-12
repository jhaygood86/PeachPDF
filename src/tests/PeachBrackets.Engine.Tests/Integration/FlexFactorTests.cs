using PeachBrackets.Html.Dom;
using System.Threading.Tasks;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// The flex shorthand's CSS-wide keywords (CSS Flexbox 1 section 7.1) and the sum-of-factors-below-one rule of
    /// section 9.7 step 4c, measured against headless Edge.
    /// </summary>
    public class FlexFactorTests
    {
        private static double Width(CssBox box) => box.ActualRight - box.Location.X;

        [Fact]
        public async Task FlexInitial_IsZeroOneAuto_SoTheWidthIsHonored()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='display:flex;width:300pt'>"
                + "<div id='a' style='flex:initial;width:100pt;height:10pt'></div>"
                + "<div id='b' style='flex:1;height:10pt'></div></div>"));

            Assert.Equal(100, Width(FindById(root, "a")!), 1);
            Assert.Equal(200, Width(FindById(root, "b")!), 1);
        }

        [Fact]
        public async Task FlexInitial_MatchesWrittenOutZeroOneAuto()
        {
            var (initial, _) = await LayoutAsync(Wrap(
                "<div style='display:flex;width:300pt'><div id='a' style='flex:initial;width:100pt;height:10pt'></div></div>"));
            var (written, _) = await LayoutAsync(Wrap(
                "<div style='display:flex;width:300pt'><div id='a' style='flex:0 1 auto;width:100pt;height:10pt'></div></div>"));

            Assert.Equal(Width(FindById(written, "a")!), Width(FindById(initial, "a")!), 3);
        }

        [Fact]
        public async Task GrowFactorsBelowOne_ShareThatFractionOfTheFreeSpace()
        {
            // 100pt of content in a 400pt row leaves 300pt; 0.3 of it is 90pt, so the item is 190pt (Edge).
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='display:flex;width:400pt'><div id='a' style='flex-grow:.3'>"
                + "<div style='width:100pt;height:10pt'></div></div></div>"));

            Assert.Equal(190, Width(FindById(root, "a")!), 1);
        }

        [Fact]
        public async Task GrowFactorsSummingToOneOrMore_StillFillTheLine()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='display:flex;width:400pt'>"
                + "<div id='a' style='flex-grow:.5'><div style='width:100pt;height:10pt'></div></div>"
                + "<div id='b' style='flex-grow:.5'><div style='width:100pt;height:10pt'></div></div></div>"));

            Assert.Equal(200, Width(FindById(root, "a")!), 1);
            Assert.Equal(200, Width(FindById(root, "b")!), 1);
        }

        [Fact]
        public async Task ShrinkFactorsBelowOne_GiveUpThatFractionOfTheOverflow()
        {
            // Two 300pt items in a 400pt row overflow by 200pt; flex-shrink .5 on each sums to 1, so the whole overflow is removed.
            // With .25 each the sum is .5 and only half of it (100pt) is removed: 250pt each.
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='display:flex;width:400pt'>"
                + "<div id='a' style='flex:0 .25 300pt;min-width:0;height:10pt'></div>"
                + "<div id='b' style='flex:0 .25 300pt;min-width:0;height:10pt'></div></div>"));

            Assert.Equal(250, Width(FindById(root, "a")!), 1);
            Assert.Equal(250, Width(FindById(root, "b")!), 1);
        }
    }
}
