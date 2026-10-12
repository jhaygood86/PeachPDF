using PeachDrawing.Text;
using PeachDrawing.Text.Shaping;

namespace PeachDrawing.Core.Tests.Paint
{
    /// <summary>
    /// <see cref="TextMeasurement.Measure"/>: a plain run is shaped as given, and text with line feeds, tabs or other control
    /// characters is measured line by line. Both have to say the same thing about the same text.
    /// </summary>
    public class TextMeasurementTests
    {
        private const double Size = 20;

        private static readonly Typeface Face = LoadTypeface();

        private static Typeface LoadTypeface()
        {
            var fontSet = new FontSet();
            var data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "SourceSans3-Regular.ttf"));
            var family = fontSet.AddData(data);
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        private static Size Measure(string text, SyntheticStyle style = SyntheticStyle.None) =>
            TextMeasurement.Measure(text, Face, Size, style, ShapeSettings.Default);

        private static double LineHeight => (Face.Metrics.CellAscent + Face.Metrics.CellDescent) * Size / Face.Metrics.UnitsPerEm;

        private static double LineGap =>
            (Face.Metrics.LineSpacing - Face.Metrics.CellAscent - Face.Metrics.CellDescent) * Size / Face.Metrics.UnitsPerEm;

        /// <summary>The width worked out independently: every advance and kerning delta of the shaped run, rounded one by one.</summary>
        private static double ExpectedWidth(string text)
        {
            var width = 0;
            foreach (var glyph in Shaper.Shape(Face, text, ShapeSettings.Default).Glyphs)
                width += (int)Math.Round(Face.GetAdvance((ushort)glyph.GlyphIndex) + glyph.XAdvanceDelta);

            return width * Size / Face.Metrics.UnitsPerEm;
        }

        [Theory]
        [InlineData("")]
        [InlineData("a")]
        [InlineData("Hello")]
        [InlineData("Hello, world")]
        [InlineData("AVAVAV To")]
        [InlineData("fi fl ffi")]
        public void APlainRun_IsAsWideAsItsShapedGlyphs(string text)
        {
            var size = Measure(text);

            Assert.Equal(ExpectedWidth(text), size.Width, 9);
            Assert.Equal(LineHeight, size.Height, 9);
        }

        [Fact]
        public void KerningIsPartOfTheMeasuredWidth()
        {
            // "AV" is kerned: the run is narrower than its two letters' advances laid end to end.
            var kerned = Measure("AV").Width;
            var separate = Measure("A").Width + Measure("V").Width;

            Assert.True(kerned < separate, $"{kerned} should be narrower than {separate}");
        }

        [Fact]
        public void ATabIsMeasuredAsASpace()
        {
            Assert.Equal(Measure("a b").Width, Measure("a\tb").Width, 9);
        }

        [Fact]
        public void OtherControlCharactersAreDropped()
        {
            Assert.Equal(Measure("ab").Width, Measure("a\u0001b").Width, 9);
        }

        [Fact]
        public void ALineFeedStartsAnotherLine_AndTheWidthIsTheWidestLine()
        {
            var size = Measure("ab\ncdef");

            Assert.Equal(Measure("cdef").Width, size.Width, 9);
            Assert.Equal(2 * LineHeight + LineGap, size.Height, 9);
        }

        [Fact]
        public void ATrailingLineFeedAddsNoLine()
        {
            var plain = Measure("ab");
            var trailing = Measure("ab\n");

            Assert.Equal(plain.Width, trailing.Width, 9);
            Assert.Equal(plain.Height, trailing.Height, 9);
        }

        [Fact]
        public void AnEmptyLineBetweenTwoLfs_CountsAsALine()
        {
            Assert.Equal(3 * LineHeight + 2 * LineGap, Measure("a\n\nb").Height, 9);
        }

        [Fact]
        public void ALoneSurrogate_IsMeasuredAsTheReplacementCharacter()
        {
            Assert.Equal(Measure("a�b").Width, Measure("a\uD800b").Width, 9);
        }

        [Fact]
        public void ASurrogatePair_IsMeasuredAsOneCharacter()
        {
            var size = Measure("a\U0001F600b");

            Assert.Equal(ExpectedWidth("a\U0001F600b"), size.Width, 9);
            Assert.Equal(LineHeight, size.Height, 9);
        }

        [Fact]
        public void BoldSynthesis_WidensByTwoPercentOfTheSizePerCharacter()
        {
            var plain = Measure("abc").Width;

            Assert.Equal(plain + 3 * Size * TextMeasurement.BoldEmphasis, Measure("abc", SyntheticStyle.Bold).Width, 9);
        }

        [Fact]
        public void BoldSynthesis_DoesNotCountLineFeedsOrDroppedControlCharacters()
        {
            // Two characters survive: the line feed and the control character are not measured.
            var plain = Measure("a\n\u0001b").Width;

            Assert.Equal(plain + 2 * Size * TextMeasurement.BoldEmphasis, Measure("a\n\u0001b", SyntheticStyle.Bold).Width, 9);
        }
    }
}
