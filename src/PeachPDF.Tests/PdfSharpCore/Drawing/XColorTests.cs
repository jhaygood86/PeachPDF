using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.PdfSharpCoreTests.Drawing
{
    public class XColorTests
    {
        [Fact]
        public void FromArgb_RedGreenBlue_SetsOpaqueColor()
        {
            var color = XColor.FromArgb(10, 20, 30);

            Assert.Equal(1, color.A);
            Assert.Equal(10, color.R);
            Assert.Equal(20, color.G);
            Assert.Equal(30, color.B);
        }

        [Fact]
        public void FromArgb_WithAlpha_SetsAlphaChannel()
        {
            var color = XColor.FromArgb(128, 10, 20, 30);

            Assert.Equal(128 / 255.0, color.A, precision: 6);
            Assert.Equal(10, color.R);
            Assert.Equal(20, color.G);
            Assert.Equal(30, color.B);
        }

        [Fact]
        public void FromArgb_OutOfRangeComponent_Throws()
        {
            Assert.Throws<ArgumentException>(() => XColor.FromArgb(256, 0, 0));
            Assert.Throws<ArgumentException>(() => XColor.FromArgb(-1, 0, 0));
            Assert.Throws<ArgumentException>(() => XColor.FromArgb(0, 256, 0, 0));
        }

        [Fact]
        public void FromArgb_Int32Packed_MatchesComponents()
        {
            int argb = unchecked((int)0xFF10203Fu);

            var color = XColor.FromArgb(argb);

            Assert.Equal(0x10, color.R);
            Assert.Equal(0x20, color.G);
            Assert.Equal(0x3F, color.B);
        }

        [Fact]
        public void FromArgb_UInt32Packed_MatchesComponents()
        {
            var color = XColor.FromArgb(0xFF10203Fu);

            Assert.Equal(0x10, color.R);
            Assert.Equal(0x20, color.G);
            Assert.Equal(0x3F, color.B);
        }

        [Fact]
        public void FromArgb_AlphaOverride_ChangesOnlyAlpha()
        {
            var baseColor = XColor.FromArgb(10, 20, 30);

            var withAlpha = XColor.FromArgb(128, baseColor);

            Assert.Equal(10, withAlpha.R);
            Assert.Equal(20, withAlpha.G);
            Assert.Equal(30, withAlpha.B);
            Assert.Equal(128 / 255.0, withAlpha.A, precision: 6);
        }

        [Fact]
        public void FromCmyk_RoundTripsThroughRgb()
        {
            var color = XColor.FromCmyk(0, 0, 0, 0);

            Assert.Equal(255, color.R);
            Assert.Equal(255, color.G);
            Assert.Equal(255, color.B);
        }

        [Fact]
        public void FromCmyk_WithAlpha_SetsAlpha()
        {
            var color = XColor.FromCmyk(0.5, 0, 0, 0, 0);

            Assert.Equal(0.5, color.A, precision: 6);
        }

        [Fact]
        public void FromCmyk_FullBlack_ProducesBlackRgb()
        {
            var color = XColor.FromCmyk(0, 0, 0, 1);

            Assert.Equal(0, color.R);
            Assert.Equal(0, color.G);
            Assert.Equal(0, color.B);
        }

        [Fact]
        public void FromGrayScale_ProducesNeutralColor()
        {
            var color = XColor.FromGrayScale(0.5);

            Assert.Equal(color.R, color.G);
            Assert.Equal(color.G, color.B);
            Assert.Equal((byte)(0.5 * 255), color.R);
        }

        [Fact]
        public void FromKnownColor_ProducesExpectedRgb()
        {
            var color = XColor.FromKnownColor(XKnownColor.Red);

            Assert.True(color.R > 0);
        }

        [Fact]
        public void FromName_ReturnsEmptyColor()
        {
            var color = XColor.FromName("red");

            Assert.True(color.IsEmpty);
        }

        [Fact]
        public void Empty_IsEmpty()
        {
            Assert.True(XColor.Empty.IsEmpty);
        }

        [Fact]
        public void Equality_ComparesByComponentValues()
        {
            var a = XColor.FromArgb(255, 1, 2, 3);
            var b = XColor.FromArgb(255, 1, 2, 3);
            var c = XColor.FromArgb(255, 1, 2, 4);

            Assert.True(a == b);
            Assert.False(a == c);
            Assert.True(a != c);
            Assert.True(a.Equals(b));
            Assert.True(a.Equals((object)b));
            Assert.False(a.Equals((object)"not a color"));
        }

        [Fact]
        public void GetHashCode_SameForEqualColors()
        {
            var a = XColor.FromArgb(255, 1, 2, 3);
            var b = XColor.FromArgb(255, 1, 2, 3);

            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void ColorSpace_DefaultsToRgbForRgbFactory()
        {
            var color = XColor.FromArgb(1, 2, 3);

            Assert.Equal(XColorSpace.Rgb, color.ColorSpace);
        }

        [Fact]
        public void ColorSpace_SetInvalidValue_Throws()
        {
            var color = XColor.FromArgb(1, 2, 3);

            Assert.Throws<System.ComponentModel.InvalidEnumArgumentException>(() => color.ColorSpace = (XColorSpace)999);
        }

        [Fact]
        public void SettingR_RecalculatesCmykAndGrayscale()
        {
            var color = XColor.FromArgb(0, 0, 0);
            color.R = 255;

            Assert.Equal(XColorSpace.Rgb, color.ColorSpace);
            Assert.Equal(255, color.R);
        }

        [Fact]
        public void SettingCyan_RecalculatesRgb()
        {
            var color = XColor.FromCmyk(0, 0, 0, 0);
            color.C = 1;

            Assert.Equal(XColorSpace.Cmyk, color.ColorSpace);
            Assert.True(color.R < 255);
        }

        [Fact]
        public void SettingGrayScale_RecalculatesRgb()
        {
            var color = XColor.FromArgb(0, 0, 0);
            color.GS = 1;

            Assert.Equal(255, color.R);
            Assert.Equal(255, color.G);
            Assert.Equal(255, color.B);
        }

        [Fact]
        public void ComponentSetters_ClampToValidRange()
        {
            var color = XColor.FromArgb(0, 0, 0);

            color.A = 2;
            Assert.Equal(1, color.A);

            color.A = -1;
            Assert.Equal(0, color.A);

            color.C = 2;
            Assert.Equal(1, color.C);

            color.C = -1;
            Assert.Equal(0, color.C);

            color.GS = 2;
            Assert.Equal(1, color.GS);

            color.GS = -1;
            Assert.Equal(0, color.GS);
        }

        [Fact]
        public void GetHue_Grayscale_ReturnsZero()
        {
            var color = XColor.FromArgb(100, 100, 100);

            Assert.Equal(0, color.GetHue());
        }

        [Fact]
        public void GetHue_PureRed_ReturnsZero()
        {
            var color = XColor.FromArgb(255, 0, 0);

            Assert.Equal(0, color.GetHue(), precision: 1);
        }

        [Fact]
        public void GetHue_PureGreen_Returns120()
        {
            var color = XColor.FromArgb(0, 255, 0);

            Assert.Equal(120, color.GetHue(), precision: 1);
        }

        [Fact]
        public void GetHue_PureBlue_Returns240()
        {
            var color = XColor.FromArgb(0, 0, 255);

            Assert.Equal(240, color.GetHue(), precision: 1);
        }

        [Fact]
        public void GetSaturation_Grayscale_ReturnsZero()
        {
            var color = XColor.FromArgb(100, 100, 100);

            Assert.Equal(0, color.GetSaturation());
        }

        [Fact]
        public void GetSaturation_PureColor_ReturnsOne()
        {
            var color = XColor.FromArgb(255, 0, 0);

            Assert.Equal(1, color.GetSaturation(), precision: 6);
        }

        [Fact]
        public void GetBrightness_White_ReturnsOne()
        {
            var color = XColor.FromArgb(255, 255, 255);

            Assert.Equal(1, color.GetBrightness(), precision: 6);
        }

        [Fact]
        public void GetBrightness_Black_ReturnsZero()
        {
            var color = XColor.FromArgb(0, 0, 0);

            Assert.Equal(0, color.GetBrightness(), precision: 6);
        }

        [Fact]
        public void IsKnownColor_TrueForNamedColor()
        {
            var color = XColor.FromKnownColor(XKnownColor.Red);

            Assert.True(color.IsKnownColor);
        }

        [Fact]
        public void RgbCmykG_RoundTripsAllComponents()
        {
            var color = XColor.FromArgb(128, 10, 20, 30);

            var serialized = color.RgbCmykG;
            var restored = XColor.FromArgb(0, 0, 0);
            restored.RgbCmykG = serialized;

            Assert.Equal(color.R, restored.R);
            Assert.Equal(color.G, restored.G);
            Assert.Equal(color.B, restored.B);
            Assert.Equal(color.A, restored.A, precision: 5);
        }
    }
}
