using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <c>margin: 0 auto</c> centres a box within its containing block's <b>content</b> width, per
    /// <see href="https://www.w3.org/TR/CSS21/visudet.html#blockwidth">CSS 2.1 §10.3.3</see>, which states the
    /// constraint over the block <see href="https://www.w3.org/TR/CSS21/visudet.html#containing-block-details">§10.1</see>
    /// puts at the content edge of the nearest block container ancestor.
    /// </summary>
    /// <remarks>
    /// <c>Size.Width</c> is the containing block's content width under <c>content-box</c> but its border
    /// box under <c>border-box</c>, so centring against it pushed the box toward the end edge by half the
    /// container's padding and border. Each case is asserted for a plain <c>&lt;div&gt;</c> as well as an
    /// <c>&lt;hr&gt;</c> - the defect is not specific to the rule, and "the two agree" is what makes the
    /// pairing valid.
    /// </remarks>
    public class BlockAutoMarginBasisTests
    {
        private const string ChildDeclaration = "margin: 0 auto; width: 50pt; border: 1px solid; height: auto";

        private static async Task<(double RuleLeft, double DivLeft, double ContainerLeft)> LeftEdgesAsync(
            string containerDeclaration, string childDeclaration = ChildDeclaration)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                $"<!DOCTYPE html><html><body><div id='container' style='{containerDeclaration}'>"
                + $"<hr id='rule' style='{childDeclaration}'>"
                + $"<div id='equivalent' style='{childDeclaration}'></div>"
                + "</div></body></html>");

            return (LayoutHarness.FindById(root, "rule")!.Location.X,
                    LayoutHarness.FindById(root, "equivalent")!.Location.X,
                    LayoutHarness.FindById(root, "container")!.Location.X);
        }

        [Theory]
        // Content box is 150pt (200 - 40 padding - 10 border); a 50pt + 2 * 0.75pt-border box centres at
        // 25 + (150 - 51.5) / 2 = 74.25 from the container's border edge. Splitting the 200pt border box
        // instead gave 99.25, off by exactly half the container's padding and border.
        [InlineData("box-sizing: border-box; width: 200pt; padding: 0 20pt; border: 5pt solid", 74.25)]
        // content-box: Size.Width already is the content width, so the two expressions coincide.
        [InlineData("box-sizing: content-box; width: 150pt; padding: 0 20pt; border: 5pt solid", 74.25)]
        // No padding or border at all: nothing to be wrong about, and the content edge is the border edge.
        [InlineData("width: 200pt", 74.25)]
        public async Task AutoMargins_CentreAgainstTheContainingBlocksContentWidth(
            string containerDeclaration, double expectedFromContainerBorderEdge)
        {
            var (rule, equivalent, container) = await LeftEdgesAsync(containerDeclaration);

            Assert.Equal(expectedFromContainerBorderEdge, rule - container, 3);
            Assert.Equal(expectedFromContainerBorderEdge, equivalent - container, 3);
        }

        [Theory]
        // A percentage margin resolves against the same content width (CSS 2.1 §8.3): 10% of the 150pt
        // content box is 15pt, not 10% of the 200pt border box.
        [InlineData("box-sizing: border-box; width: 200pt; padding: 0 20pt; border: 5pt solid", 25 + 15)]
        [InlineData("box-sizing: content-box; width: 150pt; padding: 0 20pt; border: 5pt solid", 25 + 15)]
        public async Task APercentageMargin_ResolvesAgainstTheContainingBlocksContentWidth(
            string containerDeclaration, double expectedFromContainerBorderEdge)
        {
            var (_, equivalent, container) = await LeftEdgesAsync(containerDeclaration, "margin-left: 10%; height: auto");

            Assert.Equal(expectedFromContainerBorderEdge, equivalent - container, 3);
        }

        [Fact]
        public async Task AutoMargins_WithAWidthThatLeavesNoFreeSpace_DoNotMoveTheBox()
        {
            // 150pt content box, 148.5pt + 1.5pt of border = exactly the content width.
            var (rule, equivalent, container) = await LeftEdgesAsync(
                "box-sizing: border-box; width: 200pt; padding: 0 20pt; border: 5pt solid",
                "margin: 0 auto; width: 148.5pt; border: 1px solid; height: auto");

            Assert.Equal(25, rule - container, 3);
            Assert.Equal(25, equivalent - container, 3);
        }
    }
}
