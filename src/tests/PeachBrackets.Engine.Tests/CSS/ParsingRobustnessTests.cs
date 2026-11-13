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

        [Theory]
        [InlineData("column-gap: -5pt")]
        [InlineData("gap: -5pt")]
        [InlineData("gap: 2pt -5pt")]
        public async Task ANegativeGap_IsInvalid_SoTheEarlierGapApplies(string negative)
        {
            // Two 95pt columns and a 10pt gap: the rule sits in the middle of the gap, 100pt from the left edge.
            var html = LayoutHarness.Wrap(
                "<style>#b { width: 200pt; columns: 2; column-rule: 1pt solid black; column-gap: 10pt; " + negative + " }</style>" +
                "<div id='b'><div style='height: 20pt'></div><div style='height: 20pt'></div></div>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var b = LayoutHarness.FindById(root, "b")!;

            Assert.Equal(100, b.ColumnRuleSegments![0].X - b.Location.X, 1);
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

        [Theory]
        [InlineData("width: -5pt", 100)]          // dropped: the earlier valid width stays
        [InlineData("width: calc(50pt - 200pt)", 0)] // a calc() clamps to the range instead
        [InlineData("min-width: -5pt", 100)]
        public async Task ANegativeSize_IsInvalid_ButANegativeCalcClamps(string declaration, double expectedWidth)
        {
            var html = LayoutHarness.Wrap($"<style>#b {{ width: 100pt; {declaration} }}</style><div id='b'>x</div>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);

            Assert.Equal(expectedWidth, LayoutHarness.FindById(root, "b")!.ActualWidth, 2);
        }

        [Fact]
        public async Task ANegativeMaxHeight_IsDropped_SoTheDeclaredHeightApplies()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap("<style>#b { height: 72pt; max-height: -1pt }</style><div id='b'>x</div>"));

            Assert.Equal(72, LayoutHarness.FindById(root, "b")!.ActualHeight, 2);
        }

        [Theory]
        [InlineData("border-top: 6pt solid black", 6)]
        [InlineData("border-top: 6pt solid black; border-top-width: 4pt", 4)]
        // The invalid declaration is dropped. (It used to be folded to its absolute value, 4pt; it is the initial `medium`, 2pt, here: whether it should keep
        // the 6pt before it is not what this pins.)
        [InlineData("border-top-style: solid; border-top-width: -4pt", 2)]
        public async Task ANegativeBorderWidth_IsInvalid_AndIsNotFoldedToItsAbsoluteValue(string declarations, double expected)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap($"<style>#b {{ {declarations} }}</style><div id='b'>x</div>"));

            Assert.Equal(expected, LayoutHarness.FindById(root, "b")!.ActualBorderTopWidth, 2);
        }
    }
}
