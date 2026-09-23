using PeachPDF.CSS;
using PeachPDF.Html.Core.Parse;
using System;
using System.Linq;
using Xunit;

namespace PeachPDF.Tests.CSS
{
    /// <summary>
    /// Tests for <see cref="CalcEvaluator"/>'s angle-leaf evaluation: a calc() whose value is an
    /// <c>&lt;angle&gt;</c> (e.g. <c>calc(1turn * 0.35)</c>) evaluates to radians (the canonical angle
    /// unit). This is what lets a conic-gradient stop position be authored as a calc() expression — the
    /// Charts.css pie-slice case, whose every stop is <c>calc(1turn * &lt;value&gt;)</c>.
    /// </summary>
    public class CalcEvaluatorTests
    {
        private static double? EvaluateAngle(string calc)
        {
            var function = CssValueParser.GetCssTokens(calc).Single(t => t.Type == TokenType.Function);
            var node = CalcParser.Parse(function);
            Assert.NotNull(node);
            // A full turn is 2π radians; em/rem factors are irrelevant to an angle calc.
            return CalcEvaluator.Evaluate(node!, new CalcContext(2.0 * Math.PI, 0, 0));
        }

        private sealed class DoubledCh : IFontMetricSource
        {
            public double GetRatio(FontMetric metric, bool rootElement) => metric == FontMetric.Ch ? 1.0 : 0.5;
        }

        [Fact]
        public void LengthCalc_MeasuredUnitLeaf_ReadsTheContextsFontSource()
        {
            var function = CssValueParser.GetCssTokens("calc(2ch + 1rch + 3lh)").Single(t => t.Type == TokenType.Function);
            var node = CalcParser.Parse(function);
            Assert.NotNull(node);

            // em = 10, rem = 40: 2ch = 2*1.0*10, 1rch = 1*1.0*40, 3lh = 3*0.5*10.
            var context = new CalcContext(0, 10, 40, fonts: new DoubledCh());
            Assert.Equal(20 + 40 + 15, CalcEvaluator.Evaluate(node!, context)!.Value, 6);
        }

        [Fact]
        public void LengthCalc_MeasuredUnitLeafWithNoFont_TakesTheSpecFallback()
        {
            var function = CssValueParser.GetCssTokens("calc(2ch + 1cap)").Single(t => t.Type == TokenType.Function);
            var node = CalcParser.Parse(function);
            Assert.NotNull(node);

            Assert.Equal(2 * 0.5 * 10 + 0.7 * 10, CalcEvaluator.Evaluate(node!, new CalcContext(0, 10, 40))!.Value, 6);
        }

        [Theory]
        [InlineData("calc(1turn * 0.35)", 0.35)]
        [InlineData("calc(1turn * 0.5)", 0.5)]
        [InlineData("calc(0.25turn)", 0.25)]
        public void AngleCalc_TurnScaled_EvaluatesToRadians(string calc, double expectedTurns)
        {
            var radians = EvaluateAngle(calc);
            Assert.NotNull(radians);
            Assert.Equal(expectedTurns * 2.0 * Math.PI, radians!.Value, 4);
        }

        [Fact]
        public void AngleCalc_Degrees_EvaluatesToRadians()
        {
            var radians = EvaluateAngle("calc(90deg + 90deg)");
            Assert.NotNull(radians);
            Assert.Equal(Math.PI, radians!.Value, 4); // 180deg
        }

        // ── <time>/<resolution> calc leaves (issue #229 gap 2) ───────────────────────
        // These node types are produced by CalcParser for @property `<time>`/`<resolution>` validation.
        // No layout property evaluates or serializes a time/resolution calc, so these tests exercise the
        // CalcEvaluator/CalcSerializer/CalcNode leaf handling that path never reaches directly.

        private static double? Evaluate(string calc)
        {
            var function = CssValueParser.GetCssTokens(calc).Single(t => t.Type == TokenType.Function);
            var node = CalcParser.Parse(function);
            Assert.NotNull(node);
            return CalcEvaluator.Evaluate(node!, new CalcContext(0, 0, 0));
        }

        [Theory]
        [InlineData("calc(1s + 2s)", 3000.0)]    // canonical unit is milliseconds
        [InlineData("calc(500ms)", 500.0)]
        [InlineData("calc(2s * 3)", 6000.0)]
        public void TimeCalc_EvaluatesToMilliseconds(string calc, double expectedMs)
        {
            Assert.Equal(expectedMs, Evaluate(calc)!.Value, 3);
        }

        [Theory]
        [InlineData("calc(2dppx * 2)", 4.0)]     // canonical unit is dots-per-pixel
        [InlineData("calc(96dpi)", 1.0)]         // 96dpi == 1dppx
        public void ResolutionCalc_EvaluatesToDotsPerPixel(string calc, double expectedDppx)
        {
            Assert.Equal(expectedDppx, Evaluate(calc)!.Value, 3);
        }

        private static string Serialize(string calc, CalcCategory category)
        {
            var function = CssValueParser.GetCssTokens(calc).Single(t => t.Type == TokenType.Function);
            var node = CalcParser.Parse(function);
            Assert.NotNull(node);
            return CalcSerializer.Serialize(node!, category);
        }

        [Theory]
        [InlineData("calc(1s + 2s)", "time", "calc(1s + 2s)")]
        [InlineData("calc(2dppx * 2)", "resolution", "calc(2dppx * 2)")]
        public void TimeResolutionCalc_SerializesToNormalizedText(string calc, string kind, string expected)
        {
            var category = kind == "time" ? CalcCategory.Time : CalcCategory.Resolution;
            Assert.Equal(expected, Serialize(calc, category));
        }
    }
}
