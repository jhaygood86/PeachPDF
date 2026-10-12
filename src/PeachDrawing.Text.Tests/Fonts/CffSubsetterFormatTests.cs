using System;
using System.IO;
using System.Linq;
using System.Text;
using PeachDrawing.Text.Internal.Fonts.OpenType;

namespace PeachDrawing.Text.Tests.Fonts
{
    /// <summary>Small byte-level cases for CFF structures that a large CID font does not contain.</summary>
    public class CffSubsetterFormatTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void Subset_CopiesEveryDefinedCharsetFormat(int format)
        {
            byte[] charset = format switch
            {
                0 => [0, 0, 1],
                1 => [1, 0, 1, 0],
                2 => [2, 0, 1, 0, 0],
                _ => throw new ArgumentOutOfRangeException(nameof(format)),
            };

            var subset = CffSubsetter.Subset(BuildOrdinaryCff(charset: charset), [1]);

            Assert.NotNull(subset);
            Assert.True(new CffTable(subset!, 0).IsSupported);
        }

        [Fact]
        public void Subset_HandlesPredefinedMissingAndEmptyCharsets()
        {
            foreach (var predefined in new[] { 0, 1, 2 })
            {
                var subset = CffSubsetter.Subset(
                    BuildOrdinaryCff(charsetOperand: CharsetOperand.ForPredefined(predefined)), [1]);
                Assert.NotNull(subset);
            }

            Assert.NotNull(CffSubsetter.Subset(BuildOrdinaryCff(), [1]));
            Assert.NotNull(CffSubsetter.Subset(
                BuildOrdinaryCff(charsetOperand: CharsetOperand.NoOperands), [1]));
            Assert.NotNull(CffSubsetter.Subset(
                BuildOrdinaryCff(charsetOperand: CharsetOperand.ZeroReal), [1]));
            Assert.NotNull(CffSubsetter.Subset(
                BuildOrdinaryCff(charset: [0, 0, 1], fixedOffsets: true), [1]));
        }

        [Fact]
        public void Subset_RejectsUnsupportedEncodingCharsetsAndOffsetsSafely()
        {
            Assert.Null(CffSubsetter.Subset(BuildOrdinaryCff(includeEncoding: true), [1]));
            Assert.Null(CffSubsetter.Subset(BuildOrdinaryCff(charset: [99]), [1]));
            Assert.Null(CffSubsetter.Subset(
                BuildOrdinaryCff(charsetOperand: CharsetOperand.NegativeOffset), [1]));

            // The padded Name INDEX pushes the CharStrings offset into DICT's two-byte positive form.
            var padded = BuildOrdinaryCff(namePadding: 300);
            Assert.NotNull(CffSubsetter.Subset(padded, [1]));
        }

        [Fact]
        public void Subset_FailsSoftForMissingAndEmptyTopDictEntries()
        {
            Assert.Null(CffSubsetter.Subset(BuildCffWithoutTopDict(), [0]));
            Assert.Null(CffSubsetter.Subset(BuildOrdinaryCff(emptyTopDict: true), [0]));
            Assert.Null(CffSubsetter.Subset(BuildOrdinaryCff(includeCharStrings: false), [0]));
            Assert.Null(CffSubsetter.Subset(BuildOrdinaryCff(emptyCharStringsOperand: true), [0]));
            Assert.Null(CffSubsetter.Subset(BuildOrdinaryCff(charStrings: []), [0]));

            // Past the quick length check, malformed INDEX bytes go through the fail-soft catch.
            Assert.Null(CffSubsetter.Subset([1, 0, 4, 4, 0, 1], [0]));
        }

        [Fact]
        public void Subset_RewritesCidFdSelectAndFallsBackForUnsupportedFormats()
        {
            foreach (var format in new[] { 0, 3 })
            {
                var cff = SyntheticCff.CidKeyedFontWithFdArrayAndFdSelect(format);
                var subset = CffSubsetter.Subset(cff, [0]);

                Assert.NotNull(subset);
                var rewritten = new CffTable(subset!, 0);
                Assert.True(rewritten.IsSupported);
                Assert.Equal([0x0E], rewritten.CharStrings[1].ToArray());
            }

            Assert.Null(CffSubsetter.Subset(
                SyntheticCff.CidKeyedFontWithUnsupportedFdSelectFormat(), [0]));
        }

        [Fact]
        public void Subset_RebuildsTopLevelPrivateDictionaryOffsets()
        {
            Assert.NotNull(CffSubsetter.Subset(
                BuildOrdinaryCff(privateDict: [139]), [1])); // Private DICT without local Subrs
            Assert.NotNull(CffSubsetter.Subset(
                BuildOrdinaryCff(privateDict: [19]), [1])); // Subrs operator with no operand
            Assert.NotNull(CffSubsetter.Subset(
                BuildOrdinaryCff(privateDict: [139], privateOperandCount: 1), [1]));
        }

        private readonly record struct CharsetOperand(int? Predefined, bool Empty, bool RealZero, bool Negative)
        {
            public static CharsetOperand ForPredefined(int value) => new(value, false, false, false);
            public static CharsetOperand NoOperands => new(null, true, false, false);
            public static CharsetOperand ZeroReal => new(null, false, true, false);
            public static CharsetOperand NegativeOffset => new(null, false, false, true);
        }

        private static byte[] BuildOrdinaryCff(
            byte[][]? charStrings = null,
            byte[]? charset = null,
            CharsetOperand? charsetOperand = null,
            bool includeEncoding = false,
            bool includeCharStrings = true,
            bool emptyCharStringsOperand = false,
            bool emptyTopDict = false,
            int namePadding = 0,
            byte[]? privateDict = null,
            int privateOperandCount = 2,
            bool fixedOffsets = false)
        {
            byte[] header = [1, 0, 4, 4];
            byte[] name = new byte[8 + namePadding];
            Encoding.ASCII.GetBytes("Subset").CopyTo(name, 0);
            byte[] nameIndex = BuildIndex([name]);
            byte[] stringIndex = BuildIndex([]);
            byte[] globalSubrIndex = BuildIndex([]);
            byte[] charStringsIndex = BuildIndex(charStrings ?? [[14], [14]]);
            byte[] privateBytes = privateDict ?? [];

            byte[] TopDict(int charsetOffset, int charStringsOffset, int privateOffset)
            {
                if (emptyTopDict)
                    return [];

                using var dict = new MemoryStream();

                if (includeEncoding)
                {
                    WriteDictInt(dict, 0);
                    dict.WriteByte(16);
                }

                if (charset is not null || charsetOperand is not null)
                {
                    if (charsetOperand is { } special)
                    {
                        if (special.Predefined is int predefined)
                            WriteDictInt(dict, predefined);
                        else if (special.Empty)
                        {
                            // No operands before the charset operator.
                        }
                        else if (special.RealZero)
                            dict.Write([30, 0x0F]);
                        else if (special.Negative)
                            WriteDictInt(dict, -109);
                        else
                            WriteDictInt(dict, charsetOffset);
                    }
                    else if (fixedOffsets)
                        WriteDictInt16(dict, charsetOffset);
                    else
                        WriteDictInt(dict, charsetOffset);

                    dict.WriteByte(15);
                }

                if (privateDict is not null)
                {
                    WriteDictInt(dict, privateDict.Length);
                    if (privateOperandCount > 1)
                        WriteDictInt(dict, privateOffset);
                    dict.WriteByte(18);
                }

                if (includeCharStrings)
                {
                    if (!emptyCharStringsOperand)
                    {
                        if (fixedOffsets)
                            WriteDictInt16(dict, charStringsOffset);
                        else
                            WriteDictInt(dict, charStringsOffset);
                    }

                    dict.WriteByte(17);
                }

                return dict.ToArray();
            }

            byte[] topDict = TopDict(0, 0, 0);
            byte[] topDictIndex = [];
            for (var attempt = 0; attempt < 8; attempt++)
            {
                topDictIndex = BuildIndex([topDict]);
                int dataStart = header.Length + nameIndex.Length + topDictIndex.Length
                                + stringIndex.Length + globalSubrIndex.Length;
                int charsetOffset = dataStart;
                int charStringsOffset = dataStart + (charset?.Length ?? 0);
                int privateOffset = charStringsOffset + charStringsIndex.Length;
                byte[] next = TopDict(charsetOffset, charStringsOffset, privateOffset);

                if (next.SequenceEqual(topDict))
                    break;

                topDict = next;
            }

            topDictIndex = BuildIndex([topDict]);
            int finalDataStart = header.Length + nameIndex.Length + topDictIndex.Length
                                 + stringIndex.Length + globalSubrIndex.Length;
            int finalCharsetOffset = finalDataStart;
            int finalCharStringsOffset = finalDataStart + (charset?.Length ?? 0);
            int finalPrivateOffset = finalCharStringsOffset + charStringsIndex.Length;
            if (!TopDict(finalCharsetOffset, finalCharStringsOffset, finalPrivateOffset).SequenceEqual(topDict))
                throw new InvalidOperationException("The synthetic CFF DICT offsets did not settle.");

            using var font = new MemoryStream();
            font.Write(header);
            font.Write(nameIndex);
            font.Write(topDictIndex);
            font.Write(stringIndex);
            font.Write(globalSubrIndex);
            if (charset is not null)
                font.Write(charset);
            font.Write(charStringsIndex);
            if (privateDict is not null)
                font.Write(privateBytes);
            return font.ToArray();
        }

        private static byte[] BuildCffWithoutTopDict()
        {
            using var font = new MemoryStream();
            font.Write([1, 0, 4, 4]);
            font.Write(BuildIndex([Encoding.ASCII.GetBytes("EmptyTopDict")]));
            font.Write(BuildIndex([]));
            font.Write(BuildIndex([]));
            font.Write(BuildIndex([]));
            return font.ToArray();
        }

        private static byte[] BuildIndex(byte[][] entries)
        {
            if (entries.Length == 0)
                return [0, 0];

            var total = 1;
            foreach (var entry in entries)
                total += entry.Length;
            var offSize = total <= 0xFF ? 1 : 2;

            using var index = new MemoryStream();
            index.WriteByte((byte)(entries.Length >> 8));
            index.WriteByte((byte)entries.Length);
            index.WriteByte((byte)offSize);

            var offset = 1;
            WriteOffset(index, offset, offSize);
            foreach (var entry in entries)
            {
                offset += entry.Length;
                WriteOffset(index, offset, offSize);
            }

            foreach (var entry in entries)
                index.Write(entry);
            return index.ToArray();
        }

        private static void WriteOffset(Stream stream, int value, int size)
        {
            for (var shift = (size - 1) * 8; shift >= 0; shift -= 8)
                stream.WriteByte((byte)(value >> shift));
        }

        private static void WriteDictInt(Stream stream, int value)
        {
            if (value is >= -107 and <= 107)
            {
                stream.WriteByte((byte)(value + 139));
            }
            else if (value is >= 108 and <= 1131)
            {
                var adjusted = value - 108;
                stream.WriteByte((byte)(247 + (adjusted >> 8)));
                stream.WriteByte((byte)adjusted);
            }
            else if (value is >= -1131 and <= -108)
            {
                var adjusted = -value - 108;
                stream.WriteByte((byte)(251 + (adjusted >> 8)));
                stream.WriteByte((byte)adjusted);
            }
            else if (value is >= short.MinValue and <= short.MaxValue)
            {
                WriteDictInt16(stream, value);
            }
            else
            {
                stream.WriteByte(29);
                Span<byte> bytes = stackalloc byte[4];
                System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, value);
                stream.Write(bytes);
            }
        }

        private static void WriteDictInt16(Stream stream, int value)
        {
            stream.WriteByte(28);
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
        }
    }
}
