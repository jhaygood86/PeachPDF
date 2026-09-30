using PeachDrawing.Text;
using PeachPDF.PdfSharpCore.Pdf.Advanced;
using PeachPDF.Tests.TestSupport;
using System.Text;

namespace PeachPDF.Tests.PdfSharpCoreTests
{
    /// <summary>
    /// The PDF-side conversions of a typeface's public metrics: what a PDF font dictionary records has to stay the numbers it
    /// always was, so each is checked against the definition written out from <see cref="Typeface.Metrics"/> and
    /// <see cref="Typeface.GetAdvance(ushort)"/>.
    /// </summary>
    public class PdfTypefaceMetricsTests
    {
        private static string PathOf(string which) => which switch
        {
            "Ttf" => BundledFonts.Ttf,
            "Otf" => BundledFonts.Otf,
            "Math" => BundledFonts.Math,
            _ => BundledFonts.Emoji,
        };

        [Theory]
        [InlineData("Ttf")]
        [InlineData("Otf")]
        [InlineData("Math")]
        [InlineData("Emoji")]
        public void ConversionsFollowTheirDefinitionOverThePublicMetrics(string which)
        {
            var face = TypefaceFixtures.FromFile(PathOf(which));
            int unitsPerEm = face.Metrics.UnitsPerEm;
            Assert.True(unitsPerEm > 0);

            foreach (double value in new double[] { 0, 1, 250, 511.5, 700, -200, 1024, 2048 })
            {
                Assert.Equal((int)Math.Round(value * 1000.0 / unitsPerEm), PdfTypefaceMetrics.DesignUnitsToPdf(face, value));
            }

            bool anyWidth = false;
            for (int glyph = 0; glyph < 400; glyph++)
            {
                // Truncated, not rounded, and untouched when the font is already 1000 units per em.
                int advance = face.GetAdvance((ushort)glyph);
                int expected = unitsPerEm == 1000 ? advance : advance * 1000 / unitsPerEm;
                Assert.Equal(expected, PdfTypefaceMetrics.GlyphWidth(face, glyph));
                anyWidth |= expected > 0;
            }

            Assert.True(anyWidth);
        }

        [Theory]
        [InlineData("Ttf")]
        [InlineData("Otf")]
        [InlineData("Math")]
        [InlineData("Emoji")]
        public void BaseName_DropsTheStyleWordsAndAddsTheStyleSuffix(string which)
        {
            var face = TypefaceFixtures.FromFile(PathOf(which));

            string name = face.FullName;
            foreach (string word in new[] { "bold", "italic" })
            {
                int at = name.IndexOf(word, StringComparison.OrdinalIgnoreCase);
                if (at > 0)
                    name = name.Remove(at, word.Length);
            }

            string suffix = face.IsBold ? (face.IsItalic ? ",BoldItalic" : ",Bold") : (face.IsItalic ? ",Italic" : "");
            Assert.Equal(name.Trim() + suffix, PdfTypefaceMetrics.GetBaseName(face));
        }

        [Fact]
        public void SimpleFontWidths_AgreeWithTheGlyphWidthOfEachCode()
        {
            var face = TypefaceFixtures.FromFile(BundledFonts.Ttf);
            int[] widths = PdfSimpleFontWidths.Compute(face);

            face.TryMapRune(new Rune('A'), out var a);
            Assert.Equal(PdfTypefaceMetrics.GlyphWidth(face, a), widths['A']);
            Assert.True(widths['A'] > 0);
        }
    }
}
