using System.Text;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.IO;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf.IO
{
    public class PdfWriterTests
    {
        private static (PdfWriter Writer, MemoryStream Stream) CreateWriter()
        {
            var stream = new MemoryStream();
            var writer = new PdfWriter(stream) { Layout = PdfWriterLayout.Compact };
            return (writer, stream);
        }

        private static string ReadAll(MemoryStream stream)
        {
            return Encoding.ASCII.GetString(stream.ToArray());
        }

        [Fact]
        public void Write_Bool_WritesTrueOrFalse()
        {
            var (writer, stream) = CreateWriter();

            writer.Write(true);

            Assert.Equal("True", ReadAll(stream));
        }

        [Fact]
        public void Write_PdfBoolean_WritesLowercase()
        {
            var (writer, stream) = CreateWriter();

            writer.Write(new PdfBoolean(true));

            Assert.Equal("true", ReadAll(stream));
        }

        [Fact]
        public void Write_Int_WritesInvariantDigits()
        {
            var (writer, stream) = CreateWriter();

            writer.Write(42);

            Assert.Equal("42", ReadAll(stream));
        }

        [Fact]
        public void Write_Long_WritesInvariantDigits()
        {
            var (writer, stream) = CreateWriter();

            writer.Write(9999999999L);

            Assert.Equal("9999999999", ReadAll(stream));
        }

        [Fact]
        public void Write_UInt_WritesInvariantDigits()
        {
            var (writer, stream) = CreateWriter();

            writer.Write(42u);

            Assert.Equal("42", ReadAll(stream));
        }

        [Fact]
        public void Write_PdfInteger_WritesValue()
        {
            var (writer, stream) = CreateWriter();

            writer.Write(new PdfInteger(7));

            Assert.Equal("7", ReadAll(stream));
        }

        [Fact]
        public void Write_PdfLong_WritesValue()
        {
            var (writer, stream) = CreateWriter();

            writer.Write(new PdfLong(123456789012L));

            Assert.Equal("123456789012", ReadAll(stream));
        }

        [Fact]
        public void Write_PdfUInteger_WritesValue()
        {
            var (writer, stream) = CreateWriter();

            writer.Write(new PdfUInteger(99u));

            Assert.Equal("99", ReadAll(stream));
        }

        [Fact]
        public void Write_Double_WritesInvariantDecimal()
        {
            var (writer, stream) = CreateWriter();

            writer.Write(1.5);

            Assert.Equal("1.5", ReadAll(stream));
        }

        [Fact]
        public void Write_PdfReal_WritesInvariantDecimal()
        {
            var (writer, stream) = CreateWriter();

            writer.Write(new PdfReal(2.25));

            Assert.Equal("2.25", ReadAll(stream));
        }

        [Fact]
        public void Write_PdfString_WritesParenthesizedLiteral()
        {
            var (writer, stream) = CreateWriter();

            writer.Write(new PdfString("hi"));

            Assert.Equal("(hi)", ReadAll(stream));
        }

        [Fact]
        public void Write_PdfName_EscapesSpecialCharacters()
        {
            var (writer, stream) = CreateWriter();

            writer.Write(new PdfName("/a#b"));

            Assert.Equal("/a#23b", ReadAll(stream));
        }

        [Fact]
        public void Write_PdfLiteral_WritesRawValue()
        {
            var (writer, stream) = CreateWriter();

            writer.Write(new PdfLiteral("true"));

            Assert.Equal("true", ReadAll(stream));
        }

        [Fact]
        public void Write_PdfRectangle_WritesFourNumbersInBrackets()
        {
            var (writer, stream) = CreateWriter();

            writer.Write(new PdfRectangle(0, 0, 100, 200));

            Assert.Equal("[0 0 100 200]", ReadAll(stream));
        }

        [Fact]
        public void Write_Bytes_WritesRawBytes()
        {
            var (writer, stream) = CreateWriter();

            writer.Write([65, 66, 67]);

            Assert.Equal("ABC", ReadAll(stream));
        }

        [Fact]
        public void Write_EmptyByteArray_WritesNothing()
        {
            var (writer, stream) = CreateWriter();

            writer.Write([]);

            Assert.Equal(0, stream.Length);
        }

        [Fact]
        public void WriteRaw_NullOrEmptyString_WritesNothing()
        {
            var (writer, stream) = CreateWriter();

            writer.WriteRaw((string)null!);
            writer.WriteRaw("");

            Assert.Equal(0, stream.Length);
        }

        [Fact]
        public void WriteRaw_Char_WritesSingleByte()
        {
            var (writer, stream) = CreateWriter();

            writer.WriteRaw('X');

            Assert.Equal("X", ReadAll(stream));
        }

        [Fact]
        public void NewLine_AfterNonNewLine_WritesLineFeed()
        {
            var (writer, stream) = CreateWriter();

            writer.WriteRaw('X');
            writer.NewLine();

            Assert.Equal("X\n", ReadAll(stream));
        }

        [Fact]
        public void NewLine_AfterNewLine_IsNoOp()
        {
            var (writer, stream) = CreateWriter();

            writer.WriteRaw('X');
            writer.NewLine();
            writer.NewLine();

            Assert.Equal("X\n", ReadAll(stream));
        }

        [Fact]
        public void WriteBeginObject_IndirectArray_WritesObjectAddressAndBracket()
        {
            var doc = new PdfDocument();
            var array = new PdfArray();
            doc.Internals.AddObject(array);
            var (writer, stream) = CreateWriter();

            writer.WriteBeginObject(array);

            var text = ReadAll(stream);
            Assert.Contains("obj\n", text);
            Assert.EndsWith("[\n", text);
        }

        [Fact]
        public void WriteBeginObject_DirectDictionary_WritesOpenAngleBrackets()
        {
            var dict = new PdfDictionary();
            var (writer, stream) = CreateWriter();

            writer.WriteBeginObject(dict);

            Assert.Contains("<<", ReadAll(stream));
        }

        [Fact]
        public void WriteBeginObject_ThenWriteEndObject_IndirectDictionary_WritesEndobj()
        {
            var doc = new PdfDocument();
            var dict = new PdfDictionary();
            doc.Internals.AddObject(dict);
            var (writer, stream) = CreateWriter();

            writer.WriteBeginObject(dict);
            writer.WriteEndObject();

            Assert.Contains("endobj\n", ReadAll(stream));
        }

        [Fact]
        public void WriteBeginObject_ThenWriteEndObject_DirectArray_WritesClosingBracket()
        {
            var array = new PdfArray();
            var (writer, stream) = CreateWriter();

            writer.WriteBeginObject(array);
            writer.WriteEndObject();

            Assert.EndsWith("]", ReadAll(stream));
        }

        [Fact]
        public void WriteFileHeader_WritesPdfVersionBanner()
        {
            var doc = new PdfDocument();
            var (writer, stream) = CreateWriter();

            writer.WriteFileHeader(doc);

            Assert.StartsWith("%PDF-1.4\n", ReadAll(stream));
        }

        [Fact]
        public void WriteEof_WritesStartxrefAndTrailer()
        {
            var doc = new PdfDocument();
            var (writer, stream) = CreateWriter();

            writer.WriteEof(doc, 1234);

            var text = ReadAll(stream);
            Assert.StartsWith("startxref\n1234\n%%EOF\n", text);
        }

        [Fact]
        public void Position_ReflectsUnderlyingStreamPosition()
        {
            var (writer, stream) = CreateWriter();

            writer.WriteRaw("abc");

            Assert.Equal(3, writer.Position);
        }

        [Fact]
        public void Close_DisposesUnderlyingStreamByDefault()
        {
            var (writer, stream) = CreateWriter();

            writer.Close();

            Assert.Throws<ObjectDisposedException>(() => stream.Position);
        }

        [Fact]
        public void Close_WithFalse_LeavesStreamOpen()
        {
            var (writer, stream) = CreateWriter();

            writer.Close(false);

            var ex = Record.Exception(() => stream.Position);
            Assert.Null(ex);
        }

        [Fact]
        public void Indent_RoundTrips()
        {
            var (writer, _) = CreateWriter();

            writer.Indent = 4;

            Assert.Equal(4, writer.Indent);
        }

        [Fact]
        public void Options_RoundTrips()
        {
            var (writer, _) = CreateWriter();

            writer.Options = PdfWriterOptions.OmitStream;

            Assert.Equal(PdfWriterOptions.OmitStream, writer.Options);
        }

        [Fact]
        public void Stream_ReturnsUnderlyingStream()
        {
            var (writer, stream) = CreateWriter();

            Assert.Same(stream, writer.Stream);
        }
    }
}
