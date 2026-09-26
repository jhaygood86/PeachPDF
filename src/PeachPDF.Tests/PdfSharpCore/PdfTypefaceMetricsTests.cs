using PeachDrawing.Text;
using PeachPDF.PdfSharpCore.Pdf.Advanced;
using PeachPDF.Tests.TestSupport;
using System.Text;

namespace PeachPDF.Tests.PdfSharpCoreTests
{
    /// <summary>
    /// The PDF-side conversions that used to be the engine descriptor's, against the engine's own originals, which are still
    /// there: what a PDF font dictionary records has to be the same numbers it always was.
    /// </summary>
    public class PdfTypefaceMetricsTests
    {
        [Theory]
        [InlineData("Ttf")]
        [InlineData("Otf")]
        [InlineData("Math")]
        [InlineData("Emoji")]
        public void ConversionsMatchTheEnginesOriginals(string which)
        {
            string path = which switch
            {
                "Ttf" => BundledFonts.Ttf,
                "Otf" => BundledFonts.Otf,
                "Math" => BundledFonts.Math,
                _ => BundledFonts.Emoji,
            };
            var face = TypefaceFixtures.FromFile(path);
            var descriptor = face.Face.Descriptor;

            foreach (double value in new double[] { 0, 1, 250, 511.5, 700, -200, 1024, 2048 })
            {
                Assert.Equal(descriptor.DesignUnitsToPdf(value), PdfTypefaceMetrics.DesignUnitsToPdf(face, value));
            }

            for (int glyph = 0; glyph < 400; glyph++)
            {
                Assert.Equal(descriptor.GlyphIndexToPdfWidth(glyph), PdfTypefaceMetrics.GlyphWidth(face, glyph));
            }

            Assert.Equal(face.Face.GetBaseName(), PdfTypefaceMetrics.GetBaseName(face));
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
