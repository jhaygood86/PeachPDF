using System.Text;
using PeachDrawing.PDF.Import;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>
    /// The CFF reader against small CFF programs assembled byte by byte, so every charset and encoding format, the number encodings of a DICT and
    /// the CID-keyed header are exercised (the real fonts the other tests use each take only one path).
    /// </summary>
    public class CffReaderTests
    {
        private static byte[] Index(params byte[][] items)
        {
            var o = new List<byte> { (byte)(items.Length >> 8), (byte)items.Length };
            if (items.Length == 0) return [.. o];
            o.Add(2);                                    // offSize
            int offset = 1;
            o.AddRange([(byte)(offset >> 8), (byte)offset]);
            foreach (var item in items)
            {
                offset += item.Length;
                o.AddRange([(byte)(offset >> 8), (byte)offset]);
            }
            foreach (var item in items) o.AddRange(item);
            return [.. o];
        }

        /// <summary>A DICT integer in the fixed five-byte form, so the size of a DICT does not depend on the value.</summary>
        private static byte[] Int5(int v) => [29, (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];

        private sealed record Options(
            int Glyphs = 3, byte[]? Charset = null, int PredefinedCharset = -1, byte[]? Encoding = null, int PredefinedEncoding = 0,
            string[]? Strings = null, bool Cid = false, byte[]? FontMatrixOperands = null, byte[]? FontBBoxOperands = null);

        /// <summary>Assembles a CFF program with <see cref="Options.Glyphs"/> one-byte charstrings and the requested charset and encoding.</summary>
        private static byte[] Cff(Options o)
        {
            byte[] header = [1, 0, 4, 1];
            var nameIndex = Index("T"u8.ToArray());
            var stringIndex = Index((o.Strings ?? []).Select(s => Encoding.ASCII.GetBytes(s)).ToArray());
            var gsubrIndex = Index();
            var charStrings = Index(Enumerable.Range(0, o.Glyphs).Select(_ => new byte[] { 14 }).ToArray());

            byte[] TopDict(int charsetOffset, int encodingOffset, int charStringsOffset)
            {
                var d = new List<byte>();
                if (o.Cid) { d.AddRange(Int5(391)); d.AddRange(Int5(392)); d.AddRange(Int5(0)); d.AddRange([12, 30]); }
                if (o.FontMatrixOperands is { } fm) { d.AddRange(fm); d.AddRange([12, 7]); }
                if (o.FontBBoxOperands is { } bb) { d.AddRange(bb); d.Add(5); }
                d.AddRange(Int5(charsetOffset)); d.Add(15);
                d.AddRange(Int5(encodingOffset)); d.Add(16);
                d.AddRange(Int5(charStringsOffset)); d.Add(17);
                return [.. d];
            }

            // The offsets are absolute; the DICT's size is the same whatever they are, so one pass with zeros gives the layout.
            int topSize = Index(TopDict(0, 0, 0)).Length;
            int afterIndexes = header.Length + nameIndex.Length + topSize + stringIndex.Length + gsubrIndex.Length;
            byte[] charset = o.Charset ?? [];
            byte[] encoding = o.Encoding ?? [];
            int charsetOffset = o.PredefinedCharset >= 0 ? o.PredefinedCharset : afterIndexes;
            int encodingOffset = o.Encoding is null ? o.PredefinedEncoding : afterIndexes + charset.Length;
            int charStringsOffset = afterIndexes + charset.Length + encoding.Length;

            var cff = new List<byte>(header);
            cff.AddRange(nameIndex);
            cff.AddRange(Index(TopDict(charsetOffset, encodingOffset, charStringsOffset)));
            cff.AddRange(stringIndex);
            cff.AddRange(gsubrIndex);
            cff.AddRange(charset);
            cff.AddRange(encoding);
            cff.AddRange(charStrings);
            return [.. cff];
        }

        [Fact]
        public void CharsetFormat0_ListsOneSidPerGlyph_AndCustomStringsNameTheRest()
        {
            // glyphs 1..3 are SIDs 34 (A), 391 (the first custom string) and 35 (B).
            var font = CffFont.Parse(Cff(new Options(Glyphs: 4, Charset: [0, 0, 34, 1, 135, 0, 35], Strings: ["custom"])))!;

            Assert.Equal([".notdef", "A", "custom", "B"], font.GlyphNames);
            Assert.False(font.IsCid);
        }

        [Fact]
        public void CharsetFormat1_AndFormat2_ExpandRanges()
        {
            var one = CffFont.Parse(Cff(new Options(Glyphs: 4, Charset: [1, 0, 34, 2])))!;              // first 34, three glyphs (left = 2)
            Assert.Equal([".notdef", "A", "B", "C"], one.GlyphNames);

            var two = CffFont.Parse(Cff(new Options(Glyphs: 4, Charset: [2, 0, 66, 0, 2])))!;           // first 66 (a), left = 2 (two-byte)
            Assert.Equal([".notdef", "a", "b", "c"], two.GlyphNames);
        }

        [Theory]
        [InlineData(0, new[] { ".notdef", "space", "exclam" })]
        [InlineData(1, new[] { ".notdef", ".notdef", ".notdef" })]
        [InlineData(2, new[] { ".notdef", ".notdef", ".notdef" })]
        public void PredefinedCharsets_AreIsoAdobeOrUnnamed(int predefined, string[] expected)
        {
            var font = CffFont.Parse(Cff(new Options(Glyphs: 3, PredefinedCharset: predefined)))!;

            Assert.Equal(expected, font.GlyphNames);
        }

        [Fact]
        public void EncodingFormat0_AssignsCodesToGlyphsInOrder()
        {
            var font = CffFont.Parse(Cff(new Options(Glyphs: 3, Charset: [0, 0, 34, 0, 35], Encoding: [0, 2, 65, 66])))!;

            Assert.Equal(1, font.BuiltInEncoding[65]);
            Assert.Equal(2, font.BuiltInEncoding[66]);
        }

        [Fact]
        public void EncodingFormat1_AssignsRangesOfCodes_AndSupplementsAreIgnored()
        {
            var font = CffFont.Parse(Cff(new Options(Glyphs: 4, Charset: [0, 0, 34, 0, 35, 0, 36], Encoding: [1 | 0x80, 1, 97, 2, 1, 0x41, 0, 36])))!;

            Assert.Equal(1, font.BuiltInEncoding[97]);
            Assert.Equal(3, font.BuiltInEncoding[99]);
        }

        [Fact]
        public void PredefinedEncodings_LeaveTheBuiltInEncodingEmpty()
        {
            Assert.Empty(CffFont.Parse(Cff(new Options(PredefinedEncoding: 0)))!.BuiltInEncoding);
            Assert.Empty(CffFont.Parse(Cff(new Options(PredefinedEncoding: 1)))!.BuiltInEncoding);
        }

        [Fact]
        public void CidKeyedFont_HasCidsInsteadOfNames()
        {
            var font = CffFont.Parse(Cff(new Options(Glyphs: 3, Cid: true, Charset: [0, 0, 5, 0, 9], Strings: ["Adobe", "Identity"])))!;

            Assert.True(font.IsCid);
            Assert.Equal([0, 5, 9], font.CidOfGlyph);
            Assert.Equal(2, font.GlyphOfCid()[9]);
            Assert.Empty(font.GlyphNames);
        }

        [Fact]
        public void DictNumbers_AreDecodedInEveryEncoding()
        {
            // FontMatrix as reals: 0.001, 0, 0, 1E-3, 0, 0 (nibbles: 0 . 0 0 1 end / 1 E- 3 end).
            byte[] matrix = [30, 0x0A, 0x00, 0x1F, 139, 139, 30, 0x1C, 0x3F, 139, 139];
            // FontBBox using the one-byte (-100 = 39), two-byte positive (1000), two-byte negative (-300) and 28 forms.
            byte[] box = [39 /* -100 */, 28, 0x03, 0xE8 /* 1000 */, 251, 192 /* -(251-251)*256-192-108 = -300 */, 247, 0 /* 108 */];
            var font = CffFont.Parse(Cff(new Options(FontMatrixOperands: matrix, FontBBoxOperands: box)))!;

            Assert.Equal(0.001, font.FontMatrix[0], 9);
            Assert.Equal(0.001, font.FontMatrix[3], 9);
            Assert.Equal([-100.0, 1000.0, -300.0, 108.0], font.FontBBox);
        }

        [Fact]
        public void DefaultsApply_WhenTheFontGivesNoMatrixOrBox()
        {
            var font = CffFont.Parse(Cff(new Options()))!;

            Assert.Equal([0.001, 0, 0, 0.001, 0, 0], font.FontMatrix);
            Assert.Equal([0.0, 0, 0, 0], font.FontBBox);
        }

        [Fact]
        public void Truncated_OrInconsistentData_IsNull()
        {
            var good = Cff(new Options(Glyphs: 3, Charset: [0, 0, 34, 0, 35]));

            Assert.Null(CffFont.Parse(good[..30]));
            var badOffSize = (byte[])good.Clone();
            badOffSize[4 + 2] = 9;                                   // the name INDEX's offSize
            Assert.Null(CffFont.Parse(badOffSize));
            Assert.Null(CffFont.Parse([1, 0, 4, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));   // empty INDEXes: no top DICT
        }
    }
}
