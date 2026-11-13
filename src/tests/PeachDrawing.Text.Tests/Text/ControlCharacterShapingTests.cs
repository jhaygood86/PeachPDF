using PeachDrawing.Text.Shaping;
using PeachPDF.Tests.TestSupport;
using System.IO;

namespace PeachDrawing.Text.Tests.Text
{
    /// <summary>
    /// A control character is shaped to the font's missing glyph whatever the font's cmap maps it to: css-text-3 requires it to be visible,
    /// and many fonts map the C0 controls to blank glyphs.
    /// </summary>
    public class ControlCharacterShapingTests
    {
        private static Typeface Face() => TypefaceFixtures.FromBytes(File.ReadAllBytes(BundledFonts.Ttf));

        [Theory]
        [InlineData(0x01)]
        [InlineData(0x04)]
        [InlineData(0x1F)]
        [InlineData(0x7F)]
        [InlineData(0x88)]
        public void AControlCharacter_ShapesToTheMissingGlyph(int codepoint)
        {
            var run = Shaper.Shape(Face(), char.ConvertFromUtf32(codepoint), new ShapeSettings());

            Assert.Equal(0, Assert.Single(run.Glyphs).GlyphIndex);
        }

        [Fact]
        public void AnOrdinaryLetter_KeepsItsGlyph()
        {
            var face = Face();
            var run = Shaper.Shape(face, "A", new ShapeSettings());

            Assert.Equal(face.GlyphOf('A'), Assert.Single(run.Glyphs).GlyphIndex);
        }
    }
}
