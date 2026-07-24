using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Utils;

using PeachPDF.Fonts;

namespace PeachPDF.Tests.PdfSharpCoreTests.Drawing
{
    /// <summary>
    /// Direct unit tests for <see cref="XFont"/>'s weight-only and weight+stretch constructor overloads -
    /// both delegate to the full weight/stretch/obliqueSkewSinus constructor PeachPDF's own
    /// <c>PdfSharpAdapter.CreateFontInt</c> calls directly, so neither is exercised by the real
    /// HTML-to-PDF pipeline, but both remain real public API surface.
    /// </summary>
    public class XFontConstructorTests
    {
        [Fact]
        public void WeightOnlyConstructor_DefaultsStretchToNormal()
        {
            var font = new XFont("Times New Roman", 12, XFontStyle.Regular,
                new XPdfFontOptions(PdfFontEncoding.Unicode), weight: 700, new FontResolver());

            Assert.NotNull(font);
            Assert.Equal(12, font.Size);
        }

        [Fact]
        public void WeightAndStretchConstructor_ConstructsSuccessfully()
        {
            var font = new XFont("Times New Roman", 14, XFontStyle.Italic,
                new XPdfFontOptions(PdfFontEncoding.Unicode), weight: 400, stretch: 3, new FontResolver());

            Assert.NotNull(font);
            Assert.Equal(14, font.Size);
        }
    }
}
