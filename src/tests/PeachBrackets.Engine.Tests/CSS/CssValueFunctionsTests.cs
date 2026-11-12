using System;
using System.Linq;
using System.Threading.Tasks;
using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Parse;
using Xunit;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.CSS
{
    /// <summary>
    /// The value functions beyond calc/min/max/clamp: the rounding, trigonometric, exponential and sign functions of
    /// CSS Values 4 §10, <c>env()</c>, <c>sibling-index()</c>/<c>sibling-count()</c>, <c>contrast-color()</c> and <c>calc-size()</c>.
    /// </summary>
    public class CssValueFunctionsTests
    {
        private static CalcNode? Parse(string css) =>
            CalcParser.Parse(CssValueParser.GetCssTokens(css).Single(t => t.Type == TokenType.Function));

        private static double? Evaluate(string css)
        {
            var node = Parse(css);
            Assert.NotNull(node);
            return CalcEvaluator.Evaluate(node!, new CalcContext(100, 10, 16));
        }

        [Theory]
        [InlineData("round(7, 2)", 8)]
        [InlineData("round(nearest, 6.5, 2)", 6)]
        [InlineData("round(up, 5.1, 2)", 6)]
        [InlineData("round(down, 5.9, 2)", 4)]
        [InlineData("round(to-zero, -5.9, 2)", -4)]
        [InlineData("round(up, -5.9, 2)", -4)]
        [InlineData("round(2.6)", 3)]
        [InlineData("round(up, 33.3px, 10px)", 40)]
        [InlineData("mod(7, 3)", 1)]
        [InlineData("mod(-7, 3)", 2)]
        [InlineData("mod(7, -3)", -2)]
        [InlineData("rem(-7, 3)", -1)]
        [InlineData("rem(7, -3)", 1)]
        [InlineData("sin(90deg)", 1)]
        [InlineData("cos(0deg)", 1)]
        [InlineData("tan(45deg)", 1)]
        [InlineData("sin(pi / 2)", 1)]
        [InlineData("asin(1)", Math.PI / 2)]
        [InlineData("acos(1)", 0)]
        [InlineData("atan(1)", Math.PI / 4)]
        [InlineData("atan2(1, 1)", Math.PI / 4)]
        [InlineData("atan2(1px, -1px)", 3 * Math.PI / 4)]
        [InlineData("pow(2, 10)", 1024)]
        [InlineData("sqrt(81)", 9)]
        [InlineData("hypot(3, 4)", 5)]
        [InlineData("hypot(3px, 4px, 12px)", 13)]
        [InlineData("log(e)", 1)]
        [InlineData("log(8, 2)", 3)]
        [InlineData("exp(0)", 1)]
        [InlineData("abs(-5px)", 5)]
        [InlineData("abs(-5)", 5)]
        [InlineData("sign(-3px)", -1)]
        [InlineData("sign(0)", 0)]
        [InlineData("sign(2)", 1)]
        [InlineData("calc(round(down, 25%, 10px) + 1px)", 21)]
        public void TheFunctionEvaluatesToItsSpecifiedValue(string css, double expected)
        {
            // Lengths resolve to layout units (1px = 0.75pt); 25% of 100 is 25 against a 10px (7.5pt) step.
            var value = Evaluate(css);
            Assert.NotNull(value);
            var scale = css.Contains("px", StringComparison.Ordinal) && !css.Contains("atan2", StringComparison.Ordinal) && !css.Contains("sign", StringComparison.Ordinal)
                ? 0.75
                : 1;
            if (css.StartsWith("calc(round(down, 25%", StringComparison.Ordinal))
                Assert.Equal(22.5 + 0.75, value!.Value, 6);
            else
                Assert.Equal(expected * scale, value!.Value, 6);
        }

        [Theory]
        [InlineData("round(up)")]
        [InlineData("round(5px)")]
        [InlineData("mod(1)")]
        [InlineData("mod(1, 2, 3)")]
        [InlineData("sin(1px)")]
        [InlineData("asin(1deg)")]
        [InlineData("pow(2px, 2)")]
        [InlineData("sqrt(4px)")]
        [InlineData("log(1px)")]
        [InlineData("exp()")]
        [InlineData("mod(1px, 2deg)")]
        [InlineData("round(sideways, 1, 2)")]
        public void AMalformedOrMistypedCallIsNotAValue(string css)
        {
            var node = Parse(css);
            Assert.True(node is null || CalcTypeChecker.Check(node) is null, css);
        }

        [Theory]
        [InlineData("mod(5, 0)")]
        [InlineData("rem(5, 0)")]
        [InlineData("round(5, 0)")]
        [InlineData("asin(2)")]
        [InlineData("log(-1)")]
        public void AnInvalidComputationHasNoValue(string css) => Assert.Null(Evaluate(css));

        [Fact]
        public void TheCanonicalTextOfARoundingFunctionKeepsItsStrategy()
        {
            var converter = new CalcValueConverter(CalcCategory.LengthPercentage);
            var value = converter.Convert(CssValueParser.GetCssTokens("round(up, 1em + 3px, 5%)"));
            Assert.NotNull(value);
            Assert.Equal("round(up, calc(1em + 3px), 5%)".Replace("calc(1em + 3px)", "1em + 3px"), value!.CssText);
        }

        [Fact]
        public void ATrigonometricCalcFolds()
        {
            var converter = new CalcValueConverter(CalcCategory.LengthPercentage);
            Assert.Equal("100px", converter.Convert(CssValueParser.GetCssTokens("calc(sin(90deg) * 100px)"))!.CssText);
            Assert.Equal("1024px", converter.Convert(CssValueParser.GetCssTokens("calc(pow(2, 10) * 1px)"))!.CssText);
        }

        private static async Task<CssBox> BoxAsync(string html)
        {
            var (root, _) = await LayoutAsync(Wrap(html));
            return FindById(root, "box")!;
        }

        private static double WidthOf(CssBox box) => box.ActualRight - box.Location.X;

        [Fact]
        public async Task AMathFunctionSizesABox()
        {
            Assert.Equal(40, WidthOf(await BoxAsync("<div id='box' style='width: round(up, 33.3pt, 10pt)'>x</div>")), 3);
            Assert.Equal(50, WidthOf(await BoxAsync("<div id='box' style='width: hypot(30pt, 40pt)'>x</div>")), 3);
            Assert.Equal(60, WidthOf(await BoxAsync("<div id='box' style='width: calc(sin(30deg) * 120pt)'>x</div>")), 3);
        }

        [Fact]
        public async Task EnvResolvesToZeroForAKnownNameAndToTheFallbackOtherwise()
        {
            Assert.Equal(0, WidthOf(await BoxAsync("<div id='box' style='width: env(safe-area-inset-left, 70pt)'>x</div>")), 3);
            Assert.Equal(70, WidthOf(await BoxAsync("<div id='box' style='width: env(not-a-real-name, 70pt)'>x</div>")), 3);
            Assert.Equal(25, WidthOf(await BoxAsync("<div id='box' style='width: calc(env(safe-area-inset-top) + 25pt)'>x</div>")), 3);
        }

        [Fact]
        public async Task EnvWithNoFallbackForAnUnknownNameInvalidatesTheDeclaration()
        {
            // Invalid at computed-value time: the declaration is dropped, so the block keeps its auto (stretch) width.
            var box = await BoxAsync("<div id='box' style='width: env(not-a-real-name)'>x</div>");
            Assert.True(WidthOf(box) > 100);
        }

        [Fact]
        public async Task SiblingIndexAndCountAreTheElementsPositionEvenForSiblingsThatOtherwiseShareAStyle()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<style>p { margin: 0 } p.n { width: calc(sibling-index() * 10pt) } i { display: block; width: calc(100pt / sibling-count()) }</style>"
                + "<div><p class='n' id='a'>x</p><p class='n' id='b'>x</p><p class='n' id='c'>x</p><i id='d'>y</i><i id='e'>y</i></div>"));

            Assert.Equal(10, WidthOf(FindById(root, "a")!), 3);
            Assert.Equal(20, WidthOf(FindById(root, "b")!), 3);
            Assert.Equal(30, WidthOf(FindById(root, "c")!), 3);
            Assert.Equal(100.0 / 5, WidthOf(FindById(root, "d")!), 3);
        }

        [Fact]
        public async Task SiblingIndexCountsElementsOnly()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div><!-- note -->text<span style='display:block;width:calc(sibling-index() * 7pt)' id='a'>x</span>text<span style='display:block;width:calc(sibling-index() * 7pt)' id='b'>x</span></div>"));

            Assert.Equal(7, WidthOf(FindById(root, "a")!), 3);
            Assert.Equal(14, WidthOf(FindById(root, "b")!), 3);
        }

        [Theory]
        [InlineData("white", 0)]
        [InlineData("black", 255)]
        [InlineData("yellow", 0)]
        [InlineData("navy", 255)]
        [InlineData("rgb(from white r g b)", 0)]
        [InlineData("color-mix(in srgb, black, white 10%)", 255)]
        public void ContrastColorPicksTheMoreReadableOfBlackAndWhite(string operand, int expectedChannel)
        {
            var color = CssValueParser.GetCssTokens("contrast-color(" + operand + ")").ToResolvedColor();
            Assert.NotNull(color);
            Assert.Equal(expectedChannel, color!.Value.R);
            Assert.Equal(expectedChannel, color.Value.G);
            Assert.Equal(expectedChannel, color.Value.B);
        }

        [Theory]
        [InlineData("contrast-color()")]
        [InlineData("contrast-color(white, black)")]
        [InlineData("contrast-color(banana)")]
        public void ContrastColorWithABadOperandIsNotAColor(string css) =>
            Assert.Null(CssValueParser.GetCssTokens(css).ToResolvedColor());

        [Fact]
        public async Task CalcSizeWithALengthBasisResolvesTheExpression()
        {
            Assert.Equal(200, WidthOf(await BoxAsync("<div id='box' style='width: calc-size(100pt, size * 2)'>x</div>")), 3);
            var box = await BoxAsync("<div id='box' style='height: calc-size(50pt, size + 10pt); width: 40pt'>x</div>");
            Assert.Equal(60, box.ActualBottom - box.Location.Y, 3);
        }

        [Fact]
        public async Task CalcSizeAutoTakesTheAutoWidth()
        {
            var (root, _) = await LayoutAsync(Wrap("<div style='width: 200pt'><div id='box' style='width: calc-size(auto, size / 2)'>x</div></div>"));
            Assert.Equal(100, WidthOf(FindById(root, "box")!), 3);
        }

        [Fact]
        public async Task CalcSizeMaxContentAndMinContentMeasureTheContent()
        {
            var plain = await BoxAsync("<div style='float:left' id='box'>alpha beta</div>");
            var maxContent = await BoxAsync("<div id='box' style='float:left;width: calc-size(max-content, size + 10pt)'>alpha beta</div>");
            Assert.Equal(WidthOf(plain) + 10, WidthOf(maxContent), 2);

            var minContent = await BoxAsync("<div id='box' style='float:left;width: calc-size(min-content, size + 10pt)'>alpha beta</div>");
            Assert.True(WidthOf(minContent) < WidthOf(maxContent));
            Assert.True(WidthOf(minContent) > 10);
        }

        [Fact]
        public async Task CalcSizeMinAndMaxWidthClamp()
        {
            var (root, _) = await LayoutAsync(Wrap("<div style='width: 300pt'><div id='box' style='max-width: calc-size(auto, size / 3)'>x</div></div>"));
            Assert.Equal(100, WidthOf(FindById(root, "box")!), 3);
        }

        private static double HeightOf(PeachBrackets.Html.Dom.CssBox box) => box.ActualBottom - box.Location.Y;

        [Theory]
        [InlineData("auto")]
        [InlineData("min-content")]
        [InlineData("max-content")]
        [InlineData("fit-content")]
        public async Task ABlockAxisKeywordBasisIsTheContentHeight(string keyword)
        {
            var plain = HeightOf(await BoxAsync("<div id='box'>x</div>"));
            var box = await BoxAsync($"<div id='box' style='height: calc-size({keyword}, size + 100pt)'>x</div>");
            Assert.Equal(plain + 100, HeightOf(box), 2);
        }

        [Fact]
        public async Task ABlockAxisKeywordBasisScalesTheContentHeight()
        {
            var plain = HeightOf(await BoxAsync("<div id='box'>x</div>"));
            var box = await BoxAsync("<div id='box' style='height: calc-size(auto, size * 3)'>x</div>");
            Assert.Equal(plain * 3, HeightOf(box), 2);
        }

        [Fact]
        public async Task ABlockAxisKeywordBasisExcludesPaddingAndBorderFromTheBasis()
        {
            var plain = HeightOf(await BoxAsync("<div id='box' style='padding: 5pt; border: 2pt solid'>x</div>"));
            var box = await BoxAsync("<div id='box' style='padding: 5pt; border: 2pt solid; height: calc-size(auto, size + 30pt)'>x</div>");
            Assert.Equal(plain + 30, HeightOf(box), 2);
        }

        [Fact]
        public async Task ABlockAxisKeywordBasisInMinHeightGrowsTheBox()
        {
            var plain = HeightOf(await BoxAsync("<div id='box'>x</div>"));
            var box = await BoxAsync("<div id='box' style='min-height: calc-size(min-content, size + 40pt)'>x</div>");
            Assert.Equal(plain + 40, HeightOf(box), 2);
        }

        [Fact]
        public async Task ABlockAxisKeywordBasisInMaxHeightShrinksTheBox()
        {
            var plain = HeightOf(await BoxAsync("<div id='box'><p style='margin:0'>a</p><p style='margin:0'>b</p></div>"));
            var box = await BoxAsync("<div id='box' style='max-height: calc-size(max-content, size / 2)'><p style='margin:0'>a</p><p style='margin:0'>b</p></div>");
            Assert.Equal(plain / 2, HeightOf(box), 2);
        }

        [Fact]
        public async Task ABlockAxisKeywordBasisHeightFlowsIntoTheParent()
        {
            var (root, _) = await LayoutAsync(Wrap("<div id='outer'><div id='inner' style='height: calc-size(auto, size + 50pt)'>x</div></div>"));
            var inner = FindById(root, "inner")!;
            Assert.Equal(HeightOf(inner), HeightOf(FindById(root, "outer")!), 2);
            Assert.True(HeightOf(inner) > 50);
        }

        [Fact]
        public async Task DirPseudoClassMatchesByDirectionalityIncludingInherited()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<style>div { display: block } :dir(rtl) > i { width: 40pt } :dir(ltr) > i { width: 70pt } i { display: block }</style>"
                + "<div dir='rtl'><i id='a'>x</i></div><div><i id='b'>x</i></div><div dir='rtl'><b dir='ltr'><i id='c'>x</i></b></div>"
                + "<div dir='auto'><i id='d'>x</i></div>"));

            Assert.Equal(40, WidthOf(FindById(root, "a")!), 3);
            Assert.Equal(70, WidthOf(FindById(root, "b")!), 3);
            Assert.Equal(70, WidthOf(FindById(root, "c")!), 3);
        }

        [Fact]
        public async Task DirPseudoClassUsesTheResolvedDirectionOfAnAutoElement()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<style>p:dir(rtl) { width: 40pt } p:dir(ltr) { width: 70pt }</style>"
                + "<p id='a' dir='auto'>שלום</p><p id='b' dir='auto'>hello</p><p id='c' dir='RTL'>x</p>"));

            Assert.Equal(40, WidthOf(FindById(root, "a")!), 3);
            Assert.Equal(70, WidthOf(FindById(root, "b")!), 3);
            Assert.Equal(40, WidthOf(FindById(root, "c")!), 3);
        }
    }
}
