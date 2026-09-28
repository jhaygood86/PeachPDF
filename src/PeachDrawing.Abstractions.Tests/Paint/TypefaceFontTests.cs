using PeachDrawing.Text;

namespace PeachDrawing.Abstractions.Tests.Paint
{
    public class TypefaceFontTests
    {
        private static Typeface LoadTypeface()
        {
            var fontSet = new FontSet();
            var data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "SourceSans3-Regular.ttf"));
            var family = fontSet.AddData(data);
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        [Fact]
        public void Constructor_ExposesTheGivenTypefaceSizeAndStyle()
        {
            var typeface = LoadTypeface();
            var font = new TypefaceFont(typeface, 24, SyntheticStyle.Bold, obliqueSkewSinus: 0.2);

            Assert.Same(typeface, font.Typeface);
            Assert.Equal(24, font.Size);
            Assert.Equal(SyntheticStyle.Bold, font.SyntheticStyle);
            Assert.Equal(0.2, font.ObliqueSkewSinus);
        }

        [Fact]
        public void Metrics_ScaleLinearlyWithSize()
        {
            var typeface = LoadTypeface();
            var small = new TypefaceFont(typeface, 10);
            var large = new TypefaceFont(typeface, 20);

            // Every metric here is size * (a fixed ratio from the typeface's own design-unit metrics),
            // so doubling the size must exactly double each one.
            Assert.Equal(small.Height * 2, large.Height, 9);
            Assert.Equal(small.Ascent * 2, large.Ascent, 9);
            Assert.Equal(small.NormalLineHeight * 2, large.NormalLineHeight, 9);
            Assert.Equal(small.LeftPadding * 2, large.LeftPadding, 9);
        }

        [Fact]
        public void Height_MatchesTheTypefacesOwnLineSpacingFormula()
        {
            var typeface = LoadTypeface();
            var font = new TypefaceFont(typeface, 16);
            var metrics = typeface.Metrics;

            Assert.Equal(metrics.LineSpacing * 16.0 / metrics.UnitsPerEm, font.Height, 9);
        }

        [Fact]
        public void UnderlineOffset_IsHeightMinusDescentPlusOne()
        {
            var typeface = LoadTypeface();
            var font = new TypefaceFont(typeface, 16);
            var metrics = typeface.Metrics;
            var descent = 16.0 * metrics.CellDescent / metrics.UnitsPerEm;

            Assert.Equal(font.Height - descent + 1, font.UnderlineOffset, 9);
        }

        [Fact]
        public void LeftPadding_IsHeightOverSix()
        {
            var typeface = LoadTypeface();
            var font = new TypefaceFont(typeface, 16);

            Assert.Equal(font.Height / 6, font.LeftPadding, 9);
        }

        [Fact]
        public void HasGlyph_TrueForALetterTheFontActuallyContains()
        {
            var font = new TypefaceFont(LoadTypeface(), 16);

            Assert.True(font.HasGlyph(new System.Text.Rune('A')));
        }

        [Fact]
        public void FaceKey_IsStableForTheSameTypefaceAndSynthesis()
        {
            var typeface = LoadTypeface();
            var a = new TypefaceFont(typeface, 16, SyntheticStyle.Italic);
            var b = new TypefaceFont(typeface, 32, SyntheticStyle.Italic);
            var c = new TypefaceFont(typeface, 16, SyntheticStyle.Bold);

            // Same typeface + synthesis: same key regardless of size (a face key identifies the glyphs,
            // not their scale).
            Assert.Equal(a.FaceKey, b.FaceKey);
            // Different synthesis: different key.
            Assert.NotEqual(a.FaceKey, c.FaceKey);
        }

    }
}
