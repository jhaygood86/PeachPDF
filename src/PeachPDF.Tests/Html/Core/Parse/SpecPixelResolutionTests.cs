using PeachPDF.CSS;
using PeachPDF.Html.Core.Parse;

namespace PeachPDF.Tests.Html.Core.Parse
{
    /// <summary>
    /// Issue #150: CSS px resolves spec-correctly (1px = 1/96in = 0.75pt, CSS Values &amp; Units
    /// §6.2) through the single shared conversion (<see cref="Length.PointsPerPx"/> in
    /// <see cref="Length.ToPixels"/>) — for bare lengths and for px leaves inside calc(), with no
    /// font-only special case (the old <c>fontAdjust</c> dual convention is gone: font sizes and
    /// every other length agree by construction).
    /// </summary>
    public class SpecPixelResolutionTests
    {
        [Theory]
        [InlineData("96px", 72.0)]
        [InlineData("1px", 0.75)]
        [InlineData("8px", 6.0)]     // the UA default body margin
        [InlineData("72pt", 72.0)]   // pt is the identity: internal layout units are points
        [InlineData("1in", 72.0)]
        [InlineData("25.4mm", 72.0)]
        public void ParseLength_AbsoluteUnits_ResolveToPoints(string length, double expected)
        {
            var result = CssValueParser.ParseLength(length, 0, 0, 0, null, false);
            Assert.Equal(expected, result, 3);
        }

        [Theory]
        [InlineData("calc(96px)", 72.0)]
        [InlineData("calc(48px + 36pt)", 72.0)]
        [InlineData("calc(96px * 2)", 144.0)]
        [InlineData("min(96px, 100pt)", 72.0)]
        public void ParseLength_CalcWithPxLeaves_UsesSameSharedConversion(string length, double expected)
        {
            var result = CssValueParser.ParseLength(length, 0, 0, 0, null, false);
            Assert.Equal(expected, result, 3);
        }

        [Fact]
        public void PointsPerPx_IsTheSpecRatio()
        {
            Assert.Equal(0.75, Length.PointsPerPx, 10);
        }

        [Fact]
        public void FontSizeResolution_PxIsUnchangedFromTheOldFontAdjustPath()
        {
            // Font-size px used the 72/96 factor before the unification (via the old fontAdjust
            // flag) — the same input must keep resolving to the same value now that the factor is
            // unconditional: 16px -> 12pt.
            var resolved = PeachPDF.Html.Core.Utils.FontSizeResolver.Resolve("16px", 11, 11);
            Assert.Equal(12.0, resolved, 3);
        }
    }
}
