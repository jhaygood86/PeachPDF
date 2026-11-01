using System.IO.Compression;
using System.Text;
using PeachDrawing.PDF.Import;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>The tokenizer's tolerance for real-world syntax, and each stream filter the importer decodes, with the odd inputs writers produce.</summary>
    public class PdfLexerAndFilterTests
    {
        private static PdfLexer Lex(string text) => new(Encoding.Latin1.GetBytes(text));

        private static List<PdfToken> All(string text)
        {
            var lexer = Lex(text);
            var tokens = new List<PdfToken>();
            for (var t = lexer.Next(); t.Kind != PdfTokenKind.Eof; t = lexer.Next())
                tokens.Add(t);
            return tokens;
        }

        // ------------------------------------------------------------ lexer

        [Theory]
        [InlineData("(a\\nb\\tc\\rd\\be\\ff)", "a\nb\tc\rd\be\ff")]
        [InlineData("(\\101\\60\\7x)", "A0\ax")]
        [InlineData("(a\\\r\nb\\\nc\\\rd)", "abc" + "d")]
        [InlineData("(one\r\ntwo\rthree)", "one\ntwo\nthree")]
        [InlineData("(deeply (nested (parens)) stay)", "deeply (nested (parens)) stay")]
        [InlineData("(unterminated", "unterminated")]
        [InlineData("(\\q)", "q")]
        [InlineData("<41 4 2 4>", "AB" + "@")]
        [InlineData("<4>", "@")]
        [InlineData("<>", "")]
        public void Strings_AreDecodedByteForByte(string source, string expected)
        {
            var token = Lex(source).Next();

            Assert.Equal(PdfTokenKind.String, token.Kind);
            Assert.Equal(expected, token.Text);
        }

        [Theory]
        [InlineData("/Name", "Name")]
        [InlineData("/A#20B#2F", "A B/")]
        [InlineData("/Bad#zz", "Bad#zz")]
        [InlineData("/", "")]
        [InlineData("/a/b", "a")]
        public void Names_AreDecoded_AndEndAtTheNextDelimiter(string source, string expected)
        {
            var token = Lex(source).Next();

            Assert.Equal(PdfTokenKind.Name, token.Kind);
            Assert.Equal(expected, token.Text);
        }

        [Theory]
        [InlineData("42", 42, 42.0, true)]
        [InlineData("-7", -7, -7.0, true)]
        [InlineData("+7", 7, 7.0, true)]
        [InlineData("--5", -5, -5.0, true)]
        [InlineData("3.25", 3, 3.25, false)]
        [InlineData("-.5", 0, -0.5, false)]
        [InlineData("+.5", 0, 0.5, false)]
        [InlineData("4.", 4, 4.0, false)]
        [InlineData("12-3", 12, 12.0, true)]
        public void Numbers_AreReadLeniently(string source, long integer, double real, bool isInteger)
        {
            var token = Lex(source).Next();

            Assert.Equal(isInteger ? PdfTokenKind.Integer : PdfTokenKind.Real, token.Kind);
            Assert.Equal(integer, token.Integer);
            Assert.Equal(real, token.Real, 9);
        }

        [Theory]
        [InlineData("-")]
        [InlineData(".")]
        [InlineData("1.2.3")]
        [InlineData("12a")]
        [InlineData("obj")]
        [InlineData("R")]
        public void WordsThatAreNotNumbers_AreKeywords(string source)
        {
            var token = Lex(source).Next();

            Assert.Equal(PdfTokenKind.Keyword, token.Kind);
            Assert.Equal(source, token.Text);
        }

        [Fact]
        public void Delimiters_Comments_AndStrayClosers_AreHandled()
        {
            var tokens = All("[ 1 ] << /K (v) >> { } % a comment\n) > x");

            Assert.Equal(
            [
                PdfTokenKind.ArrayStart, PdfTokenKind.Integer, PdfTokenKind.ArrayEnd, PdfTokenKind.DictStart, PdfTokenKind.Name,
                PdfTokenKind.String, PdfTokenKind.DictEnd, PdfTokenKind.Keyword, PdfTokenKind.Keyword, PdfTokenKind.Keyword,
            ], tokens.Select(t => t.Kind));
            Assert.Equal("{", tokens[7].Text);
            Assert.Equal("x", tokens[^1].Text);
        }

        [Fact]
        public void Peek_DoesNotConsume_AndPositionIsSettable()
        {
            var lexer = Lex("12 34");

            Assert.Equal(12, lexer.Peek().Integer);
            Assert.Equal(12, lexer.Next().Integer);
            lexer.Position = 0;
            Assert.Equal(12, lexer.Next().Integer);
            Assert.Equal(34, lexer.Next().Integer);
            Assert.Equal(PdfTokenKind.Eof, lexer.Next().Kind);
            Assert.Equal(5, lexer.End);
        }

        [Fact]
        public void Lexer_CanBeBoundedToARangeOfTheData()
        {
            var data = Encoding.ASCII.GetBytes("1 2 3 4");
            var lexer = new PdfLexer(data, 2, 5);

            Assert.Equal(2, lexer.Next().Integer);
            Assert.Equal(3, lexer.Next().Integer);
            Assert.Equal(PdfTokenKind.Eof, lexer.Next().Kind);
        }

        // ------------------------------------------------------------ filters

        private static byte[] Zlib(byte[] data)
        {
            using var output = new MemoryStream();
            using (var z = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true)) z.Write(data);
            return output.ToArray();
        }

        private static byte[] RawDeflate(byte[] data)
        {
            using var output = new MemoryStream();
            using (var z = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true)) z.Write(data);
            return output.ToArray();
        }

        [Fact]
        public void Flate_DecodesZlibData_RawDeflate_AndTruncatedData()
        {
            var text = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("PeachPDF ", 200)));

            Assert.Equal(text, PdfStreamFilters.Decode("FlateDecode", Zlib(text)));
            Assert.Equal(text, PdfStreamFilters.Decode("Fl", RawDeflate(text)));

            var truncated = Zlib(text);
            Array.Resize(ref truncated, truncated.Length / 2);
            var partial = PdfStreamFilters.Decode("FlateDecode", truncated);
            Assert.NotEmpty(partial);
            Assert.True(partial.Length < text.Length);
            Assert.Empty(PdfStreamFilters.Decode("FlateDecode", []));
        }

        [Fact]
        public void Flate_WithAPngPredictor_UndoesEachRowFilter()
        {
            // Two 3-byte rows, 8-bit gray, per-row filters: Sub then Up.
            byte[] filtered = [1, 10, 5, 5, 2, 1, 1, 1];
            var decoded = PdfStreamFilters.Decode("FlateDecode", Zlib(filtered), predictor: 15, colors: 1, bitsPerComponent: 8, columns: 3);

            Assert.Equal([10, 15, 20, 11, 16, 21], decoded);
        }

        [Fact]
        public void PngPredictor_HandlesAverageAndPaethAndNoneRows()
        {
            // Row 0: None [4 8 12]; row 1: Average (3) [0 0 0] -> averages of left and up; row 2: Paeth (4) [1 1 1].
            byte[] data = [0, 4, 8, 12, 3, 0, 0, 0, 4, 1, 1, 1];
            var decoded = PdfStreamFilters.Predict(data, 12, 1, 8, 3);

            Assert.Equal([4, 8, 12], decoded[..3]);
            Assert.Equal([2, 5, 8], decoded[3..6]);      // avg(left, up): (0+4)/2, (2+8)/2, (5+12)/2
            Assert.Equal(3, decoded.Length / 3);
            Assert.True(decoded[6] > 0 && decoded[7] > 0 && decoded[8] > 0);
        }

        [Fact]
        public void PngPredictor_UnknownFilterTypeCopiesTheRow_AndAShortLastRowIsTolerated()
        {
            byte[] data = [9, 1, 2, 3, 0, 7];
            var decoded = PdfStreamFilters.Predict(data, 12, 1, 8, 3);

            Assert.Equal([1, 2, 3, 7, 0, 0], decoded);
        }

        [Fact]
        public void TiffPredictor_AddsEachSampleToTheOneBefore_AndOtherDepthsPassThrough()
        {
            byte[] data = [1, 1, 1, 10, 5, 5];
            Assert.Equal([1, 2, 3, 10, 15, 20], PdfStreamFilters.Predict(data, 2, 1, 8, 3));
            Assert.Equal(data, PdfStreamFilters.Predict(data, 2, 1, 16, 3));
            Assert.Equal(data, PdfStreamFilters.Predict(data, 1, 1, 8, 3));
            Assert.Equal(data, PdfStreamFilters.Predict(data, 12, 1, 8, 0));
        }

        [Fact]
        public void TiffPredictor_WithColourSamples_AddsPerChannel()
        {
            byte[] data = [1, 2, 3, 1, 1, 1];     // two RGB pixels in one row
            Assert.Equal([1, 2, 3, 2, 3, 4], PdfStreamFilters.Predict(data, 2, 3, 8, 2));
        }

        [Fact]
        public void Ascii85_DecodesZGroups_PartialGroups_AndTheOptionalPrefix()
        {
            Assert.Equal("Man ", Encoding.ASCII.GetString(PdfStreamFilters.Decode("A85", Encoding.ASCII.GetBytes("<~9jqo^~>"))));
            Assert.Equal(new byte[8], PdfStreamFilters.Decode("ASCII85Decode", Encoding.ASCII.GetBytes("zz~>")));
            Assert.Equal("Ma", Encoding.ASCII.GetString(PdfStreamFilters.Decode("ASCII85Decode", Encoding.ASCII.GetBytes("9jn~>"))));
            Assert.Equal("Man ", Encoding.ASCII.GetString(PdfStreamFilters.Decode("ASCII85Decode", Encoding.ASCII.GetBytes("9j\nqo^"))));
        }

        [Fact]
        public void AsciiHex_IgnoresWhiteSpace_AndPadsAnOddFinalDigit()
        {
            Assert.Equal([0x48, 0x65, 0x70], PdfStreamFilters.Decode("AHx", Encoding.ASCII.GetBytes("48 6\n5 7>ignored")));
        }

        [Fact]
        public void RunLength_RepeatsAndCopies_AndStopsAtTheEndMarker()
        {
            Assert.Equal([1, 2, 3, 9, 9, 9, 9], PdfStreamFilters.Decode("RL", [2, 1, 2, 3, 253, 9, 128, 7, 7]));
            Assert.Equal([5], PdfStreamFilters.Decode("RunLengthDecode", [5, 5]));       // a copy run longer than the data
            Assert.Empty(PdfStreamFilters.Decode("RunLengthDecode", [200]));            // a repeat run with no byte
        }

        [Fact]
        public void Lzw_ResetsOnClearCodes_AndSupportsEarlyChangeZero()
        {
            // Data written with early change 1 (the default): the spec's example.
            byte[] encoded = [0x80, 0x0B, 0x60, 0x50, 0x22, 0x0C, 0x0C, 0x85, 0x01];
            Assert.Equal([45, 45, 45, 45, 45, 65, 45, 45, 45, 66], PdfStreamFilters.Decode("LZW", encoded));
            Assert.NotNull(PdfStreamFilters.Decode("LZWDecode", encoded, earlyChange: 0));
            Assert.Empty(PdfStreamFilters.Decode("LZWDecode", []));
        }

        [Fact]
        public void LzwWithAPredictor_IsPredicted()
        {
            byte[] encoded = [0x80, 0x0B, 0x60, 0x50, 0x22, 0x0C, 0x0C, 0x85, 0x01];
            var plain = PdfStreamFilters.Decode("LZWDecode", encoded);
            var predicted = PdfStreamFilters.Decode("LZWDecode", encoded, predictor: 2, colors: 1, bitsPerComponent: 8, columns: 10);

            Assert.Equal(plain.Length, predicted.Length);
            Assert.Equal(plain[0], predicted[0]);
            Assert.Equal((byte)(plain[0] + plain[1]), predicted[1]);
        }

        [Theory]
        [InlineData("Fl", "FlateDecode", true)]
        [InlineData("LZW", "LZWDecode", true)]
        [InlineData("A85", "ASCII85Decode", true)]
        [InlineData("AHx", "ASCIIHexDecode", true)]
        [InlineData("RL", "RunLengthDecode", true)]
        [InlineData("DCT", "DCTDecode", false)]
        [InlineData("CCF", "CCITTFaxDecode", false)]
        [InlineData("JBIG2Decode", "JBIG2Decode", false)]
        public void FilterNames_AreCanonicalised_AndOnlyGeneralFiltersAreDecoded(string name, string canonical, bool general)
        {
            Assert.Equal(canonical, PdfStreamFilters.Canonical(name));
            Assert.Equal(general, PdfStreamFilters.IsGeneral(name));
        }

        [Fact]
        public void AnUndecodedFilter_LeavesTheDataAlone()
        {
            byte[] data = [1, 2, 3];

            Assert.Equal(data, PdfStreamFilters.Decode("JBIG2Decode", data));
        }

        [Fact]
        public void PdfNull_IsASingletonThatPrintsAsNull()
        {
            Assert.Same(PdfNull.Instance, PdfNull.Instance);
            Assert.Equal("null", PdfNull.Instance.ToString());
        }
    }
}
