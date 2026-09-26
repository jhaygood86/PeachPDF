using System.Text;
using PeachDrawing.Text.Internal.Fonts.OpenType;
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
        public void CffTable_CidKeyedFontMissingFdArrayAndFdSelect_ReportsCidKeyedButNotSupported()
        {
            // A CID-keyed CFF needs FDArray/FDSelect to pick the right Private DICT/local subrs per
            // glyph - this fixture's Top DICT carries only ROS (no CharStrings, no FDArray/FDSelect
            // at all), so CffTable must still recognize IsCidKeyed (so a caller could report *why*
            // this font falls back) while reporting IsSupported = false rather than guessing at the
            // wrong (top-level, likely absent) local subrs.
            byte[] font = SyntheticCff.CidKeyedFont();

            var cff = new CffTable(font, tableStart: 0);

            Assert.True(cff.IsCidKeyed);
            Assert.False(cff.IsSupported);
        }

        [Theory]
        [InlineData(0)] // FDSelect format 0: a flat one-byte-per-GID array
        [InlineData(3)] // FDSelect format 3: (first GID, FD index) ranges terminated by a sentinel
        public void CffTable_CidKeyedFontWithFdArrayAndFdSelect_IsSupportedAndResolvesPerGidLocalSubrs(int fdSelectFormat)
        {
            byte[] font = SyntheticCff.CidKeyedFontWithFdArrayAndFdSelect(fdSelectFormat);

            var cff = new CffTable(font, tableStart: 0);

            Assert.True(cff.IsCidKeyed);
            Assert.True(cff.IsSupported);
            Assert.Equal(2, cff.CharStrings.Count);

            // GID 0 maps to FD 0, GID 1 to FD 1 - each FD's own (distinct) local Subrs INDEX, not the
            // (nonexistent) top-level one.
            Assert.Equal(1, cff.LocalSubrsFor(0).Count);
            Assert.Equal(1, cff.LocalSubrsFor(1).Count);
            Assert.NotEqual(
                Encoding.Latin1.GetString(cff.LocalSubrsFor(0)[0]),
                Encoding.Latin1.GetString(cff.LocalSubrsFor(1)[0]));
        }

        [Fact]
        public void CffTable_CidKeyedFontWithFdArrayButNoFdSelect_ReportsUnsupported()
        {
            // FDArray alone cannot resolve which FD a glyph belongs to - FDSelect is required too.
            byte[] font = SyntheticCff.CidKeyedFontWithFdArrayButNoFdSelect();

            var cff = new CffTable(font, tableStart: 0);

            Assert.True(cff.IsCidKeyed);
            Assert.False(cff.IsSupported);
        }

        [Fact]
        public void CffTable_FdArrayEntryWithNoPrivateDict_ResolvesToAnEmptyLocalSubrsIndexRatherThanThrowing()
        {
            // A Font DICT with no Private operator is legal CFF (that FD needs no private-scoped data);
            // CffTable must resolve it to CffIndex.Empty rather than leaving an unassigned
            // default(CffIndex), whose own Count throws.
            byte[] font = SyntheticCff.CidKeyedFontWithFdArrayEntryMissingPrivateDict();

            var cff = new CffTable(font, tableStart: 0);

            Assert.True(cff.IsSupported);
            Assert.Equal(1, cff.LocalSubrsFor(0).Count); // GID 0 -> FD 0, which does have local subrs
            Assert.Equal(0, cff.LocalSubrsFor(1).Count); // GID 1 -> FD 1, which has none
        }

        [Fact]
        public void CffTable_CidKeyedFontWithUnsupportedFdSelectFormat_FailsSoftRatherThanThrowing()
        {
            byte[] font = SyntheticCff.CidKeyedFontWithUnsupportedFdSelectFormat();

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

        /// <summary>
        /// A well-formed CID-keyed CFF: two glyphs (GID 0, GID 1), two FDArray Font DICTs each with
        /// its own one-entry local Subrs INDEX (distinct content per FD, so a test can tell which FD a
        /// decode actually used), and an FDSelect table (format 0 or 3, per <paramref name="fdSelectFormat"/>)
        /// mapping GID 0 -&gt; FD 0 and GID 1 -&gt; FD 1. Both glyphs' own charstrings are byte-identical
        /// (push -107, callsubr 0) - the only way their decode can differ is if <c>CffTable</c>
        /// actually resolves a different local Subrs INDEX per GID rather than a single top-level one.
        /// </summary>
        /// <remarks>
        /// Every offset a DICT stores that points forward to data whose own position depends on this
        /// DICT's encoded length (CharStrings/FDArray/FDSelect in the Top DICT, Private in each Font
        /// DICT) is written with a fixed-width 3-byte encoding (<see cref="DictInt16"/>) instead of the
        /// variable-width <see cref="DictInt"/> the rest of this class uses - that decouples the
        /// containing DICT's own byte length from the offset's numeric value, so each offset can be
        /// computed in one forward pass (build with a placeholder, measure fixed lengths, patch in the
        /// real value) with no risk of the patch itself changing any length it was computed from.
        /// </remarks>
        public static byte[] CidKeyedFontWithFdArrayAndFdSelect(int fdSelectFormat) =>
            BuildCidKeyedWithFdArray(fdSelectFormat, omitFdSelectOperator: false);

        /// <summary>
        /// A CID-keyed CFF with a well-formed FDArray but no FDSelect operator at all in the Top DICT -
        /// FDArray alone is not enough to resolve a glyph's FD, so <c>CffTable</c> must still report
        /// <c>IsSupported = false</c> rather than guessing.
        /// </summary>
        public static byte[] CidKeyedFontWithFdArrayButNoFdSelect() =>
            BuildCidKeyedWithFdArray(fdSelectFormat: 0, omitFdSelectOperator: true);

        /// <summary>
        /// A CID-keyed CFF whose FDSelect table declares a format byte this codebase does not
        /// implement (only 0 and 3 are, per the CFF spec's own defined formats) - <c>CffTable</c> must
        /// fail soft (its constructor's try/catch around <c>Parse</c>) rather than throwing.
        /// </summary>
        public static byte[] CidKeyedFontWithUnsupportedFdSelectFormat() =>
            BuildCidKeyedWithFdArray(fdSelectFormat: 99, omitFdSelectOperator: false);

        /// <summary>
        /// A well-formed CID-keyed CFF whose second FDArray entry (mapped to GID 1 via FDSelect) has no
        /// <c>Private</c> operator at all - legal CFF for an FD that needs no private-scoped data.
        /// <c>CffTable.LocalSubrsFor</c> must resolve that FD to <c>CffIndex.Empty</c> rather than an
        /// unassigned <c>default(CffIndex)</c>, whose <c>Count</c> throws.
        /// </summary>
        public static byte[] CidKeyedFontWithFdArrayEntryMissingPrivateDict() =>
            BuildCidKeyedWithFdArray(fdSelectFormat: 0, omitFdSelectOperator: false, fd1HasNoPrivateDict: true);

        private static byte[] BuildCidKeyedWithFdArray(int fdSelectFormat, bool omitFdSelectOperator,
            bool fd1HasNoPrivateDict = false)
        {
            byte[] header = [1, 0, 4, 4];
            byte[] nameIndex = BuildIndex([Encoding.ASCII.GetBytes("Synthetic")]);
            byte[] stringIndex = BuildIndex([]);
            byte[] globalSubrIndex = BuildIndex([]);
            byte[] charStringsIndex = BuildIndex([[32, 10], [32, 10]]); // GID0/GID1: push -107; callsubr

            // Each FD's Private DICT is exactly DictInt(2) + DictOp(19) = 2 bytes ("Subrs" at offset 2,
            // i.e. immediately following these two bytes) - fixed and known without any placeholder
            // pass, since both DictInt(2) and DictOp(19) always encode as a single byte.
            byte[] privateDictBytes = Concat(DictInt(2), DictOp(19));
            byte[] localSubrsFd0 = BuildIndex([[149, 149, 21, 14]]); // rmoveto(10,10); endchar
            byte[] localSubrsFd1 = BuildIndex([[159, 159, 21, 14]]); // rmoveto(20,20); endchar

            // Font DICT: DictInt(privSize=2) [1 byte] + DictInt16(privOffset placeholder) [3 bytes] +
            // DictOp(18) [1 byte] = 5 bytes, fixed regardless of the offset's real value. FD1 may
            // instead be an empty DICT (no Private operator at all) - fd1HasNoPrivateDict is constant
            // for the whole call, so this Font DICT's own byte length is still stable across the
            // placeholder/patch passes below, just 0 bytes instead of 5.
            byte[] FontDict(int privOffset) => Concat(DictInt(2), DictInt16(privOffset), DictOp(18));
            byte[] FontDict1(int privOffset) => fd1HasNoPrivateDict ? [] : FontDict(privOffset);

            byte[] fdArrayIndexPlaceholder = BuildIndex([FontDict(0), FontDict1(0)]);

            byte[] TopDict(int charStringsOffset, int fdArrayOffset, int fdSelectOffset) => Concat(
                DictInt(0), DictInt(0), DictInt(0), DictOp(1230), // ROS
                DictInt16(charStringsOffset), DictOp(17),
                DictInt16(fdArrayOffset), DictOp(1236),
                omitFdSelectOperator ? [] : Concat(DictInt16(fdSelectOffset), DictOp(1237)));

            byte[] topDictIndexPlaceholder = BuildIndex([TopDict(0, 0, 0)]);

            int charStringsOffset = header.Length + nameIndex.Length + topDictIndexPlaceholder.Length
                                     + stringIndex.Length + globalSubrIndex.Length;
            int fdArrayOffset = charStringsOffset + charStringsIndex.Length;
            int privFd0Offset = fdArrayOffset + fdArrayIndexPlaceholder.Length;
            int privFd1Offset = privFd0Offset + privateDictBytes.Length + localSubrsFd0.Length;
            int fdSelectOffset = fd1HasNoPrivateDict
                ? privFd1Offset
                : privFd1Offset + privateDictBytes.Length + localSubrsFd1.Length;

            byte[] fdArrayIndex = BuildIndex([FontDict(privFd0Offset), FontDict1(privFd1Offset)]);
            byte[] topDictIndex = BuildIndex([TopDict(charStringsOffset, fdArrayOffset, fdSelectOffset)]);

            byte[] fdSelect = fdSelectFormat switch
            {
                0 => [0, /* GID0 -> FD */ 0, /* GID1 -> FD */ 1],
                3 => [3, /* nRanges */ 0, 2, /* first=0,fd=0 */ 0, 0, 0, /* first=1,fd=1 */ 0, 1, 1, /* sentinel=2 */ 0, 2],
                _ => [(byte)fdSelectFormat] // an unsupported format - CffTable must fail soft reading just this byte
            };

            var trailer = omitFdSelectOperator ? [] : fdSelect;
            var fd1PrivateAndSubrs = fd1HasNoPrivateDict ? [] : Concat(privateDictBytes, localSubrsFd1);
            return Concat(header, nameIndex, topDictIndex, stringIndex, globalSubrIndex, charStringsIndex,
                fdArrayIndex, privateDictBytes, localSubrsFd0, fd1PrivateAndSubrs, trailer);
        }

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

        /// <summary>
        /// Always a fixed 3-byte (lead byte 28 + int16) DICT integer encoding, regardless of
        /// <paramref name="value"/>'s magnitude - unlike <see cref="DictInt"/>, whose encoded width
        /// varies with the value. Used only for a forward-pointing offset a DICT stores about data
        /// whose own position depends on that DICT's encoded length (see
        /// <see cref="CidKeyedFontWithFdArrayAndFdSelect"/>'s remarks) - the fixed width means the
        /// DICT's length is already known before the real offset value is.
        /// </summary>
        private static byte[] DictInt16(int value) => [28, (byte)(value >> 8), (byte)value];

        private static byte[] Concat(params byte[][] parts)
        {
            using var stream = new System.IO.MemoryStream();
            foreach (byte[] part in parts) stream.Write(part, 0, part.Length);
            return stream.ToArray();
        }
    }
}
