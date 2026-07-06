using PeachPDF.PdfSharpCore.Pdf;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf
{
    public class PdfBooleanTests
    {
        [Fact]
        public void DefaultConstructor_ValueIsFalse()
        {
            var value = new PdfBoolean();

            Assert.False(value.Value);
        }

        [Fact]
        public void Constructor_SetsValue()
        {
            Assert.True(new PdfBoolean(true).Value);
            Assert.False(new PdfBoolean(false).Value);
        }

        [Fact]
        public void True_And_False_Constants()
        {
            Assert.True(PdfBoolean.True.Value);
            Assert.False(PdfBoolean.False.Value);
        }

        [Fact]
        public void ToString_ReturnsBooleanLiteral()
        {
            Assert.Equal("True", new PdfBoolean(true).ToString());
            Assert.Equal("False", new PdfBoolean(false).ToString());
        }
    }
}
