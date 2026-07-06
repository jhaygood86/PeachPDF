using PeachPDF.PdfSharpCore.Pdf;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf
{
    public class PdfRealTests
    {
        [Fact]
        public void DefaultConstructor_ValueIsZero()
        {
            Assert.Equal(0, new PdfReal().Value);
        }

        [Fact]
        public void Constructor_SetsValue()
        {
            Assert.Equal(3.5, new PdfReal(3.5).Value);
        }

        [Fact]
        public void ToString_UsesUpToThreeSignificantDecimalDigits()
        {
            Assert.Equal("3.5", new PdfReal(3.5).ToString());
            Assert.Equal("0", new PdfReal(0).ToString());
        }
    }
}
