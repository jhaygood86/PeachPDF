using PeachPDF.Tests.TestSupport;
using Xunit;

namespace PeachPDF.Tests.Html.Adapters
{
    /// <summary>
    /// <see cref="PeachPDF.Html.Adapters.RFont"/>'s vertical-metrics query surface (issues #770/#775) is
    /// virtual with defaults that reproduce the pre-#770 approximation exactly, so any <see cref="PeachPDF.Html.Adapters.RFont"/>
    /// that doesn't override them (only <see cref="PeachPDF.Adapters.FontAdapter"/>, the product's one
    /// concrete implementation, does) stays behaviorally unchanged. <see cref="TestFont"/> is exactly
    /// such a font - this exercises the base defaults directly, mirroring the CPAL palette section's own
    /// "every non-color font reports no palettes" defaults immediately above them in <c>RFont</c>.
    /// </summary>
    public class RFontVerticalMetricsDefaultsTests
    {
        [Fact]
        public void Defaults_ReproduceThePreExistingApproximation()
        {
            var font = new TestFont(20);
            var rune = new System.Text.Rune('A');

            Assert.False(font.HasVerticalMetrics);
            Assert.Equal(font.Height, font.GetVerticalAdvance(rune));

            Assert.False(font.HasVerticalOrigin);
            Assert.Equal(font.Ascent, font.GetVerticalOriginY(rune));
        }
    }
}
