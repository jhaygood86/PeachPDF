using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.Advanced;
using PeachPDF.Tests.TestSupport;
using Xunit;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf.Advanced
{
    /// <summary>
    /// Direct unit coverage for <see cref="PdfColorConversionGuard"/> edge cases not reachable through a
    /// full HTML render (see <c>PdfColorConversionTests</c> for the end-to-end coverage) - specifically,
    /// that a <see cref="XColorSpace.GrayScale"/>-tagged color (never produced by any CSS construct, but
    /// a real <see cref="XColor"/> state) passes through unconverted, matching this guard's own doc
    /// comment.
    /// </summary>
    public class PdfColorConversionGuardTests
    {
        [Fact]
        public void ApplyConversion_GrayScaleColor_LeftUnchanged()
        {
            var document = new PdfDocument();
            document.Options.ColorOptions = new ColorOptions
            {
                ConversionMode = ColorConversionMode.ConvertToProfile,
                ConvertToProfile = IccProfileFixture.BuildCmykProfileWithReverseTransform(),
            };

            var gray = XColor.FromGrayScale(0.5);

            var result = PdfColorConversionGuard.ApplyConversion(document, gray);

            Assert.Equal(XColorSpace.GrayScale, result.ColorSpace);
            Assert.Equal(gray.GS, result.GS);
        }
    }
}
