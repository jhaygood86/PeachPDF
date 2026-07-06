using PeachPDF.PdfSharpCore.Pdf;
using System;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf
{
    public class PdfStringTests
    {
        [Fact]
        public void DefaultConstructor_ValueIsEmpty()
        {
            var s = new PdfString();

            Assert.Equal("", s.Value);
            Assert.Equal(0, s.Length);
        }

        [Fact]
        public void Constructor_SetsValueAndLength()
        {
            var s = new PdfString("Hello");

            Assert.Equal("Hello", s.Value);
            Assert.Equal(5, s.Length);
        }

        [Fact]
        public void Constructor_WithEncoding_SetsEncoding()
        {
            var s = new PdfString("Hello", PdfStringEncoding.Unicode);

            Assert.Equal(PdfStringEncoding.Unicode, s.Encoding);
            Assert.Equal("Hello", s.Value);
        }

        [Theory]
        [InlineData((int)PdfStringEncoding.RawEncoding)]
        [InlineData((int)PdfStringEncoding.StandardEncoding)]
        [InlineData((int)PdfStringEncoding.PDFDocEncoding)]
        [InlineData((int)PdfStringEncoding.WinAnsiEncoding)]
        [InlineData((int)PdfStringEncoding.MacRomanEncoding)]
        [InlineData((int)PdfStringEncoding.Unicode)]
        public void Constructor_AcceptsAllKnownEncodings(int encodingValue)
        {
            var encoding = (PdfStringEncoding)encodingValue;
            var s = new PdfString("Text", encoding);

            Assert.Equal(encoding, s.Encoding);
        }

        [Fact]
        public void Constructor_UnknownEncoding_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PdfString("Text", (PdfStringEncoding)999));
        }

        [Fact]
        public void HexLiteral_DefaultsToFalse()
        {
            var s = new PdfString("Hello");

            Assert.False(s.HexLiteral);
        }

        [Fact]
        public void ToString_Empty_ReturnsEmptyParentheses()
        {
            Assert.Equal("()", new PdfString().ToString());
        }

        [Fact]
        public void ToString_RawEncoding_WrapsInParentheses()
        {
            var text = new PdfString("Hello").ToString();

            Assert.StartsWith("(", text);
            Assert.EndsWith(")", text);
            Assert.Contains("Hello", text);
        }

        [Fact]
        public void ToStringFromPdfDocEncoded_AsciiRoundTrips()
        {
            var s = new PdfString("Hello");

            Assert.Equal("Hello", s.ToStringFromPdfDocEncoded());
        }

        [Fact]
        public void Constructor_CharAbove255_ThrowsDuringConstruction()
        {
            // The default (raw-encoding) constructor calls CheckRawEncoding, which does
            // `Debug.Assert(s[idx] < 256, ...)` for every character -- so a value containing a
            // character above 255 (like '€', U+20AC) fails during construction itself, not later
            // when calling ToStringFromPdfDocEncoded() as the original upstream test assumed. Real,
            // pre-existing behavior; documented here rather than fixed.
            var ex = Record.Exception(() => new PdfString("H€llo"));

            Assert.NotNull(ex);
        }
    }
}
