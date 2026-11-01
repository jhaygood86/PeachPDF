using PeachBrackets.Engine.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// CSS Box 3 (padding): a percentage <c>padding-*</c> refers to the width of the containing block, on all four sides,
    /// and so does the <c>%</c> term of a <c>calc()</c>. It used to resolve against the box's own width, which is 0 while
    /// the box is being sized.
    /// </summary>
    public class PercentagePaddingLayoutTests
    {
        private static async Task<PeachBrackets.Html.Dom.CssBox> BoxAsync(string body)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(body));
            var box = LayoutHarness.FindById(root, "t");
            Assert.NotNull(box);
            return box!;
        }

        [Fact]
        public async Task PaddingLeft_Percentage_IsAShareOfTheContainingBlocksWidth()
        {
            var box = await BoxAsync("<div style='width:400pt'><div id='t' style='width:100pt;height:20pt;padding-left:30%'></div></div>");

            Assert.Equal(120, box.ActualPaddingLeft, 3);
            Assert.Equal(220, box.ActualBoxSizingWidth, 3);
        }

        [Fact]
        public async Task PaddingLeft_Percentage_WithAutoWidth_TakesItOutOfTheContent()
        {
            var box = await BoxAsync("<div style='width:400pt'><div id='t' style='height:20pt;padding-left:30%'></div></div>");

            Assert.Equal(120, box.ActualPaddingLeft, 3);
            // The padding comes out of the auto width: the border box still fills the containing block.
            Assert.Equal(280, box.Size.Width, 3);
            Assert.Equal(400, box.ActualBoxSizingWidth, 3);
        }

        [Fact]
        public async Task PaddingShorthand_Percentage_AppliesToAllFourSidesAgainstTheWidth()
        {
            var box = await BoxAsync("<div style='width:400pt;height:900pt'><div id='t' style='padding:10%'></div></div>");

            Assert.Equal(40, box.ActualPaddingLeft, 3);
            Assert.Equal(40, box.ActualPaddingRight, 3);
            Assert.Equal(40, box.ActualPaddingTop, 3);
            Assert.Equal(40, box.ActualPaddingBottom, 3);
        }

        [Fact]
        public async Task PaddingLeft_CalcWithPercentage_ResolvesTheTermAgainstTheContainingBlock()
        {
            var box = await BoxAsync("<div style='width:400pt'><div id='t' style='height:20pt;padding-left:calc(10% + 5pt)'></div></div>");

            Assert.Equal(45, box.ActualPaddingLeft, 3);
        }

        [Fact]
        public async Task PaddingTop_Percentage_WithZeroHeight_MakesAnAspectRatioBox()
        {
            var box = await BoxAsync("<div style='width:400pt'><div id='t' style='height:0;padding-top:50%'></div></div>");

            Assert.Equal(200, box.ActualPaddingTop, 3);
            Assert.Equal(200, box.ActualBoxSizingHeight, 3);
        }

        [Fact]
        public async Task Padding_Percentage_OnAnInlineBlock_ResolvesAgainstTheContainingBlock()
        {
            var box = await BoxAsync("<div style='width:400pt'><span id='t' style='display:inline-block;padding:10%'>x</span></div>");

            Assert.Equal(40, box.ActualPaddingLeft, 3);
            Assert.Equal(40, box.ActualPaddingTop, 3);
        }

        [Fact]
        public async Task Padding_Percentage_OnAFlexItem_ResolvesAgainstTheFlexContainersWidth()
        {
            var box = await BoxAsync("<div style='display:flex;width:400pt'><div id='t' style='padding:10%'>x</div></div>");

            Assert.Equal(40, box.ActualPaddingLeft, 3);
            Assert.Equal(40, box.ActualPaddingTop, 3);
        }

        [Fact]
        public async Task Padding_Percentage_FollowsTheContainingBlockWhenItsWidthIsAuto()
        {
            var box = await BoxAsync("<div style='width:500pt;padding:0 50pt'><div id='t' style='height:20pt;padding-left:10%'></div></div>");

            // The containing block's content box is 500pt wide, not the page.
            Assert.Equal(50, box.ActualPaddingLeft, 3);
        }
    }
}
