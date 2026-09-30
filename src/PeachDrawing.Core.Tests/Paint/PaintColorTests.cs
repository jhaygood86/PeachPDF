namespace PeachDrawing.Core.Tests.Paint
{
    public class PaintColorTests
    {
        [Fact]
        public void FromArgb_WithFourComponents_SetsAllChannelsAndIsNotCmyk()
        {
            var color = PaintColor.FromArgb(128, 10, 20, 30);

            Assert.Equal(128, color.A);
            Assert.Equal(10, color.R);
            Assert.Equal(20, color.G);
            Assert.Equal(30, color.B);
            Assert.False(color.IsCmyk);
        }

        [Fact]
        public void FromArgb_WithThreeComponents_DefaultsToFullyOpaque()
        {
            var color = PaintColor.FromArgb(10, 20, 30);

            Assert.Equal(255, color.A);
        }

        [Fact]
        public void FromCmyk_SetsTheCmykChannelsAndIsCmyk()
        {
            var color = PaintColor.FromCmyk(255, 0.1f, 0.2f, 0.3f, 0.4f);

            Assert.True(color.IsCmyk);
            Assert.Equal(0.1f, color.C);
            Assert.Equal(0.2f, color.M);
            Assert.Equal(0.3f, color.Y);
            Assert.Equal(0.4f, color.K);
        }

        [Fact]
        public void FromCmyk_ClampsComponentsToTheZeroToOneRange()
        {
            var color = PaintColor.FromCmyk(255, -1f, 2f, 0.5f, 0.5f);

            Assert.Equal(0f, color.C);
            Assert.Equal(1f, color.M);
        }

        [Fact]
        public void FromCmyk_ColorDoesNotExposeMeaningfulRgbComponents()
        {
            // A CMYK-tagged color's low 24 bits are never populated - IsCmyk is the caller's signal
            // to read C/M/Y/K instead of R/G/B, not a promise that R/G/B hold a converted value.
            var color = PaintColor.FromCmyk(255, 0.1f, 0.2f, 0.3f, 0.4f);

            Assert.Equal(0, color.R);
            Assert.Equal(0, color.G);
            Assert.Equal(0, color.B);
        }

        [Fact]
        public void Equality_ComparesArgbComponentsWithinTolerance()
        {
            var a = PaintColor.FromArgb(255, 10, 20, 30);
            var b = PaintColor.FromArgb(255, 10, 20, 30);
            var c = PaintColor.FromArgb(255, 10, 20, 31);

            Assert.Equal(a, b);
            Assert.NotEqual(a, c);
        }

        [Fact]
        public void WellKnownColors_HaveTheExpectedArgbValues()
        {
            Assert.Equal(PaintColor.FromArgb(255, 0, 0, 0), PaintColor.Black);
            Assert.Equal(PaintColor.FromArgb(255, 255, 255, 255), PaintColor.White);
            Assert.Equal(0, PaintColor.Transparent.A);
        }

        [Fact]
        public void ToRgb_OnAnRgbColor_ReturnsItsOwnComponents()
        {
            var color = PaintColor.FromArgb(255, 10, 20, 30);

            var (r, g, b) = color.ToRgb();

            Assert.Equal(10, r);
            Assert.Equal(20, g);
            Assert.Equal(30, b);
        }

        [Fact]
        public void ToRgb_OnFullBlackCmyk_ReturnsBlack()
        {
            var color = PaintColor.FromCmyk(255, 0f, 0f, 0f, 1f);

            var (r, g, b) = color.ToRgb();

            Assert.Equal(0, r);
            Assert.Equal(0, g);
            Assert.Equal(0, b);
        }

        [Fact]
        public void ToRgb_OnNoInkCmyk_ReturnsWhite()
        {
            var color = PaintColor.FromCmyk(255, 0f, 0f, 0f, 0f);

            var (r, g, b) = color.ToRgb();

            Assert.Equal(255, r);
            Assert.Equal(255, g);
            Assert.Equal(255, b);
        }

        [Fact]
        public void ToRgb_OnPureCyan_ReturnsCyanRgb()
        {
            var color = PaintColor.FromCmyk(255, 1f, 0f, 0f, 0f);

            var (r, g, b) = color.ToRgb();

            Assert.Equal(0, r);
            Assert.Equal(255, g);
            Assert.Equal(255, b);
        }
    }
}
