using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// An absolutely positioned child of a flex container with auto insets sits where it would as the only item of the container (css-flexbox-1 section 4.1):
    /// justified along the main axis and aligned across it.
    /// </summary>
    public class AbsoluteStaticPositionInFlexContainerTests
    {
        private static async Task<(CssBox Flex, CssBox Child)> LayoutAsync(string flexStyle, string childStyle = "")
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='f' style='display: flex; position: relative; width: 100pt; height: 60pt; {flexStyle}'><div id='c' style='position: absolute; width: 20pt; height: 10pt; {childStyle}'></div></div>"));
            return (LayoutHarness.FindById(root, "f")!, LayoutHarness.FindById(root, "c")!);
        }

        [Fact]
        public async Task TheDefaultsPutTheBoxAtTheStartCorner()
        {
            var (flex, child) = await LayoutAsync("");

            Assert.Equal(flex.ClientLeft, child.Location.X, 3);
            Assert.Equal(flex.ClientTop, child.Location.Y, 3);
        }

        [Fact]
        public async Task JustifyContentCenterAndAlignItemsCenter_CentreItInARow()
        {
            var (flex, child) = await LayoutAsync("justify-content: center; align-items: center");

            Assert.Equal(flex.ClientLeft + 40, child.Location.X, 3);
            Assert.Equal(flex.ClientTop + 25, child.Location.Y, 3);
        }

        [Fact]
        public async Task InAColumnJustifyContentRunsDownAndAlignItemsAcross()
        {
            var (flex, child) = await LayoutAsync("flex-direction: column; justify-content: flex-end; align-items: center");

            Assert.Equal(flex.ClientLeft + 40, child.Location.X, 3);
            Assert.Equal(flex.ClientTop + 50, child.Location.Y, 3);
        }

        [Fact]
        public async Task ARowReverseStartsAtTheRight_AndAnAlignSelfOnTheChildOutranksAlignItems()
        {
            var (flex, child) = await LayoutAsync("flex-direction: row-reverse; align-items: flex-end", "align-self: flex-start");

            Assert.Equal(flex.ClientLeft + 80, child.Location.X, 3);
            Assert.Equal(flex.ClientTop, child.Location.Y, 3);
        }

        [Fact]
        public async Task AnAxisWithAnInsetIsLeftToThatInset()
        {
            var (flex, child) = await LayoutAsync("justify-content: center; align-items: center", "left: 5pt");

            Assert.Equal(flex.ClientLeft + 5, child.Location.X, 3);
            Assert.Equal(flex.ClientTop + 25, child.Location.Y, 3);
        }
    }
}
