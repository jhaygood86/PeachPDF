using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Under <c>box-sizing: border-box</c> a box's used border-box width floors at its own padding and
    /// border: <see href="https://www.w3.org/TR/css-sizing-3/#box-sizing">css-sizing-3 §3</see> derives the
    /// content box by subtracting them from the specified size, and a content width cannot be negative
    /// (<see href="https://www.w3.org/TR/CSS21/visudet.html#the-width-property">CSS 2.1 §10.2</see>), so the
    /// border box cannot be squeezed below them.
    /// </summary>
    /// <remarks>
    /// The auto branch of <c>GetBoxWidth</c> subtracts <c>ActualBoxSizeIncludedWidth</c>, which is zero
    /// under <c>border-box</c> (<c>Size.Width</c> already is the border box), so a containing block with no
    /// content width to give resolved the box to 0 (and, under <c>content-box</c>, below it). It is not
    /// specific to <c>&lt;hr&gt;</c>, and is asserted here on the <c>&lt;div&gt;</c> alone: the rule resolves
    /// its own width rather than going through <c>GetBoxWidth</c>, so it neither has the defect nor the fix.
    /// </remarks>
    public class BorderBoxWidthFloorTests
    {
        private static async Task<double> BorderBoxWidthAsync(string containerDeclaration, string declaration)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                $"<!DOCTYPE html><html><body><div style='{containerDeclaration}'>"
                + $"<div id='box' style='margin: 0; height: auto; {declaration}'></div>"
                + "</div></body></html>");

            var box = LayoutHarness.FindById(root, "box")!;

            return box.ActualRight - box.Location.X;
        }

        [Theory]
        // 20pt of padding, and 6pt of border from 4px a side (1px = 0.75pt).
        [InlineData("width: 0", "box-sizing: border-box; padding: 0 10pt; border: 4px solid", 26)]
        [InlineData("width: 0", "box-sizing: border-box; padding: 0 10pt; border: 0 none", 20)]
        // Any container too narrow for the box's own edges reaches the same floor, not only a zero one.
        [InlineData("width: 5pt", "box-sizing: border-box; padding: 0 10pt; border: 4px solid", 26)]
        public async Task AnAutoWidthBorderBox_FloorsAtItsOwnPaddingAndBorder(
            string container, string declaration, double expected)
        {
            Assert.Equal(expected, await BorderBoxWidthAsync(container, declaration), 3);
        }

        [Theory]
        // The explicit-width branch has the same floor: a declared width smaller than the box's own
        // edges cannot squeeze them out either.
        [InlineData("width: 200pt", "box-sizing: border-box; width: 5pt; padding: 0 10pt; border: 4px solid", 26)]
        [InlineData("width: 200pt", "box-sizing: border-box; width: 0; padding: 0 10pt", 20)]
        public async Task ADeclaredBorderBoxWidthBelowItsOwnEdges_FloorsAtThem(
            string container, string declaration, double expected)
        {
            Assert.Equal(expected, await BorderBoxWidthAsync(container, declaration), 3);
        }

        [Theory]
        // content-box already holds the edges outside Size.Width and is unaffected: the contrast case.
        [InlineData("width: 0", "box-sizing: content-box; padding: 0 10pt; border: 4px solid", 26)]
        // A border box wider than its own edges is untouched by the floor.
        [InlineData("width: 200pt", "box-sizing: border-box; padding: 0 10pt; border: 4px solid", 200)]
        public async Task TheFloor_LeavesOrdinaryWidthsAlone(string container, string declaration, double expected)
        {
            Assert.Equal(expected, await BorderBoxWidthAsync(container, declaration), 3);
        }
    }
}
