using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.CSS
{
    /// <summary>Valid style sheets that used to throw while the cascade read them (found by the web-platform-tests reftests).</summary>
    public class ParsingRobustnessTests
    {
        [Theory]
        // percentage slice / width / outset: the unlabeled periodic values of the shorthand are exported per longhand
        [InlineData("border-image: url(\"i.png\") 5% 10% 15% 20% / 15% 20% 35% 30% / 0 repeat repeat")]
        [InlineData("border-image: url(\"i.png\") 30 / 10px 5px / 2 round")]
        [InlineData("border-image: url(\"i.png\") 1 / 1 / 1")]
        public async Task ABorderImageShorthand_WithWidthAndOutsetLists_LaysOut(string declaration)
        {
            var html = LayoutHarness.Wrap($"<style>#b {{ border: 10px solid; {declaration} }}</style><div id='b'>x</div>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);

            Assert.NotNull(LayoutHarness.FindById(root, "b"));
        }

        [Theory]
        [InlineData("-2147483649")]
        [InlineData("2147483649")]
        [InlineData("-99999999999999999999")]
        [InlineData("+99999999999999999999")]
        public async Task AnIntegerBeyondThirtyTwoBits_IsClamped_NotAnError(string zIndex)
        {
            var html = LayoutHarness.Wrap($"<style>#b {{ position: relative; z-index: {zIndex}; }}</style><div id='b'>x</div>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);

            Assert.NotNull(LayoutHarness.FindById(root, "b"));
        }

        [Theory]
        [InlineData("padding-top: -1px", 0)]
        [InlineData("padding-left: -5pt", 0)]
        [InlineData("padding: -1px", 0)]
        [InlineData("padding: 4pt -1px", 0)]
        public async Task ANegativePadding_IsInvalid_AndTheDeclarationIsDropped(string declaration, double expectedTop)
        {
            var html = LayoutHarness.Wrap($"<style>#b {{ padding-top: 0; {declaration} }}</style><div id='b'>x</div>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var b = LayoutHarness.FindById(root, "b")!;

            Assert.Equal(expectedTop, b.ActualPaddingTop, 3);
            Assert.Equal(0, b.ActualPaddingLeft, 3);
        }

        [Fact]
        public async Task APositivePadding_StillApplies_AfterAnInvalidNegativeOne()
        {
            var html = LayoutHarness.Wrap("<style>#b { padding-top: 6pt; padding-top: -3pt; padding-left: 2pt }</style><div id='b'>x</div>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var b = LayoutHarness.FindById(root, "b")!;

            // The invalid declaration leaves the earlier valid one in force.
            Assert.Equal(6, b.ActualPaddingTop, 3);
            Assert.Equal(2, b.ActualPaddingLeft, 3);
        }
    }
}
