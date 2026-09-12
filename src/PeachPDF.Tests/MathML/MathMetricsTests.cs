using PeachPDF.MathML;
using Xunit;

namespace PeachPDF.Tests.MathML
{
    /// <summary>
    /// Coverage for <see cref="MathMetrics"/>'s fallback path (no <c>MATH</c> table) - the TeX-derived
    /// approximate ratios <see cref="MathLayoutEngine"/> falls back to for a font with no real MATH
    /// table data (see the class's own remarks). The real-font path is already covered end to end by
    /// <c>MathTableTests</c>/<c>MathLayoutIntegrationTests</c> against STIX Two Math.
    /// </summary>
    public class MathMetricsTests
    {
        static MathMetrics NoMathTableMetrics() => new(mathTable: null, unitsPerEm: 1000, pixelsPerPoint: 1.0, resolveFont: null!, graphics: null!);

        [Fact]
        public void ScriptPercentScaleDown_FallsBackToSpecSuggestedDefault()
        {
            var metrics = NoMathTableMetrics();
            Assert.Equal(0.8, metrics.ScriptPercentScaleDown);
            Assert.Equal(0.6, metrics.ScriptScriptPercentScaleDown);
        }

        [Theory]
        [InlineData(10.0)]
        [InlineData(20.0)]
        public void EveryNamedAccessor_ReturnsAPositiveFractionOfSize(double sizePt)
        {
            var metrics = NoMathTableMetrics();

            // Every fallback accessor should return a well-defined positive value proportional to the
            // current size, rather than throwing or returning zero/garbage when there's no real font
            // MathConstants table to read from.
            Assert.True(metrics.AxisHeight(sizePt) > 0);
            Assert.True(metrics.FractionRuleThickness(sizePt) > 0);
            Assert.True(metrics.FractionNumeratorGapMin(sizePt) > 0);
            Assert.True(metrics.FractionDenominatorGapMin(sizePt) > 0);
            Assert.True(metrics.RadicalRuleThickness(sizePt) > 0);
            Assert.True(metrics.RadicalVerticalGap(sizePt) > 0);
            Assert.True(metrics.RadicalExtraAscender(sizePt) > 0);
            Assert.True(metrics.RadicalKernBeforeDegree(sizePt) > 0);
            Assert.True(metrics.RadicalKernAfterDegree(sizePt) < 0); // a negative kern, per spec
            Assert.Equal(60, metrics.RadicalDegreeBottomRaisePercent(sizePt));
            Assert.True(metrics.SubscriptShiftDown(sizePt) > 0);
            Assert.True(metrics.SubscriptTopMax(sizePt) > 0);
            Assert.True(metrics.SuperscriptShiftUp(sizePt) > 0);
            Assert.True(metrics.SubSuperscriptGapMin(sizePt) > 0);
            Assert.True(metrics.StretchStackGapAboveMin(sizePt) > 0);
            Assert.True(metrics.StackGapMin(sizePt) > 0);
            Assert.True(metrics.ThickMathSpace(sizePt) > 0);
        }

        [Fact]
        public void Length_ConvertsEveryUnit()
        {
            var metrics = NoMathTableMetrics();
            const double sizePt = 10.0;

            Assert.Equal(5.0, metrics.Length(new MathLength(5, MathLengthUnit.Pt), sizePt));
            Assert.Equal(3.75, metrics.Length(new MathLength(5, MathLengthUnit.Px), sizePt));
            Assert.Equal(50.0, metrics.Length(new MathLength(5, MathLengthUnit.Em), sizePt));
            Assert.Equal(25.0, metrics.Length(new MathLength(5, MathLengthUnit.Ex), sizePt));
            Assert.Equal(5.0, metrics.Length(new MathLength(50, MathLengthUnit.Percent), sizePt));
            Assert.Equal(360, metrics.Length(new MathLength(5, MathLengthUnit.In), sizePt));
            Assert.True(metrics.Length(new MathLength(5, MathLengthUnit.Cm), sizePt) > 0);
            Assert.True(metrics.Length(new MathLength(5, MathLengthUnit.Mm), sizePt) > 0);
            Assert.Equal(60, metrics.Length(new MathLength(5, MathLengthUnit.Pc), sizePt));
            Assert.True(metrics.Length(new MathLength(18, MathLengthUnit.Mu), sizePt) > 0);
        }
    }
}
