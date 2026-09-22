using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A height that depends on a percentage - a plain <c>50%</c> or a <c>calc()</c>/<c>min()</c> containing one -
    /// behaves as <c>auto</c> against a containing block whose own height is indefinite
    /// (<see href="https://www.w3.org/TR/CSS21/visudet.html#the-height-property">CSS 2.1 §10.5</see>), and resolves
    /// against it when it is definite.
    /// </summary>
    /// <remarks>
    /// The layout engine recognised "this height is a percentage" with <c>EndsWith('%')</c>, so
    /// <c>calc(100% - 5px)</c> - which ends in <c>)</c> - was read as a definite length and resolved against the
    /// container's content-driven height: the box painted at that height while the block after it was placed as if
    /// the height were <c>auto</c>. Every literal was measured in Chrome 153, and every case is asserted for a
    /// plain <c>&lt;div&gt;</c> as well as an <c>&lt;hr&gt;</c> - the defect is not specific to the rule.
    /// The parent always holds a 40pt block first, so its content-driven height is a real, non-zero number that a
    /// wrongly-definite basis would pick up.
    /// </remarks>
    public class PercentageDependentHeightTests
    {
        private static async Task<(double Own, double Parent, double Gap)> MeasureAsync(
            string tag, string parentStyle, string childStyle)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body style='margin: 0'>"
                + $"<div id='p' style='margin: 0; {parentStyle}'><div style='height: 40pt; margin: 0'></div>"
                + $"<{tag} id='b' style='margin: 0; border: 0; {childStyle}'>{(tag == "div" ? "</div>" : string.Empty)}</div>"
                + "<div id='n' style='height: 1pt; margin: 0'></div></body></html>");

            var b = LayoutHarness.FindById(root, "b")!;
            var p = LayoutHarness.FindById(root, "p")!;
            var n = LayoutHarness.FindById(root, "n")!;

            return (b.ActualBottom - b.Location.Y, p.ActualBottom - p.Location.Y, n.Location.Y - b.Location.Y);
        }

        [Theory]
        [InlineData("div", "height: 50%")]
        [InlineData("div", "height: calc(100% - 5px)")]
        [InlineData("div", "height: calc(50% + 0px)")]
        // A percentage under a leading unary sign inside a parenthesized group (UnaryCalcNode).
        [InlineData("div", "height: calc(-(50% - 10px))")]
        // A percentage inside min()/max() (CallCalcNode), on both height and min-height.
        [InlineData("div", "height: min(10px, 50%)")]
        [InlineData("div", "min-height: min(10px, 50%)")]
        [InlineData("div", "min-height: calc(50% + 0px)")]
        [InlineData("div", "min-height: 50%")]
        [InlineData("hr", "height: 50%")]
        [InlineData("hr", "height: calc(100% - 5px)")]
        [InlineData("hr", "height: calc(50% + 0px)")]
        public async Task AnIndefiniteBasis_MakesAPercentageDependentHeightAuto(string tag, string declaration)
        {
            // Chrome: the box is 0 tall, the parent is exactly its 40pt block, and the block after starts at the
            // box's own top - i.e. what the flow reserved is what the box painted.
            var (own, parent, gap) = await MeasureAsync(tag, string.Empty, declaration);

            Assert.Equal(0, own, 3);
            Assert.Equal(40, parent, 3);
            Assert.Equal(0, gap, 3);
        }

        [Theory]
        [InlineData("div", "height: calc(50% + 0px)")]
        [InlineData("div", "height: calc(100% - 5px)")]
        public async Task ThePaintedHeight_AndTheReservedHeight_Agree(string tag, string declaration)
        {
            // The symptom itself, independent of the literal: whatever height the box paints, the next block is
            // placed exactly that far below the box's top.
            var (own, _, gap) = await MeasureAsync(tag, string.Empty, declaration);

            Assert.Equal(own, gap, 3);
        }

        [Theory]
        [InlineData("div", "max-height: calc(50% + 0px); height: 100pt")]
        [InlineData("div", "max-height: 50%; height: 100pt")]
        public async Task AnIndefiniteBasis_IgnoresAPercentageDependentMaxHeight(string tag, string declaration)
        {
            // max-height: none is what a percentage max-height computes to against an indefinite block.
            var (own, _, gap) = await MeasureAsync(tag, string.Empty, declaration);

            Assert.Equal(100, own, 3);
            Assert.Equal(100, gap, 3);
        }

        [Theory]
        [InlineData("div", "height: 50%", 50)]
        [InlineData("div", "height: calc(100% - 5px)", 96.25)]
        [InlineData("div", "height: calc(50% + 10px)", 57.5)]
        [InlineData("div", "min-height: calc(50% + 0px)", 50)]
        [InlineData("div", "max-height: calc(50% + 0px); height: 100pt", 50)]
        [InlineData("hr", "height: calc(100% - 5px)", 96.25)]
        public async Task ADefiniteBasis_ResolvesThePercentageDependentHeightAgainstIt(
            string tag, string declaration, double expected)
        {
            var (own, _, _) = await MeasureAsync(tag, "height: 100pt", declaration);

            Assert.Equal(expected, own, 2);
        }

        [Theory]
        [InlineData("div", "height: 10px")]
        [InlineData("div", "height: calc(4px + 6px)")]
        public async Task ALengthWithNoPercentage_IsUnaffected(string tag, string declaration)
        {
            // 10px is 7.5pt, and a calc() with no percentage in it is as definite as the plain length.
            var (own, parent, gap) = await MeasureAsync(tag, string.Empty, declaration);

            Assert.Equal(7.5, own, 3);
            Assert.Equal(47.5, parent, 3);
            Assert.Equal(7.5, gap, 3);
        }
    }
}
