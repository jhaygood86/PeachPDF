using System.Text;
using PeachPDF.Fonts.OpenType;
using Xunit;

namespace PeachPDF.Tests.PdfSharpCoreTests.Fonts
{
    /// <summary>
    /// Low-level coverage for <see cref="CffIndex"/>/<see cref="CffDict"/>/<see cref="CffTable"/> -
    /// the binary-format building blocks <see cref="Type2CharstringInterpreter"/> needs to locate a
    /// glyph's charstring and its local/global subroutines (issue #1117). <c>GetTextOutlineTests</c>
    /// already proves the interpreter end-to-end against a real bundled CFF font; these hand-built
    /// byte arrays isolate the INDEX/DICT readers themselves, including the CID-keyed-font fallback
    /// that font is too ordinary to exercise.
    /// </summary>
    public class CffTableTests
    {
        [Fact]
        public void CffIndex_EmptyIndex_HasZeroCountAndAdvancesTwoBytes()
        {
            byte[] data = [0, 0]; // count=0 - no offSize/offsets/data follow
            var pos = 0;

            var index = CffIndex.Read(data, ref pos);

            Assert.Equal(0, index.Count);
            Assert.Equal(2, pos);
        }

        [Fact]
        public void CffIndex_TwoEntries_RoundTripsBytesAndAdvancesPastOwnData()
        {
            // count=2, offSize=1, offsets [1, 3, 5] (1-based, relative to the byte right after the
            // offset array), data = "AB" + "CD", then one trailing byte belonging to whatever follows.
            byte[] data = [0, 2, 1, 1, 3, 5, (byte)'A', (byte)'B', (byte)'C', (byte)'D', 0xFF];
            var pos = 0;

            var index = CffIndex.Read(data, ref pos);

            Assert.Equal(2, index.Count);
            Assert.Equal("AB", Encoding.ASCII.GetString(index[0]));
            Assert.Equal("CD", Encoding.ASCII.GetString(index[1]));
            Assert.Equal(10, pos); // stops right after "CD" - the trailing 0xFF is not consumed
        }

        [Fact]
        public void CffDict_IntegerEncodings_AllFiveRangesRoundTrip()
        {
            // 139-range (single byte): 0 encodes as 139. 247-250 range: 108 encodes as [247, 0].
            // 251-254 range: -108 encodes as [251, 0]. 28 (int16): 1000. 29 (int32): 100000. Operator
            // 17 (CharStrings) clears the accumulated operand list onto itself so it can be read back.
            byte[] data =
            [
                139,                         // 0
                247, 0,                      // 108
                251, 0,                      // -108
                28, 0x03, 0xE8,               // 1000
                29, 0x00, 0x01, 0x86, 0xA0,   // 100000
                17                            // operator: CharStrings
            ];

            var dict = CffDict.Parse(data, 0, data.Length);

            Assert.True(dict.TryGet(17, out var operands));
            Assert.Equal([0d, 108d, -108d, 1000d, 100000d], operands);
        }

        [Fact]
        public void CffDict_EscapedTwoByteOperator_DecodesRos()
        {
            // ROS = 12 30 (CFF's marker for a CID-keyed font), preceded by three arbitrary operands
            // (registry SID, ordering SID, supplement), as a real Top DICT would carry them.
            byte[] data = [139, 139, 139, 12, 30];

            var dict = CffDict.Parse(data, 0, data.Length);

            Assert.True(dict.Has(1230));
        }

        [Fact]
        public void CffDict_RealNumberOperand_DecodesNegativeValue()
        {
            // -2.5, nibble-packed: '-'(0xE) '2'(0x2) '.'(0xA) '5'(0x5) end(0xF), then operator 7
            // (FontMatrix) to read the decoded value back out.
            byte[] data = [30, 0xE2, 0xA5, 0xFF, 7];

            var dict = CffDict.Parse(data, 0, data.Length);

            Assert.True(dict.TryGet(7, out var operands));
            Assert.Equal(-2.5, operands[0], precision: 6);
        }

        [Fact]
        public void CffDict_RealNumberOperand_DecodesExponentForm()
        {
            // 1.5E-2, nibble-packed: '1'(0x1) '.'(0xA) '5'(0x5) 'E-'(0xC) '2'(0x2) end(0xF), then
            // operator 7 to read it back - exercises the 'E'/'E-' nibbles AppendNibble's digit-only
            // sibling test above doesn't reach.
            byte[] data = [30, 0x1A, 0x5C, 0x2F, 7];

            var dict = CffDict.Parse(data, 0, data.Length);

            Assert.True(dict.TryGet(7, out var operands));
            Assert.Equal(0.015, operands[0], precision: 6);
        }

        [Fact]
        public void CffDict_RealNumberOperand_DecodesPositiveExponentForm()
        {
            // 1E2, nibble-packed: '1'(0x1) 'E'(0xB) '2'(0x2) end(0xF), then operator 7 to read it back
            // - the plain 'E' nibble (positive exponent) the exponent-form sibling test above doesn't
            // reach (that one only exercises 'E-').
            byte[] data = [30, 0x1B, 0x2F, 7];

            var dict = CffDict.Parse(data, 0, data.Length);

            Assert.True(dict.TryGet(7, out var operands));
            Assert.Equal(100, operands[0], precision: 6);
        }

        [Fact]
        public void CffDict_ReservedLeadByte_IsSkippedRatherThanCorruptingWhatFollows()
        {
            // 255 is not a valid DICT operand/operator lead byte (Table 3's reserved slot) - a
            // conforming reader skips it rather than misinterpreting subsequent bytes, so a stray one
            // (from a future DICT extension this reader doesn't know about) can't desync the parse of
            // whatever operand/operator legitimately follows it.
            byte[] data = [255, 139, 17]; // reserved byte, then 0, then operator 17 (CharStrings)

            var dict = CffDict.Parse(data, 0, data.Length);

            Assert.True(dict.TryGet(17, out var operands));
            Assert.Equal([0d], operands);
        }

        [Fact]
        public void CffTable_CidKeyedFont_ReportsCidKeyedButNotSupported()
        {
            // A CID-keyed CFF needs FDArray/FDSelect to pick the right Private DICT/local subrs per
            // glyph - deliberately not parsed here (see the accepted-gap note this file's header
            // links). CffTable must still recognize IsCidKeyed (so a caller could report *why* this
            // font falls back) while reporting IsSupported = false rather than guessing at the wrong
            // (top-level, likely absent) local subrs.
            byte[] font = SyntheticCff.CidKeyedFont();

            var cff = new CffTable(font, tableStart: 0);

            Assert.True(cff.IsCidKeyed);
            Assert.False(cff.IsSupported);
        }

        [Fact]
        public void CffTable_OrdinaryFont_ResolvesCharStringsAndIsSupported()
        {
            byte[] font = SyntheticCff.OrdinaryFont(charstrings: [[14]] /* a single bare endchar glyph */);

            var cff = new CffTable(font, tableStart: 0);

            Assert.False(cff.IsCidKeyed);
            Assert.True(cff.IsSupported);
            Assert.Equal(1, cff.CharStrings.Count);
        }

        [Fact]
        public void CffTable_TruncatedTable_FailsSoftRatherThanThrowing()
        {
            byte[] truncated = [1, 0, 4, 4]; // header only - every INDEX read past this is out of bounds

            var cff = new CffTable(truncated, tableStart: 0);

            Assert.False(cff.IsSupported);
        }

        [Fact]
        public void CffTable_ExplicitNonType2Charstrings_ReportsUnsupported()
        {
            // CharstringType (12 6) defaults to 2 (Type 2) when absent, which every other fixture in
            // this file relies on - an explicit, different value (legacy Type 1 charstrings) must be
            // rejected instead of the interpreter mis-decoding Type 1's incompatible operator set.
            byte[] font = SyntheticCff.OrdinaryFont(charstrings: [[14]], charstringType: 1);

            var cff = new CffTable(font, tableStart: 0);

            Assert.False(cff.IsSupported);
        }
    }

    /// <summary>
    /// Hand-assembles the minimum valid CFF byte layout <see cref="CffTable"/> needs, for tests that
    /// want to isolate its parsing from a real font file. Offsets a Top DICT operand carries (the
    /// CharStrings/Private DICT locations) are always relative to <c>tableStart</c> - callers here use
    /// 0, since these arrays stand alone rather than sitting inside a larger sfnt file.
    /// </summary>
    internal static class SyntheticCff
    {
        public static byte[] OrdinaryFont(byte[][] charstrings, byte[][]? globalSubrs = null, int charstringType = 2) =>
            Build(cidKeyed: false, charstrings, globalSubrs ?? [], charstringType);

        public static byte[] CidKeyedFont() => Build(cidKeyed: true, charstrings: [[14]], globalSubrs: [], charstringType: 2);

        private static byte[] Build(bool cidKeyed, byte[][] charstrings, byte[][] globalSubrs, int charstringType)
        {
            byte[] header = [1, 0, 4, 4]; // major, minor, hdrSize=4, offSize(unused by readers here)
            byte[] nameIndex = BuildIndex([Encoding.ASCII.GetBytes("Synthetic")]);
            byte[] stringIndex = BuildIndex([]);
            byte[] globalSubrIndex = BuildIndex(globalSubrs);
            byte[] charStringsIndex = BuildIndex(charstrings);

            // charstringType == 2 is the spec default (never written explicitly by real fonts either),
            // so only a non-default value adds a CharstringType (12 6) entry - keeps every other
            // fixture's byte layout exactly as it was before this parameter existed.
            byte[] charstringTypeEntry = charstringType == 2 ? [] : Concat(DictInt(charstringType), DictOp(1206));

            // The Top DICT's own CharStrings offset (op 17) must point at charStringsIndex's start,
            // which sits after header + nameIndex + topDictIndex + stringIndex + globalSubrIndex - but
            // topDictIndex's own length depends on the dict bytes, which depend on this offset. Break
            // the cycle by building the dict against a placeholder offset of the right byte-width
            // first (DictInt's encoded length only depends on the value's magnitude, and every
            // fixture here stays under 1240 either way), matching how the real read-order works.
            byte[] topDictBytes = cidKeyed
                ? Concat(DictInt(0), DictInt(0), DictInt(0), DictOp(1230)) // ROS - CffTable must never reach CharStrings
                : Concat(charstringTypeEntry, DictInt(0), DictOp(17)); // CharStrings - offset patched in below

            byte[] topDictIndex = BuildIndex([topDictBytes]);

            int charStringsOffset = header.Length + nameIndex.Length + topDictIndex.Length + stringIndex.Length + globalSubrIndex.Length;

            if (!cidKeyed)
            {
                // Rebuild the dict (and re-wrap it in a fresh INDEX) against the real offset, now
                // that it is known - every fixture built by this class stays well under the 107
                // boundary where DictInt's own encoded length would grow and this single pass would
                // need to be repeated, so one rebuild is enough here.
                topDictBytes = Concat(charstringTypeEntry, DictInt(charStringsOffset), DictOp(17));
                topDictIndex = BuildIndex([topDictBytes]);
            }

            return Concat(header, nameIndex, topDictIndex, stringIndex, globalSubrIndex, charStringsIndex);
        }

        private static byte[] BuildIndex(byte[][] entries)
        {
            if (entries.Length == 0) return [0, 0];

            const int offSize = 2; // fixed 2-byte offsets - simplest correct choice for small test data
            var offsets = new int[entries.Length + 1];
            var running = 1;
            for (var i = 0; i < entries.Length; i++)
            {
                offsets[i] = running;
                running += entries[i].Length;
            }
            offsets[entries.Length] = running;

            using var stream = new System.IO.MemoryStream();
            stream.WriteByte((byte)(entries.Length >> 8));
            stream.WriteByte((byte)entries.Length);
            stream.WriteByte(offSize);
            foreach (int offset in offsets)
            {
                stream.WriteByte((byte)(offset >> 8));
                stream.WriteByte((byte)offset);
            }
            foreach (byte[] entry in entries)
                stream.Write(entry, 0, entry.Length);

            return stream.ToArray();
        }

        private static byte[] DictInt(int value)
        {
            if (value is >= -107 and <= 107) return [(byte)(value + 139)];
            if (value is >= -32768 and <= 32767) return [28, (byte)(value >> 8), (byte)value];
            return [29, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
        }

        private static byte[] DictOp(int op) => op < 1200 ? [(byte)op] : [12, (byte)(op - 1200)];

        private static byte[] Concat(params byte[][] parts)
        {
            using var stream = new System.IO.MemoryStream();
            foreach (byte[] part in parts) stream.Write(part, 0, part.Length);
            return stream.ToArray();
        }
    }
}
