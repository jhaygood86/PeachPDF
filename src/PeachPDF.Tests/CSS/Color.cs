
namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using Xunit;

    public class ColorTests
    {
        [Fact]
        public void TestMethod1()
        {
            var parser = new StylesheetParser();
            parser.Parse("body .class #item:hover{ background-color: red;}");
        }
        [Fact]
        public void ColorInvalidHexDigitString()
        {
            var color = "BCDEFG";
            var result = Color.TryFromHex(color, out _);
            Assert.False(result);
        }

        [Fact]
        public void ColorValidFourLetterString()
        {
            var color = "abcd";
            var result = Color.TryFromHex(color, out Color hc);
            Assert.Equal(new Color(170, 187, 204, 221), hc);
            Assert.True(result);
        }

        [Fact]
        public void ColorInvalidLengthString()
        {
            var color = "abcde";
            var result = Color.TryFromHex(color, out _);
            Assert.False(result);
        }

        [Fact]
        public void ColorValidLengthShortString()
        {
            var color = "fff";
            var result = Color.TryFromHex(color, out _);
            Assert.True(result);
        }

        [Fact]
        public void ColorValidLengthLongString()
        {
            var color = "fffabc";
            var result = Color.TryFromHex(color, out _);
            Assert.True(result);
        }

        [Fact]
        public void ColorWhiteShortString()
        {
            var color = "fff";
            var result = Color.FromHex(color);
            Assert.Equal(Color.FromRgb(255, 255, 255), result);
        }

        [Fact]
        public void ColorRedShortString()
        {
            var color = "f00";
            var result = Color.FromHex(color);
            Assert.Equal(Color.FromRgb(255, 0, 0), result);
        }

        [Fact]
        public void ColorFromRedName()
        {
            var color = "red";
            var result = Color.FromName(color);
            Assert.True(result.HasValue);
            Assert.Equal(Color.Red, result);
        }

        [Fact]
        public void ColorFromWhiteName()
        {
            var color = "white";
            var result = Color.FromName(color);
            Assert.True(result.HasValue);
            Assert.Equal(Color.White, result);
        }

        [Fact]
        public void ColorFromUnknownName()
        {
            var color = "bla";
            var result = Color.FromName(color);
            Assert.False(result.HasValue);
        }

        [Fact]
        public void ColorMixedLongString()
        {
            var color = "facc36";
            var result = Color.FromHex(color);
            Assert.Equal(Color.FromRgb(250, 204, 54), result);
        }

        [Fact]
        public void ColorMixedEightDigitLongStringTransparent()
        {
            var color = "facc3600";
            var result = Color.FromHex(color);
            Assert.Equal(Color.FromRgba(250, 204, 54, 0), result);
        }

        [Fact]
        public void ColorMixedEightDigitLongStringOpaque()
        {
            var color = "facc36ff";
            var result = Color.FromHex(color);
            Assert.Equal(Color.FromRgba(250, 204, 54, 1), result);
        }

        [Fact]
        public void ColorMixBlackOnWhite50Percent()
        {
            var color1 = Color.Black;
            var color2 = Color.White;
            var mix = Color.Mix(0.5, color1, color2);
            Assert.Equal(Color.FromRgb(127, 127, 127), mix);
        }

        [Fact]
        public void ColorMixRedOnWhite75Percent()
        {
            var color1 = Color.Red;
            var color2 = Color.White;
            var mix = Color.Mix(0.75, color1, color2);
            Assert.Equal(Color.FromRgb(255, 63, 63), mix);
        }

        [Fact]
        public void ColorMixBlueOnWhite10Percent()
        {
            var color1 = Color.Blue;
            var color2 = Color.White;
            var mix = Color.Mix(0.1, color1, color2);
            Assert.Equal(Color.FromRgb(229, 229, 255), mix);
        }

        [Fact]
        public void ColorMixGreenOnRed30Percent()
        {
            var color1 = Color.PureGreen;
            var color2 = Color.Red;
            var mix = Color.Mix(0.3, color1, color2);
            Assert.Equal(Color.FromRgb(178, 76, 0), mix);
        }

        [Fact]
        public void ColorMixWhiteOnBlack20Percent()
        {
            var color1 = Color.White;
            var color2 = Color.Black;
            var mix = Color.Mix(0.2, color1, color2);
            Assert.Equal(Color.FromRgb(51, 51, 51), mix);
        }

        [Fact]
        public void ColorHslBlackMixed()
        {
            var color = Color.FromHsl(0, 1, 0);
            Assert.Equal(Color.Black, color);
        }

        [Fact]
        public void ColorHslBlackMixed1()
        {
            var color = Color.FromHsl(0, 1, 0);
            Assert.Equal(Color.Black, color);
        }

        [Fact]
        public void ColorHslBlackMixed2()
        {
            var color = Color.FromHsl(0.5f, 1, 0);
            Assert.Equal(Color.Black, color);
        }

        [Fact]
        public void ColorHslRedPure()
        {
            var color = Color.FromHsl(0, 1, 0.5f);
            Assert.Equal(Color.Red, color);
        }

        [Fact]
        public void ColorHslGreenPure()
        {
            var color = Color.FromHsl(1f / 3f, 1, 0.5f);
            Assert.Equal(Color.PureGreen, color);
        }

        [Fact]
        public void ColorHslBluePure()
        {
            var color = Color.FromHsl(2f / 3f, 1, 0.5f);
            Assert.Equal(Color.Blue, color);
        }

        [Fact]
        public void ColorHslBlackPure()
        {
            var color = Color.FromHsl(0, 0, 0);
            Assert.Equal(Color.Black, color);
        }

        [Fact]
        public void ColorHslMagentaPure()
        {
            var color = Color.FromHsl(300f / 360f, 1, 0.5f);
            Assert.Equal(Color.Magenta, color);
        }

        [Fact]
        public void ColorHslYellowGreenMixed()
        {
            var color = Color.FromHsl(1f / 4f, 0.75f, 0.63f);
            Assert.Equal(Color.FromRgb(161, 231, 90), color);
        }

        [Fact]
        public void ColorHslGrayBlueMixed()
        {
            var color = Color.FromHsl(210f / 360f, 0.25f, 0.25f);
            Assert.Equal(Color.FromRgb(48, 64, 80), color);
        }

        [Fact]
        public void ColorFlexHexOneLetter()
        {
            var color = Color.FromFlexHex("F");
            Assert.Equal(Color.FromRgb(0xf, 0x0, 0x0), color);
        }

        [Fact]
        public void ColorFlexHexTwoLetters()
        {
            var color = Color.FromFlexHex("0F");
            Assert.Equal(Color.FromRgb(0x0, 0xf, 0x0), color);
        }

        [Fact]
        public void ColorFlexHexFourLetters()
        {
            var color = Color.FromFlexHex("0F0F");
            Assert.Equal(Color.FromRgb(0xf, 0xf, 0x0), color);
        }

        [Fact]
        public void ColorFlexHexSevenLetters()
        {
            var color = Color.FromFlexHex("0F0F0F0");
            Assert.Equal(Color.FromRgb(0xf, 0xf0, 0x0), color);
        }

        [Fact]
        public void ColorFlexHexFifteenLetters()
        {
            var color = Color.FromFlexHex("1234567890ABCDE");
            Assert.Equal(Color.FromRgb(0x12, 0x67, 0xab), color);
        }

        [Fact]
        public void ColorFlexHexExtremelyLong()
        {
            var color = Color.FromFlexHex("1234567890ABCDE1234567890ABCDE");
            Assert.Equal(Color.FromRgb(0x34, 0xcd, 0x89), color);
        }

        [Fact]
        public void ColorFlexHexRandomString()
        {
            var color = Color.FromFlexHex("6db6ec49efd278cd0bc92d1e5e072d68");
            Assert.Equal(Color.FromRgb(0x6e, 0xcd, 0xe0), color);
        }

        [Fact]
        public void ColorFlexHexSixLettersInvalid()
        {
            var color = Color.FromFlexHex("zqbttv");
            Assert.Equal(Color.FromRgb(0x0, 0xb0, 0x0), color);
        }

        [Fact]
        public void ColorFromGraySimple()
        {
            var color = Color.FromGray(25);
            Assert.Equal(Color.FromRgb(25, 25, 25), color);
        }

        [Fact]
        public void ColorFromGrayWithAlpha()
        {
            var color = Color.FromGray(25, 0.5f);
            Assert.Equal(Color.FromRgba(25, 25, 25, 0.5f), color);
        }

        [Fact]
        public void ColorFromGrayPercent()
        {
            var color = Color.FromGray(0.5f, 0.5f);
            Assert.Equal(Color.FromRgba(128, 128, 128, 0.5f), color);
        }

        [Fact]
        public void ColorFromHwbRed()
        {
            var color = Color.FromHwb(0f, 0.2f, 0.2f);
            Assert.Equal(Color.FromRgb(204, 51, 51), color);
        }

        [Fact]
        public void ColorFromHwbGreen()
        {
            var color = Color.FromHwb(1f / 3f, 0.2f, 0.6f);
            Assert.Equal(Color.FromRgb(51, 102, 51), color);
        }

        [Fact]
        public void ColorFromHwbMagentaTransparent()
        {
            var color = Color.FromHwba(5f / 6f, 0.4f, 0.2f, 0.5f);
            Assert.Equal(Color.FromRgba(204, 102, 204, 0.5f), color);
        }
    }
}







