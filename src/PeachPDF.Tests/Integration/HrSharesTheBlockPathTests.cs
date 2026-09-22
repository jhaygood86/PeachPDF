using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// An <c>&lt;hr&gt;</c> is an ordinary block-level box, so it takes every width rule an equivalent
    /// <c>&lt;div&gt;</c> does - there is no rule-specific layout class to bypass any of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each case is asserted against a literal measured in Chrome 153 for a real <c>&lt;hr&gt;</c> and against
    /// the <c>&lt;div&gt;</c> that is its exact equivalent (a 1px border stands in for the rule's UA border,
    /// which is what gives it its 1.5pt box), because "the two agree" is what makes pairing them in a test or
    /// a showcase valid. <c>margin: 0</c> keeps the rule's own default margins out of the arithmetic.
    /// </para>
    /// <para>
    /// These are the behaviours the rule got for free by no longer resolving its own width: the
    /// §10.4 <c>min-width</c>/<c>max-width</c> clamp, the §10.3.5 shrink-to-fit of a float and an
    /// absolutely-positioned box, and the intrinsic sizing of a flex item and an inline-block.
    /// </para>
    /// </remarks>
    public class HrSharesTheBlockPathTests
    {
        private const string Edge = "border: 1px solid";

        private static async Task<(double Rule, double Div)> BorderBoxWidthsAsync(
            string containerDeclaration, string declaration)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                $"<!DOCTYPE html><html><body style='margin: 0'><div style='width: 200pt; position: relative; {containerDeclaration}'>"
                + $"<hr id='rule' style='margin: 0; {declaration}'>"
                + $"<div id='equivalent' style='margin: 0; height: auto; {Edge}; {declaration}'></div>"
                + "</div></body></html>");

            var rule = LayoutHarness.FindById(root, "rule")!;
            var equivalent = LayoutHarness.FindById(root, "equivalent")!;

            return (rule.ActualRight - rule.Location.X, equivalent.ActualRight - equivalent.Location.X);
        }

        // ── §10.4: min-width and max-width apply to a rule exactly as to any block ─

        [Theory]
        // content width 100 (50% of 200pt) raised to min-width 180, plus 1.5pt of border
        [InlineData("width: 50%; min-width: 180pt", 181.5)]
        // ...lowered to max-width 40
        [InlineData("width: 50%; max-width: 40pt", 41.5)]
        // min wins over max on conflict
        [InlineData("width: 50%; max-width: 40pt; min-width: 60pt", 61.5)]
        // and both apply to an auto width too
        [InlineData("max-width: 40pt", 41.5)]
        [InlineData("min-width: 250pt", 251.5)]
        public async Task MinAndMaxWidth_ClampARulesUsedWidth(string declaration, double expected)
        {
            var (rule, equivalent) = await BorderBoxWidthsAsync(string.Empty, declaration);

            Assert.Equal(expected, rule, 3);
            Assert.Equal(expected, equivalent, 3);
        }

        // ── §10.3.5 / §10.3.7: an auto width shrinks to fit for a float or absolute box ─

        [Theory]
        // Nothing inside a rule, so its shrink-to-fit content is 0 and only its 1.5pt of border remains.
        [InlineData("", "float: left", 1.5)]
        [InlineData("", "float: right", 1.5)]
        [InlineData("", "position: absolute", 1.5)]
        [InlineData("", "display: inline-block", 1.5)]
        [InlineData("display: flex", "", 1.5)]
        // A declared width still wins over shrink-to-fit.
        [InlineData("", "float: left; width: 50%", 101.5)]
        public async Task AnAutoWidthRule_ShrinksToFitWhereABlockWould(
            string containerDeclaration, string declaration, double expected)
        {
            var (rule, equivalent) = await BorderBoxWidthsAsync(containerDeclaration, declaration);

            // To 0.05pt against the literal: the flex engine adds a 0.01pt epsilon to a flex item's
            // border box (1.51 for the rule and the <div> alike), which is not the rule's to answer for.
            // The rule and its equivalent <div> agree exactly, which is the property that matters.
            Assert.Equal(expected, rule, 1);
            Assert.Equal(equivalent, rule, 3);
        }

        [Fact]
        public async Task AnAutoWidthRuleInFlow_StillFillsItsContainingBlock()
        {
            var (rule, equivalent) = await BorderBoxWidthsAsync(string.Empty, string.Empty);

            Assert.Equal(200, rule, 3);
            Assert.Equal(200, equivalent, 3);
        }

        // ── the floor at the box's own edges (border-box), which the rule now shares ─

        [Theory]
        [InlineData("width: 0", "box-sizing: border-box; padding: 0 10pt; border: 4px solid", 26)]
        [InlineData("width: 5pt", "box-sizing: border-box; padding: 0 10pt; border: 4px solid", 26)]
        [InlineData("width: 200pt", "box-sizing: border-box; width: 5pt; padding: 0 10pt; border: 4px solid", 26)]
        public async Task ABorderBoxRule_FloorsAtItsOwnPaddingAndBorder(
            string containerDeclaration, string declaration, double expected)
        {
            var (rule, equivalent) = await BorderBoxWidthsAsync(containerDeclaration, declaration);

            Assert.Equal(expected, rule, 3);
            Assert.Equal(expected, equivalent, 3);
        }
    }
}
