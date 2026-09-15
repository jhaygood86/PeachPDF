using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Parse;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// CSS Color 5 <c>device-cmyk()</c> (https://www.w3.org/TR/css-color-5/#device-cmyk) grammar and
    /// native-CMYK resolution (issue #1083) - the color is carried through as real CMYK components
    /// (<see cref="RColor.IsCmyk"/>/<see cref="RColor.C"/>/<see cref="RColor.M"/>/<see cref="RColor.Y"/>/
    /// <see cref="RColor.K"/>), never collapsed to a naive sRGB approximation. See
    /// <c>DeviceCmykColorIntegrationTests</c> for the end-to-end HTML -&gt; PDF operator coverage.
    /// </summary>
    public class DeviceCmykColorParsingTests
    {
        private static CssValueParser Parser() => new(new PdfSharpAdapter());

        [Fact]
        public void FourNumbers_ResolvesToCmykComponents()
        {
            var parser = Parser();
            var color = parser.GetActualColor("device-cmyk(0 0.5 1 0.25)");

            Assert.True(color.IsCmyk);
            Assert.Equal(0f, color.C);
            Assert.Equal(0.5f, color.M, 3);
            Assert.Equal(1f, color.Y, 3);
            Assert.Equal(0.25f, color.K, 3);
            Assert.Equal(255, color.A);
        }

        [Fact]
        public void PercentageComponents_NormalizeTo01()
        {
            var color = Parser().GetActualColor("device-cmyk(0% 50% 100% 25%)");

            Assert.True(color.IsCmyk);
            Assert.Equal(0f, color.C);
            Assert.Equal(0.5f, color.M, 3);
            Assert.Equal(1f, color.Y, 3);
            Assert.Equal(0.25f, color.K, 3);
        }

        [Fact]
        public void SlashAlpha_IsApplied()
        {
            var color = Parser().GetActualColor("device-cmyk(0 0 0 0 / 0.5)");

            Assert.True(color.IsCmyk);
            Assert.InRange(color.A, 127, 128);
        }

        [Fact]
        public void SlashAlphaPercentage_IsApplied()
        {
            var color = Parser().GetActualColor("device-cmyk(0 0 0 0 / 50%)");

            Assert.InRange(color.A, 127, 128);
        }

        [Fact]
        public void NoAlpha_DefaultsToOpaque()
        {
            var color = Parser().GetActualColor("device-cmyk(0.1 0.2 0.3 0.4)");

            Assert.Equal(255, color.A);
        }

        [Fact]
        public void NoneComponents_ResolveToZero()
        {
            var color = Parser().GetActualColor("device-cmyk(none none none none)");

            Assert.True(color.IsCmyk);
            Assert.Equal(0f, color.C);
            Assert.Equal(0f, color.M);
            Assert.Equal(0f, color.Y);
            Assert.Equal(0f, color.K);
        }

        [Fact]
        public void OutOfRangeComponents_AreClamped()
        {
            var color = Parser().GetActualColor("device-cmyk(2 -1 1.5 0.5)");

            Assert.Equal(1f, color.C);
            Assert.Equal(0f, color.M);
            Assert.Equal(1f, color.Y);
            Assert.Equal(0.5f, color.K, 3);
        }

        [Theory]
        [InlineData("device-cmyk(0 0 0)")]            // only 3 components
        [InlineData("device-cmyk(0 0 0 0 0)")]         // 5 components, no slash
        [InlineData("device-cmyk()")]                  // empty
        [InlineData("device-cmyk(red 0 0 0)")]          // non-numeric component
        public void MalformedForms_AreRejected(string value)
        {
            var parser = Parser();
            Assert.False(parser.IsColorValid(value));
        }

        [Fact]
        public void ToString_RoundTrips_AsDeviceCmykSyntax()
        {
            // Color.ToString() must re-serialize a device-cmyk() color as device-cmyk() (CSS Color 5
            // syntax), not silently drop it or fall back to rgb() - see Color.IsDeviceCmyk/ToDeviceCmykString.
            var color = PeachPDF.CSS.Color.FromDeviceCmyk(0f, 0.5f, 1f, 0.25f);

            var text = color.ToString();

            Assert.StartsWith("device-cmyk(", text);
            Assert.True(PeachPDF.CSS.Color.TryFromHex("000000", out _)); // sanity: hex path untouched

            var reparsed = Parser().GetActualColor(text);
            Assert.True(reparsed.IsCmyk);
            Assert.Equal(color.C, reparsed.C, 3);
            Assert.Equal(color.M, reparsed.M, 3);
            Assert.Equal(color.Y, reparsed.Y, 3);
            Assert.Equal(color.K, reparsed.K, 3);
        }

        [Fact]
        public void Equality_ComparesCmykComponents_NotJustAlpha()
        {
            var a = PeachPDF.CSS.Color.FromDeviceCmyk(0f, 0.5f, 1f, 0.25f);
            var b = PeachPDF.CSS.Color.FromDeviceCmyk(0.1f, 0.5f, 1f, 0.25f);

            Assert.NotEqual(a, b);
            Assert.Equal(a, PeachPDF.CSS.Color.FromDeviceCmyk(0f, 0.5f, 1f, 0.25f));
        }

        [Fact]
        public void EqualityOperators_CompareCmykComponents()
        {
            var a = PeachPDF.CSS.Color.FromDeviceCmyk(0f, 0.5f, 1f, 0.25f);
            var same = PeachPDF.CSS.Color.FromDeviceCmyk(0f, 0.5f, 1f, 0.25f);
            var different = PeachPDF.CSS.Color.FromDeviceCmyk(1f, 0.5f, 1f, 0.25f);

            Assert.True(a == same);
            Assert.False(a == different);
            Assert.True(a != different);
            Assert.False(a != same);
        }

        [Fact]
        public void CompareTo_DistinguishesCmykColors()
        {
            var a = (System.IComparable<PeachPDF.CSS.Color>)PeachPDF.CSS.Color.FromDeviceCmyk(0f, 0.5f, 1f, 0.25f);
            var same = PeachPDF.CSS.Color.FromDeviceCmyk(0f, 0.5f, 1f, 0.25f);
            var different = PeachPDF.CSS.Color.FromDeviceCmyk(1f, 0.5f, 1f, 0.25f);
            var rgb = PeachPDF.CSS.Color.FromRgb(255, 0, 0);

            Assert.Equal(0, a.CompareTo(same));
            Assert.NotEqual(0, a.CompareTo(different));
            Assert.NotEqual(0, a.CompareTo(rgb));
        }

        [Fact]
        public void GetHashCode_DiffersForDifferentCmykColors()
        {
            var a = PeachPDF.CSS.Color.FromDeviceCmyk(0f, 0.5f, 1f, 0.25f);
            var different = PeachPDF.CSS.Color.FromDeviceCmyk(1f, 0.5f, 1f, 0.25f);

            Assert.NotEqual(a.GetHashCode(), different.GetHashCode());
        }

        // ── color-mix() between two device-cmyk() operands ─────────────────────────
        // CSS Color 5 has no "cmyk" interpolation space and no defined behavior for a device-cmyk()
        // operand without a real ICC profile - this project extends color-mix() to mix two CMYK-tagged
        // operands directly in C/M/Y/K space instead (see Color.MixCmyk's remarks). A mix of one CMYK and
        // one non-CMYK operand still has no defined conversion and stays invalid.

        [Fact]
        public void ColorMix_TwoDeviceCmykOperands_MixesInCmykSpace()
        {
            var color = Parser().GetActualColor("color-mix(in srgb, device-cmyk(0 1 1 0), device-cmyk(1 0 0 0))");

            Assert.True(color.IsCmyk);
            Assert.Equal(0.5f, color.C, 2);
            Assert.Equal(0.5f, color.M, 2);
            Assert.Equal(0.5f, color.Y, 2);
            Assert.Equal(0f, color.K, 2);
            Assert.Equal(255, color.A);
        }

        [Fact]
        public void ColorMix_TwoDeviceCmykOperands_WeightedPercentage_ShiftsTowardHeavierOperand()
        {
            // 25% device-cmyk(0 1 1 0) + implicit 75% device-cmyk(1 0 0 0) -> 0.75 toward the second.
            var color = Parser().GetActualColor("color-mix(in srgb, device-cmyk(0 1 1 0) 25%, device-cmyk(1 0 0 0))");

            Assert.True(color.IsCmyk);
            Assert.Equal(0.75f, color.C, 2);
            Assert.Equal(0.25f, color.M, 2);
            Assert.Equal(0.25f, color.Y, 2);
        }

        [Fact]
        public void ColorMix_TwoDeviceCmykOperands_SubTotalPercentages_ScaleResultAlpha()
        {
            // 30% + 30% sums to 60% - the CMYK branch applies the same alpha-multiplier normalization as
            // the RGB Color.Mix path.
            var color = Parser().GetActualColor("color-mix(in srgb, device-cmyk(0 1 1 0) 30%, device-cmyk(1 0 0 0) 30%)");

            Assert.True(color.IsCmyk);
            Assert.InRange(color.A, 152, 154); // 0.6 * 255 ≈ 153
        }

        [Fact]
        public void ColorMix_TwoDeviceCmykOperands_DeclaredInSpaceIsIgnored_NotAnError()
        {
            // Neither operand has a meaningful projection into oklab without a real ICC profile - the
            // CMYK branch mixes directly in C/M/Y/K regardless of the declared "in <space>" keyword.
            var inOklab = Parser().GetActualColor("color-mix(in oklab, device-cmyk(0 1 1 0), device-cmyk(1 0 0 0))");
            var inSrgb = Parser().GetActualColor("color-mix(in srgb, device-cmyk(0 1 1 0), device-cmyk(1 0 0 0))");

            Assert.True(inOklab.IsCmyk);
            Assert.Equal(inSrgb.C, inOklab.C, 3);
            Assert.Equal(inSrgb.M, inOklab.M, 3);
            Assert.Equal(inSrgb.Y, inOklab.Y, 3);
            Assert.Equal(inSrgb.K, inOklab.K, 3);
        }

        [Fact]
        public void ColorMix_OneDeviceCmykOneRgbOperand_IsInvalid()
        {
            Assert.False(Parser().IsColorValid("color-mix(in srgb, device-cmyk(0 1 1 0), red)"));
            Assert.False(Parser().IsColorValid("color-mix(in srgb, red, device-cmyk(0 1 1 0))"));
        }
    }
}
