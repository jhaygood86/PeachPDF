using PeachBrackets.Svg;
using PeachDrawing.Core;
using System.Linq;
using System.Threading.Tasks;

namespace PeachBrackets.Engine.Tests.Svg
{
    /// <summary>
    /// What a keyframe may hold on an element of an inline <c>&lt;svg&gt;</c>: a literal the property cannot take is dropped, a function (<c>calc()</c>, <c>min()</c>)
    /// that comes out the same is valid and clamps when used, and a <c>-0</c> is a zero. The numbers are what headless Chrome computes for the same paused animation.
    /// </summary>
    public class SvgCssAnimationKeyframeEdgeCaseTests
    {
        private static async Task<double> RadiusAsync(string from, string to, double progress)
        {
            var document = await SvgCssAnimationTests.BuildAsync($"@keyframes k {{ from {{ r: {from} }} to {{ r: {to} }} }} .t {{ animation: k 1s linear }}",
                "<circle class=\"t\" cx=\"50\" cy=\"50\" r=\"10\"/>", progress);

            return Assert.IsType<SvgCircleElement>(document.Children[0]).R;
        }

        [Theory]
        [InlineData("-0px", 0.3, 6)]            // a negative zero is a zero: 0 to 20px
        [InlineData("-0px", 0.5, 10)]
        [InlineData("-0.0em", 0.5, 10)]
        public async Task ANegativeZero_IsAValidKeyframeValue(string from, double progress, double expected) =>
            Assert.Equal(expected, await RadiusAsync(from, "20", progress), 2);

        [Theory]
        [InlineData("calc(-5px)", 0.3, 2.5)]    // valid, and clamped only where it is used: -5px to 20px is 2.5 at the 30% point
        [InlineData("calc(-5px)", 0.8, 15)]
        [InlineData("calc(10px - 20px)", 0.3, 0)]
        [InlineData("min(-5px, 3px)", 0.3, 2.5)]
        public async Task AFunctionThatComesOutNegative_IsNotDropped(string from, double progress, double expected) =>
            Assert.Equal(expected, await RadiusAsync(from, "20px", progress), 2);

        [Theory]
        [InlineData("-5px")]
        [InlineData("-.5px")]
        [InlineData("-1e1px")]
        [InlineData("-1em")]
        [InlineData("-5%")]
        public async Task ANegativeLiteral_IsDroppedWhateverItIsWritten(string from) =>
            // Dropped, the animation runs from the attribute's 10 to 20: 13 at the 30% point.
            Assert.Equal(13, await RadiusAsync(from, "20px", 0.3), 2);

        [Theory]
        [InlineData("ry", "<ellipse class=\"t\" cx=\"50\" cy=\"50\" rx=\"9\" ry=\"7\"/>", 7)]
        [InlineData("height", "<rect class=\"t\" width=\"20\" height=\"12\"/>", 12)]
        [InlineData("stroke-width", "<rect class=\"t\" width=\"20\" height=\"12\" stroke=\"black\" stroke-width=\"4\"/>", 4)]
        public async Task ANegativeKeyframe_IsTheOnlyOneAndSoNothingAnimates(string property, string body, double expected)
        {
            // Chrome: a keyframe rule left with no valid declaration animates nothing, so the element keeps its attribute.
            var document = await SvgCssAnimationTests.BuildAsync($"@keyframes k {{ to {{ {property}: -10px }} }} .t {{ animation: k 1s linear }}", body, 0.5);

            var element = document.Children[0];
            Assert.Equal(expected, element switch
            {
                SvgEllipseElement e => e.Ry,
                SvgRectElement r when property == "height" => r.Height,
                _ => element.StrokeWidth,
            }, 2);
        }

        [Theory]
        [InlineData("d: M0 0 L100 0")]                       // the attribute's data is not a value of d in CSS
        [InlineData("d: path(evenodd, 'M0 0 L30 30')")]      // nor is a path() with a fill rule
        public async Task ADValueThatIsNotAPathOrNone_IsDroppedFromTheKeyframe(string declaration)
        {
            // Chrome: the to keyframe is dropped, so the path mixes from its first keyframe to the attribute's own data: (10 10, 50 50) to (0 0, 10 10).
            var document = await SvgCssAnimationTests.BuildAsync($"@keyframes k {{ from {{ d: path('M10 10 L50 50') }} to {{ {declaration} }} }} .t {{ animation: k 1s linear }}",
                "<path class=\"t\" d=\"M0 0 L10 10\" stroke=\"black\"/>", 0.5);

            var segments = Assert.IsType<SvgPathElement>(document.Children[0]).Segments!;
            Assert.Equal((5d, 5d, 30d, 30d), (segments[0].X, segments[0].Y, segments[1].X, segments[1].Y));
        }

        [Fact]
        public async Task ANegativeItemAfterAComma_DropsTheDashList()
        {
            var document = await SvgCssAnimationTests.BuildAsync(
                "@keyframes k { from { stroke-dasharray: 2 4 } to { stroke-dasharray: 10, -20, 30 } } .t { animation: k 1s linear }",
                "<rect class=\"t\" width=\"10\" height=\"10\" stroke=\"black\" stroke-dasharray=\"8\"/>", 0.5);

            Assert.Equal(new[] { 5d, 6 }, document.Children[0].StrokeDashArray);
        }

        // ─── the lowest common multiple's limit ──────────────────────────────────

        private static string Dashes(int count) => string.Join(' ', Enumerable.Repeat("2", count));

        [Theory]
        [InlineData(16, 15, true)]       // lcm 240: mixed
        [InlineData(16, 17, false)]      // lcm 272: more than a dash list is repeated to, so the pair flips
        [InlineData(17, 19, false)]
        public async Task TheLowestCommonMultipleOfTwoDashLists_IsRepeatedUpToALimit(int fromCount, int toCount, bool mixed)
        {
            var document = await SvgCssAnimationTests.BuildAsync(
                $"@keyframes k {{ from {{ stroke-dasharray: {Dashes(fromCount)} }} to {{ stroke-dasharray: {Dashes(toCount).Replace("2", "6")} }} }} .t {{ animation: k 1s linear }}",
                "<rect class=\"t\" width=\"10\" height=\"10\" stroke=\"black\"/>", 0.25);

            var dashes = document.Children[0].StrokeDashArray;

            // A flip at the quarter is the from list as it is (an odd list is drawn repeated, so twice its length); a mix is 2 to 6 at a quarter, which is 3, over the common length.
            Assert.Equal(mixed ? fromCount * toCount / Gcd(fromCount, toCount) : fromCount % 2 == 0 ? fromCount : fromCount * 2, dashes.Length);
            Assert.All(dashes, dash => Assert.Equal(mixed ? 3 : 2, dash, 4));
        }

        private static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);

        // ─── an instance's text runs ─────────────────────────────────────────────

        [Fact]
        public async Task ATspanInAnInstance_StartsFromTheFillOfTheUse()
        {
            var document = await SvgCssAnimationTests.BuildAsync("@keyframes k { to { fill: #f00 } } .t { animation: k 1s linear }",
                "<defs><text id=\"r\" x=\"1\" y=\"20\"><tspan class=\"t\">Ab</tspan></text></defs><use href=\"#r\" fill=\"#00f\"/>", 0.5);

            var text = Assert.IsType<SvgTextElement>(Assert.IsType<SvgUseElement>(document.Children[0]).Target);
            Assert.Equal(PaintColor.FromArgb(255, 128, 0, 128), text.Spans.Single().Fill.PaintColor);
        }
    }
}
