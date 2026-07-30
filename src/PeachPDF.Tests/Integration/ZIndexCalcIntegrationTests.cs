using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end coverage for <c>z-index: calc(...)</c> folding to a plain integer (CSS Values and Units
    /// Level 4 §10.9), through the full cascade pipeline - not just the CSS-OM parser level covered by
    /// <c>WptCssPositionInsetLogicalParsingTests</c>. <c>CssUtils.cs</c>'s DOM-application step gates
    /// z-index on <c>int.TryParse</c> before assigning <see cref="CssBox.ZIndex"/>, and
    /// <c>StackingOrder</c> later does a bare <c>int.Parse(box.ZIndex)</c> - both would break if a
    /// z-index calc() only got as far as parsing without also being folded to a literal integer string.
    /// </summary>
    public class ZIndexCalcIntegrationTests
    {
        [Fact]
        public async Task PositionedBox_ZIndexCalcExpression_FoldsToLiteralIntegerOnTheBox()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='t' style='position:relative; z-index: calc(3 - 2);'></div>"));

            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(box);
            Assert.Equal("1", box!.ZIndex);
        }

        [Fact]
        public async Task PositionedBox_ZIndexNegativeCalcExpression_FoldsToLiteralIntegerOnTheBox()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='t' style='position:relative; z-index: calc(2 * -3);'></div>"));

            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(box);
            Assert.Equal("-6", box!.ZIndex);
        }
    }
}
