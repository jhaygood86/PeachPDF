using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A percentage <c>width</c> on an <c>&lt;hr&gt;</c> resolves against the containing block's
    /// <b>content</b> width, per
    /// <see href="https://www.w3.org/TR/CSS21/visudet.html#the-width-property">CSS 2.1 §10.2</see>, with
    /// the rule's own margins, borders and padding outside it (issue #1230). <c>CssBoxHr</c> used to
    /// reuse its <c>auto</c> expression - the available space, already reduced by the rule's own
    /// margins and borders - as the percentage basis, making every percentage rule narrower than the
    /// equivalent <c>&lt;div&gt;</c> by twice its border width.
    /// <para>
    /// Every case is asserted against the zero-height <c>&lt;div&gt;</c> that is its exact equivalent as
    /// well as against a literal, because "the rule and its equivalent div agree" is the property that
    /// actually matters - it is what makes pairing them in a test or a showcase valid.
    /// </para>
    /// </summary>
    public class HrPercentageWidthLayoutTests
    {
        /// <summary>
        /// Lays out an <c>&lt;hr&gt;</c> and its equivalent zero-height <c>&lt;div&gt;</c> under the same
        /// declaration inside a 200pt containing block, and returns each one's content width.
        /// </summary>
        private static async Task<(double Rule, double Div)> ContentWidthsAsync(string declaration)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body><div style='width: 200pt'>"
                + $"<hr id='rule' style='margin: 0; {declaration}'>"
                + $"<div id='equivalent' style='margin: 0; height: 0; {declaration}'></div>"
                + "</div></body></html>");

            return (LayoutHarness.FindById(root, "rule")!.Size.Width,
                    LayoutHarness.FindById(root, "equivalent")!.Size.Width);
        }

        [Theory]
        // 4px borders are 3pt a side (1px = 0.75pt), so the old basis was short by 6pt and every
        // percentage came out 6% of that narrower: 50% gave 97pt where CSS asks for 100.
        [InlineData("width: 50%; border: 4px solid", 100)]
        [InlineData("width: 100%; border: 4px solid", 200)]
        [InlineData("width: 25%; border: 4px solid", 50)]
        // A margin does not reduce the basis either, though it does reduce `auto`.
        [InlineData("width: 50%; margin-left: 20pt; border: 4px solid", 100)]
        // Padding is outside the basis in exactly the same way as border.
        [InlineData("width: 50%; padding: 0 10pt; border: 4px solid", 100)]
        // ...and with no border at all the two expressions coincide, so this one passed before too.
        [InlineData("width: 50%", 100)]
        public async Task APercentageWidth_ResolvesAgainstTheContainingBlocksContentWidth(
            string declaration, double expected)
        {
            var (rule, equivalent) = await ContentWidthsAsync(declaration);

            Assert.Equal(expected, rule, 3);
            Assert.Equal(expected, equivalent, 3);
        }

        [Theory]
        // Under box-sizing: border-box the percentage IS the border box, and Size.Width holds it in
        // those terms rather than as a content width - so these are the resolved percentage itself.
        // Worth pinning separately because ParseLength applies box-sizing on top of whatever basis it
        // is handed, so a wrong basis was compounded here: `width: 50%` painted 96px of a 200px block
        // where Chrome paints 100.
        [InlineData("width: 50%; box-sizing: border-box; border: 4px solid", 100)]
        [InlineData("width: 100%; box-sizing: border-box; border: 4px solid", 200)]
        [InlineData("width: 50%; box-sizing: border-box; padding: 0 10pt; border: 4px solid", 100)]
        public async Task ABorderBoxPercentageWidth_ResolvesAgainstTheSameBasis(
            string declaration, double expected)
        {
            var (rule, equivalent) = await ContentWidthsAsync(declaration);

            Assert.Equal(expected, rule, 3);
            Assert.Equal(expected, equivalent, 3);
        }

        [Fact]
        public async Task AnAutoWidth_SubtractsTheRulesOwnPaddingToo()
        {
            // The same expression's other half, corrected alongside: an `auto` rule with horizontal
            // padding used to overflow its containing block by exactly that padding, because only
            // margins and borders were taken out of the available space. Measured at 220px inside a
            // 200px block before, against Chrome's (and the equivalent div's) 200.
            var (rule, equivalent) = await ContentWidthsAsync("padding: 0 10pt; border: 4px solid");

            // 200pt, less 3pt borders and 10pt padding a side.
            Assert.Equal(174, rule, 3);
            Assert.Equal(174, equivalent, 3);
        }

        [Fact]
        public async Task AnAutoWidth_IsStillTheAvailableSpaceLessTheRulesOwnEdges()
        {
            // The expression the percentage basis used to borrow, and which is correct where it
            // belongs: an unstyled rule spans its container exactly, its borders included. Pinned so a
            // fix to the percentage basis cannot be made by deleting the subtraction outright.
            var (root, _) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body><div style='width: 200pt'>"
                + "<hr id='rule' style='margin: 0; border: 4px solid'>"
                + "</div></body></html>");

            var rule = LayoutHarness.FindById(root, "rule")!;

            // 200pt of containing block, less this rule's own 3pt borders a side.
            Assert.Equal(194, rule.Size.Width, 3);
            // ...so its border box is the full 200pt, which is the point of the `auto` expression.
            Assert.Equal(200, rule.Size.Width + rule.ActualBorderLeftWidth + rule.ActualBorderRightWidth, 3);
        }

        [Fact]
        public async Task AnAutoWidthRuleAgreesWithItsEquivalentDiv()
        {
            // `auto` was already right on both sides - a block's used content width is the available
            // space less its own edges, so both come out 194 and both border boxes span the full 200.
            // Pinned because the fix splits one expression into two, and the `auto` half must keep
            // producing exactly what it did.
            var (rule, equivalent) = await ContentWidthsAsync("border: 4px solid");

            Assert.Equal(194, rule, 3);
            Assert.Equal(194, equivalent, 3);
        }
    }
}
