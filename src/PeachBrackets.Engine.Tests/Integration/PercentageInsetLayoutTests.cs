using PeachBrackets.Engine.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// CSS Positioned Layout 3 (insets): a percentage <c>top</c>/<c>bottom</c> refers to the height of the containing block
    /// and <c>left</c>/<c>right</c> to its width. On a relatively positioned box whose containing block has no definite
    /// height, a top/bottom percentage computes to <c>auto</c> (what Blink, WebKit and Gecko do).
    /// </summary>
    public class PercentageInsetLayoutTests
    {
        private const string Block = "position:relative;width:400pt;height:200pt;margin:0;padding:0;border:0";

        private static async Task<(PeachBrackets.Html.Dom.CssBox Container, PeachBrackets.Html.Dom.CssBox Box)> LayOutAsync(string containerStyle, string boxStyle)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='c' style='{containerStyle}'><div id='t' style='margin:0;width:10pt;height:10pt;{boxStyle}'></div></div>"));

            var container = LayoutHarness.FindById(root, "c")!;
            var box = LayoutHarness.FindById(root, "t")!;
            return (container, box);
        }

        [Theory]
        [InlineData("position:absolute;top:10%", 20)]
        [InlineData("position:absolute;top:calc(10% + 5pt)", 25)]
        [InlineData("position:absolute;bottom:10%", 170)]
        [InlineData("position:absolute;bottom:calc(10% + 5pt)", 165)]
        [InlineData("position:relative;top:10%", 20)]
        [InlineData("position:relative;bottom:10%", -20)]
        public async Task TopAndBottomPercentages_ResolveAgainstTheContainingBlocksHeight(string boxStyle, double expectedY)
        {
            var (container, box) = await LayOutAsync(Block, boxStyle);

            Assert.Equal(expectedY, box.Location.Y - container.Location.Y, 3);
        }

        [Fact]
        public async Task AbsoluteTopAndBottomPercentages_BothSet_PlaceTheBoxAtTop()
        {
            var (container, box) = await LayOutAsync(Block, "position:absolute;top:10%;bottom:20%;height:auto");

            Assert.Equal(20, box.Location.Y - container.Location.Y, 3);
            Assert.Equal(140, box.ActualBoxSizingHeight, 3);
        }

        [Fact]
        public async Task AbsoluteLeftPercentage_StaysAShareOfTheContainingBlocksWidth()
        {
            var (container, box) = await LayOutAsync(Block, "position:absolute;left:10%");

            Assert.Equal(40, box.Location.X - container.Location.X, 3);
        }

        [Fact]
        public async Task RelativeLeftPercentage_RefersToTheContainingBlocksWidthNotTheBoxs()
        {
            var (container, box) = await LayOutAsync(Block, "position:relative;left:10%");

            Assert.Equal(40, box.Location.X - container.Location.X, 3);
        }

        [Fact]
        public async Task RelativeTopPercentage_WithAContainingBlockOfAutoHeight_IsAuto()
        {
            var (container, box) = await LayOutAsync("position:relative;width:400pt;margin:0", "position:relative;top:10%");

            Assert.Equal(0, box.Location.Y - container.Location.Y, 3);
        }

        [Fact]
        public async Task RelativeBottomPercentage_WithAnIndefiniteBlock_IsAutoSoTheLengthTopStillApplies()
        {
            var (container, box) = await LayOutAsync("position:relative;width:400pt;margin:0", "position:relative;top:7pt;bottom:10%");

            Assert.Equal(7, box.Location.Y - container.Location.Y, 3);
        }

        [Fact]
        public async Task RelativeTopPercentage_IsAutoAndLetsALengthBottomMoveTheBoxTheOtherWay()
        {
            var (container, box) = await LayOutAsync("position:relative;width:400pt;margin:0", "position:relative;top:10%;bottom:6pt");

            Assert.Equal(-6, box.Location.Y - container.Location.Y, 3);
        }

        [Fact]
        public async Task AbsoluteTopPercentage_UsesThePaddingBoxOfABorderedContainingBlock()
        {
            var (container, box) = await LayOutAsync("position:relative;width:400pt;height:200pt;margin:0;border:10pt solid;box-sizing:border-box", "position:absolute;top:10%");

            // Border-box 200pt, borders 10pt each: the padding box is 180pt tall and starts 10pt in.
            Assert.Equal(10 + 18, box.Location.Y - container.Location.Y, 3);
        }
        [Theory]
        [InlineData("position:absolute;top:10%", 20)]
        [InlineData("position:absolute;top:calc(10% + 5pt)", 25)]
        [InlineData("position:absolute;bottom:10%", 170)]
        public async Task AbsoluteTopAndBottomPercentages_WithAnAutoHeightContainingBlock_UseItsFinalHeight(string boxStyle, double expectedY)
        {
            // The containing block is as tall as its in-flow child (200pt), which is only known once its children are placed.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='c' style='position:relative;width:400pt;margin:0'><div style='height:200pt'></div><div id='t' style='margin:0;width:10pt;height:10pt;{boxStyle}'></div></div>"));

            var container = LayoutHarness.FindById(root, "c")!;
            var box = LayoutHarness.FindById(root, "t")!;

            Assert.Equal(200, container.ActualBoxSizingHeight, 3);
            Assert.Equal(expectedY, box.Location.Y - container.Location.Y, 3);
        }
    }
}
