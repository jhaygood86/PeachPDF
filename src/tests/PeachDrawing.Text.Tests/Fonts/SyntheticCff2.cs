namespace PeachDrawing.Text.Tests.Fonts
{
    /// <summary>
    /// Builds <c>CFF2</c> tables from hand-written charstrings, to test the reader and the interpreter against inputs a real font's own
    /// glyphs do not happen to contain (an operator the fixture never emits, a table damaged in one exact way). Every offset the table
    /// holds is computed here, so a test says only what it means to be different.
    /// </summary>
    internal static class SyntheticCff2
    {
        /// <summary>One Font DICT: the local subroutines of its Private DICT and its <c>vsindex</c> (null leaves the operator out).</summary>
        internal sealed record FontDict(byte[][]? LocalSubrs = null, int? VariationDataIndex = null);

        /// <summary>A region of a VariationStore on one axis: where the region starts, peaks and ends (normalized).</summary>
        internal readonly record struct Region(double Start, double Peak, double End);

        /// <summary>One number operand of a charstring, in the shortest form the tests need (-107 to 107 in one byte, else a 16-bit integer).</summary>
        internal static byte[] Num(int value) =>
            value is >= -107 and <= 107 ? [(byte)(value + 139)] : [28, (byte)(value >> 8), (byte)value];

        /// <summary>The bytes of a charstring made of operands (ints) and operator bytes (given as <see cref="Op"/>).</summary>
        internal static byte[] Cs(params object[] tokens)
        {
            var bytes = new List<byte>();
            foreach (var token in tokens)
            {
                switch (token)
                {
                    case int number:
                        bytes.AddRange(Num(number));
                        break;
                    case Op op:
                        bytes.AddRange(op.Bytes);
                        break;
                    case byte[] raw:
                        bytes.AddRange(raw);
                        break;
                    default:
                        throw new ArgumentException("Unknown token " + token);
                }
            }

            return bytes.ToArray();
        }

        /// <summary>An operator: its byte, or two for an escaped one.</summary>
        internal readonly record struct Op(params byte[] Bytes)
        {
            internal static readonly Op HStem = new(1);
            internal static readonly Op VMoveTo = new(4);
            internal static readonly Op RLineTo = new(5);
            internal static readonly Op HLineTo = new(6);
            internal static readonly Op VLineTo = new(7);
            internal static readonly Op RRCurveTo = new(8);
            internal static readonly Op CallSubr = new(10);
            internal static readonly Op Return = new(11);
            internal static readonly Op EndChar = new(14);
            internal static readonly Op VsIndex = new(15);
            internal static readonly Op Blend = new(16);
            internal static readonly Op HintMask = new(19);
            internal static readonly Op RMoveTo = new(21);
            internal static readonly Op HMoveTo = new(22);
            internal static readonly Op CallGSubr = new(29);
            internal static readonly Op HFlex = new(12, 34);
            internal static readonly Op Flex = new(12, 35);
            internal static readonly Op HFlex1 = new(12, 36);
            internal static readonly Op Flex1 = new(12, 37);
        }

        /// <summary>A CFF2 INDEX (32-bit count) of the given objects, each offset written in <paramref name="offSize"/> bytes.</summary>
        internal static byte[] Index(IReadOnlyList<byte[]> objects, int offSize = 2)
        {
            var bytes = new List<byte>();
            AddU32(bytes, (uint)objects.Count);
            if (objects.Count == 0)
                return bytes.ToArray();

            bytes.Add((byte)offSize);
            int offset = 1;
            AddOffset(bytes, offset, offSize);
            foreach (var item in objects)
            {
                offset += item.Length;
                AddOffset(bytes, offset, offSize);
            }

            foreach (var item in objects)
                bytes.AddRange(item);

            return bytes.ToArray();
        }

        /// <summary>
        /// A VariationStore (its 16-bit length, then the ItemVariationStore) with one axis: <paramref name="regions"/>, and one data set per
        /// entry of <paramref name="dataSets"/> that names the regions (by index) it holds.
        /// </summary>
        internal static byte[] VariationStore(Region[] regions, int[][] dataSets)
        {
            var store = new List<byte>();
            AddU16(store, 1); // format
            int regionListOffset = 8 + dataSets.Length * 4;
            AddU32(store, (uint)regionListOffset);
            AddU16(store, dataSets.Length);
            int dataOffset = regionListOffset + 4 + regions.Length * 6;
            var dataBytes = new List<byte>();
            foreach (var set in dataSets)
            {
                AddU32(store, (uint)(dataOffset + dataBytes.Count));
                AddU16(dataBytes, 0); // itemCount: a CFF2 VariationStore holds no items
                AddU16(dataBytes, 0); // wordDeltaCount
                AddU16(dataBytes, set.Length);
                foreach (int region in set)
                    AddU16(dataBytes, region);
            }

            AddU16(store, 1); // axisCount
            AddU16(store, regions.Length);
            foreach (var region in regions)
            {
                AddU16(store, F2Dot14(region.Start));
                AddU16(store, F2Dot14(region.Peak));
                AddU16(store, F2Dot14(region.End));
            }

            store.AddRange(dataBytes);

            var withLength = new List<byte>();
            AddU16(withLength, store.Count);
            withLength.AddRange(store);
            return withLength.ToArray();
        }

        /// <summary>An FDSelect of format 0, 3 or 4 that gives glyph <c>i</c> the Font DICT <paramref name="fontOfGlyph"/>[i].</summary>
        internal static byte[] FdSelect(int format, int[] fontOfGlyph)
        {
            var bytes = new List<byte> { (byte)format };
            if (format == 0)
            {
                foreach (int font in fontOfGlyph)
                    bytes.Add((byte)font);
                return bytes.ToArray();
            }

            var ranges = new List<(int First, int Font)>();
            for (int glyph = 0; glyph < fontOfGlyph.Length; glyph++)
            {
                if (glyph == 0 || fontOfGlyph[glyph] != fontOfGlyph[glyph - 1])
                    ranges.Add((glyph, fontOfGlyph[glyph]));
            }

            if (format == 3)
            {
                AddU16(bytes, ranges.Count);
                foreach (var (first, font) in ranges)
                {
                    AddU16(bytes, first);
                    bytes.Add((byte)font);
                }

                AddU16(bytes, fontOfGlyph.Length);
            }
            else
            {
                AddU32(bytes, (uint)ranges.Count);
                foreach (var (first, font) in ranges)
                {
                    AddU32(bytes, (uint)first);
                    AddU16(bytes, font);
                }

                AddU32(bytes, (uint)fontOfGlyph.Length);
            }

            return bytes.ToArray();
        }

        /// <summary>
        /// A whole CFF2 table. <paramref name="fonts"/> are the Font DICTs (one, empty, when left out); <paramref name="fdSelect"/> is the
        /// FDSelect (left out for a single Font DICT); <paramref name="variationStore"/> is a <see cref="VariationStore"/> (a font with none
        /// is not variable).
        /// </summary>
        internal static byte[] Table(byte[][] charStrings, byte[][]? globalSubrs = null, FontDict[]? fonts = null, byte[]? fdSelect = null,
            byte[]? variationStore = null, bool sharePrivate = false)
        {
            fonts ??= [new FontDict()];

            // Everything after the Top DICT, in the order it is written; the Top DICT names them by offset from the start of the table,
            // in operands of a fixed size so that the Top DICT's own length is known first.
            byte[] globalIndex = Index(globalSubrs ?? []);
            byte[] charStringIndex = Index(charStrings);
            byte[] fdSelectBytes = fdSelect ?? [];

            var fontDicts = new List<byte[]>();
            var privates = new List<byte[]>();
            var localIndexes = new List<byte[]>();
            foreach (var font in fonts)
            {
                var privateDict = new List<byte>();
                if (font.VariationDataIndex is { } vsindex)
                {
                    privateDict.AddRange(Num(vsindex));
                    privateDict.Add(22);
                }

                if (font.LocalSubrs is { } subrs)
                {
                    // The Local Subrs follow the Private DICT; their offset is from its start, and its own length includes this operand.
                    privateDict.AddRange(Fixed(0));
                    privateDict.Add(19);
                    localIndexes.Add(Index(subrs));
                }
                else
                {
                    localIndexes.Add([]);
                }

                privates.Add(privateDict.ToArray());
            }

            byte[] TopDict(int charStringsAt, int fdArrayAt, int fdSelectAt, int vstoreAt)
            {
                var top = new List<byte>();
                top.AddRange(Fixed(charStringsAt)); top.Add(17);
                top.AddRange(Fixed(fdArrayAt)); top.AddRange([12, 36]);
                if (fdSelect is not null)
                {
                    top.AddRange(Fixed(fdSelectAt));
                    top.AddRange([12, 37]);
                }

                if (variationStore is not null)
                {
                    top.AddRange(Fixed(vstoreAt));
                    top.Add(24);
                }

                return top.ToArray();
            }

            int topLength = TopDict(0, 0, 0, 0).Length;
            int at = 5 + topLength;
            at += globalIndex.Length;
            int vstoreOffset = at;
            at += variationStore?.Length ?? 0;
            int charStringsOffset = at;
            at += charStringIndex.Length;
            int fdSelectOffset = at;
            at += fdSelectBytes.Length;

            // The FDArray, then each Private DICT (with its Local Subrs after it).
            int fdArrayOffset = at;
            var fontDictEntries = new List<byte[]>();
            // A Font DICT is the Private operator with its two five-byte operands: eleven bytes each.
            int fdArrayLength = Index(Enumerable.Repeat(new byte[11], fonts.Length).ToArray(), 3).Length;
            int privateAt = fdArrayOffset + fdArrayLength;
            var privateBlocks = new List<byte>();
            for (int i = 0; i < fonts.Length; i++)
            {
                byte[] privateDict = privates[i];
                if (fonts[i].LocalSubrs is not null)
                {
                    // The Subrs operand is the distance from the start of the Private DICT to the INDEX, which is right after it.
                    var patched = privateDict.ToArray();
                    int operandAt = patched.Length - 6;
                    byte[] distance = Fixed(patched.Length);
                    Array.Copy(distance, 0, patched, operandAt, 5);
                    privateDict = patched;
                }

                var entry = new List<byte>();
                entry.AddRange(Fixed(privateDict.Length));
                // With sharePrivate every Font DICT names the first one's Private DICT (and so its Local Subrs), which a real font never does.
                entry.AddRange(Fixed(privateAt + (sharePrivate ? 0 : privateBlocks.Count)));
                entry.Add(18);
                fontDictEntries.Add(entry.ToArray());
                if (!sharePrivate || i == 0)
                {
                    privateBlocks.AddRange(privateDict);
                    privateBlocks.AddRange(localIndexes[i]);
                }
            }

            byte[] fdArray = Index(fontDictEntries, 3);
            byte[] topDict = TopDict(charStringsOffset, fdArrayOffset, fdSelectOffset, vstoreOffset);

            var table = new List<byte> { 2, 0, 5 };
            AddU16(table, topDict.Length);
            table.AddRange(topDict);
            table.AddRange(globalIndex);
            if (variationStore is not null)
                table.AddRange(variationStore);
            table.AddRange(charStringIndex);
            table.AddRange(fdSelectBytes);
            table.AddRange(fdArray);
            table.AddRange(privateBlocks);
            return table.ToArray();
        }

        private static byte[] Fixed(int value) => [29, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

        private static int F2Dot14(double value) => (int)Math.Round(value * 16384) & 0xFFFF;

        private static void AddU16(List<byte> bytes, int value)
        {
            bytes.Add((byte)(value >> 8));
            bytes.Add((byte)value);
        }

        private static void AddU32(List<byte> bytes, uint value)
        {
            bytes.Add((byte)(value >> 24));
            bytes.Add((byte)(value >> 16));
            bytes.Add((byte)(value >> 8));
            bytes.Add((byte)value);
        }

        private static void AddOffset(List<byte> bytes, int value, int offSize)
        {
            for (int shift = (offSize - 1) * 8; shift >= 0; shift -= 8)
                bytes.Add((byte)(value >> shift));
        }
    }
}
