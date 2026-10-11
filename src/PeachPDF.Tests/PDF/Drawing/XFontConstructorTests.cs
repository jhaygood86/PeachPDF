using PeachDrawing.Core;
using PeachDrawing.Text;
using PeachPDF.PDF.Drawing;
using PeachPDF.PDF;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.PdfEngineTests.Drawing
{
    /// <summary>
    /// <see cref="XFont"/> is a matched typeface at an em size: the tests build one the way the HTML pipeline does, from a
    /// <see cref="TypefaceMatch"/> that a <see cref="FontSet"/> produced for a weight and width.
    /// </summary>
    public class XFontConstructorTests
    {
        [Fact]
        public void FontFromAMatchedWeight_CarriesTheEmSizeAndStyle()
        {
            var font = TestFonts.Create("Times New Roman", 12, PaintFontStyle.Regular, weight: 700);

            Assert.NotNull(font);
            Assert.Equal(12, font.Size);
            Assert.False(font.Italic);
        }

        [Fact]
        public void FontFromAMatchedWeightAndWidth_ConstructsSuccessfully()
        {
            var font = TestFonts.Create("Times New Roman", 14, PaintFontStyle.Italic, weight: 400, stretch: 3);

            Assert.NotNull(font);
            Assert.Equal(14, font.Size);
            Assert.True(font.Italic);
        }

        [Fact]
        public void Font_KeepsWhatTheMatchSaidHasToBeFaked()
        {
            var fontSet = new FontSet();
            TypefaceFamily family;
            using (var stream = File.OpenRead(BundledFonts.Ttf))
                family = fontSet.AddStream(stream, new AddOptions { FamilyName = "RegularOnly-" + Guid.NewGuid().ToString("N") });

            Assert.True(family.TryMatch(new TypefaceQuery(700, TypefaceQuery.NormalWidth, IsItalic: true), out var match));
            var font = new XFont(12, (PaintFontStyle.Bold | PaintFontStyle.Italic), new XPdfFontOptions(PdfFontEncoding.Unicode), match);

            // The bundled fixture is a regular, upright face, so both bold and italic have to be synthesized.
            Assert.Equal(SyntheticStyle.BoldItalic, match.Synthesis);
            Assert.Equal(SyntheticStyle.BoldItalic, font.Synthesis);
        }
    }
}
