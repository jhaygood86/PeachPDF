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

        /// <summary>
        /// <see cref="PeachPDF.Html.Adapters.RFont.NormalLineHeight"/> (issue #956) follows the identical
        /// pattern: virtual, with a default that reproduces the pre-#956 flat 1.2x-font-size approximation
        /// exactly, so only <see cref="PeachPDF.Adapters.FontAdapter"/> resolves it from real font metrics.
        /// </summary>
        [Fact]
        public void NormalLineHeightDefault_ReproducesThePreExistingApproximation()
        {
            var font = new TestFont(20);

            Assert.Equal(1.2 * font.Size, font.NormalLineHeight);
        }

        /// <summary>
        /// <see cref="PeachPDF.Html.Adapters.RFont.GetGlyphAdvanceWidthDesignUnits"/> follows the same
        /// pattern as the MATH-table query surface it sits alongside (<c>GetGlyphIndex</c>,
        /// <c>FontUnitsPerEm</c>): a font with no real <c>hmtx</c> data to consult (no descriptor) has
        /// nothing sensible to return, so the default is 0 - <c>MathLayoutEngine</c>'s stretchy-glyph
        /// width resolution falls back to its own pre-existing <c>MeasureString</c> approximation
        /// whenever this comes back non-positive.
        /// </summary>
        [Fact]
        public void GlyphAdvanceWidthDesignUnitsDefault_IsZero()
        {
            var font = new TestFont(20);

            Assert.Equal(0, font.GetGlyphAdvanceWidthDesignUnits(glyphIndex: 42));
        }
    }
}
