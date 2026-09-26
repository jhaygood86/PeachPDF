#region PeachPDF - A .NET library for rendering HTML to PDF
//
// Reader for the OpenType `CFF2` table (variable CFF): enough of it to hand Type2CharstringInterpreter one glyph's charstring plus
// what running it needs. It is CFF's successor, in the same family of structures but not the same layout:
//
//   * a header that gives the length of the Top DICT, which follows it directly (no Name, Top DICT or String INDEX);
//   * INDEXes with a 32-bit count;
//   * every font a FDArray Font DICT (there is always one), with an FDSelect of formats 0, 3 or 4 when there is more than one;
//   * a VariationStore (an ItemVariationStore whose data sets hold no items, only the regions a `blend` operator scales by) at the
//     Top DICT's `vstore` operator, and a `vsindex` in each Private DICT that says which of its data sets a glyph starts with.
//
// A CFF2 font has no glyph names, no charset, no width and no Encoding, so the reader has nothing else to skip.
//
// https://learn.microsoft.com/en-us/typography/opentype/spec/cff2
//
#endregion

using PeachDrawing.Text.Internal.Fonts.OpenType.Variations;
using System;
using System.Collections.Generic;

namespace PeachDrawing.Text.Internal.Fonts.OpenType
{
    /// <summary>
    /// A font's <c>CFF2</c> table, parsed as far as <see cref="Type2CharstringInterpreter"/> needs: the CharStrings INDEX (one entry per
    /// glyph), the Global Subr INDEX, one <see cref="FontDictInfo"/> per Font DICT (its Local Subrs and its default <c>vsindex</c>), the
    /// FDSelect that says which Font DICT a glyph uses, and the VariationStore a <c>blend</c> reads its region scalars from.
    /// </summary>
    internal sealed class Cff2Table
    {
        /// <summary>The glyphs of a font are counted by a 16-bit number, so a CharStrings INDEX with more is not one.</summary>
        private const int MaxGlyphs = 65535;

        /// <summary>What one Font DICT contributes to running a glyph's charstring.</summary>
        internal readonly struct FontDictInfo(CffIndex localSubrs, int vsindex)
        {
            /// <summary>The Local Subrs INDEX of the Font DICT's Private DICT (empty when it has none).</summary>
            public CffIndex LocalSubrs { get; } = localSubrs;

            /// <summary>The <c>vsindex</c> of the Private DICT: the data set a charstring's <c>blend</c> starts with (0 when it says nothing).</summary>
            public int VariationDataIndex { get; } = vsindex;
        }

        private FontDictInfo[] _fonts = [];
        private ushort[]? _fdSelect;
        private ItemVariationStore? _store;

        public CffIndex CharStrings { get; private set; } = CffIndex.Empty;
        public CffIndex GlobalSubrs { get; private set; } = CffIndex.Empty;

        /// <summary>
        /// False when this table could not be read (it is cut off, its INDEXes or DICTs are damaged, or it has no CharStrings), which is the
        /// caller's cue to treat the font as having no outlines, as it does for an absent <c>glyf</c>.
        /// </summary>
        public bool IsSupported { get; private set; }

        /// <summary>The number of glyphs the CharStrings INDEX holds.</summary>
        public int GlyphCount => CharStrings.Count;

        internal Cff2Table(OpenTypeFontface face)
            : this(face.FontSource.Bytes, face.TableDictionary[TableTagNames.Cff2].Offset, face.TableDictionary[TableTagNames.Cff2].Length)
        {
        }

        /// <summary>
        /// Parses the CFF2 table that occupies <c>data[tableStart .. tableStart + tableLength)</c>. A table that is damaged in any way that
        /// reading it can run into makes <see cref="IsSupported"/> false and nothing else: this constructor does not throw for font data.
        /// </summary>
        internal Cff2Table(byte[] data, int tableStart, int tableLength)
        {
            try
            {
                if (tableStart < 0 || tableLength < 5 || (long)tableStart + tableLength > data.Length)
                    return;

                Parse(data, tableStart, tableStart + tableLength);
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException or FormatException or OverflowException or InvalidOperationException)
            {
                IsSupported = false;
            }
        }

        /// <summary>The Font DICT that <paramref name="glyph"/> uses, or false when the glyph is out of range or its FDSelect entry names no Font DICT.</summary>
        internal bool TryGetFontDict(int glyph, out FontDictInfo font)
        {
            font = default;
            if ((uint)glyph >= (uint)CharStrings.Count)
                return false;

            int index = _fdSelect is { } select ? select[glyph] : 0;
            if (index >= _fonts.Length)
                return false;

            font = _fonts[index];
            return true;
        }

        /// <summary>
        /// The factors <c>blend</c> scales its deltas by while data set <paramref name="variationDataIndex"/> is current, at the normalized
        /// location <paramref name="coordinates"/>: one per region of the data set. A font with no VariationStore is not variable, and has
        /// no data set for any index (so <c>blend</c> in one of its charstrings fails).
        /// </summary>
        internal double[]? GetRegionScalars(int variationDataIndex, ReadOnlySpan<double> coordinates)
        {
            if (_store is not { } store || (uint)variationDataIndex >= (uint)store.DataSetCount)
                return null;

            // A font is drawn at one location glyph after glyph, so the factors are kept for the last location asked about (the arrays are
            // never changed once made, and two threads that make the same one do no harm).
            var cache = _lastLocation;
            if (cache is null || !coordinates.SequenceEqual(cache.Coordinates))
            {
                cache = new LocationScalars(coordinates.ToArray(), store.DataSetCount);
                _lastLocation = cache;
            }

            var scalars = cache.Sets[variationDataIndex];
            if (scalars is null)
            {
                scalars = store.GetRegionScalars(variationDataIndex, coordinates) ?? NoDataSet;
                cache.Sets[variationDataIndex] = scalars;
            }

            return ReferenceEquals(scalars, NoDataSet) ? null : scalars;
        }

        /// <summary>The region factors of the data sets at one location, each worked out the first time it is asked for.</summary>
        private sealed class LocationScalars(double[] coordinates, int dataSets)
        {
            public double[] Coordinates { get; } = coordinates;
            public double[]?[] Sets { get; } = new double[dataSets][];
        }

        private static readonly double[] NoDataSet = [];
        private volatile LocationScalars? _lastLocation;

        private void Parse(byte[] data, int start, int end)
        {
            // major(1) minor(1) headerSize(1) topDictLength(2)
            if (data[start] != 2)
                return;

            int headerSize = data[start + 2];
            int topDictLength = (data[start + 3] << 8) | data[start + 4];
            int topDictStart = start + headerSize;
            if (headerSize < 5 || topDictStart > end || topDictLength > end - topDictStart)
                return;

            var top = CffDict.Parse(data, topDictStart, topDictStart + topDictLength, isCff2: true);

            int pos = topDictStart + topDictLength;
            GlobalSubrs = CffIndex.ReadCff2(data, ref pos, end);

            if (!top.TryGet(17, out var charStringsOp) || charStringsOp.Length == 0)
                return; // no CharStrings: nothing to draw

            int charStringsPos = OffsetIn(charStringsOp[0], start, end);
            var charStrings = CffIndex.ReadCff2(data, ref charStringsPos, end);
            if (charStrings.Count > MaxGlyphs)
                return;

            if (top.TryGet(24, out var vstoreOp) && vstoreOp.Length > 0)
            {
                // A VariationStore is a 16-bit length, then the ItemVariationStore that length covers.
                int vstoreAt = OffsetIn(vstoreOp[0], start, end);
                if (vstoreAt + 2 > end)
                    return;

                _store = ItemVariationStore.TryParse(data.AsSpan(start, end - start), vstoreAt + 2 - start);
                if (_store is null)
                    return;
            }

            // The FDArray is required and holds at least one Font DICT.
            if (!top.TryGet(1236, out var fdArrayOp) || fdArrayOp.Length == 0)
                return;

            int fdArrayPos = OffsetIn(fdArrayOp[0], start, end);
            var fdArray = CffIndex.ReadCff2(data, ref fdArrayPos, end);
            if (fdArray.Count == 0 || fdArray.Count > ushort.MaxValue)
                return;

            // The Font DICTs are slices of the table that cannot overlap, but any number of them may name one Private DICT, and any number of
            // Private DICTs one Local Subrs INDEX, so each of those is read once however often it is named.
            var privates = new Dictionary<(int Start, int Size), FontDictInfo>();
            var localSubrs = new Dictionary<int, CffIndex>();
            var fonts = new FontDictInfo[fdArray.Count];
            for (var i = 0; i < fonts.Length; i++)
            {
                var fontDict = CffDict.Parse(data, fdArray.StartOffset(i), fdArray.EndOffset(i), isCff2: true);
                fonts[i] = ReadPrivate(data, fontDict, start, end, privates, localSubrs);
            }

            if (top.TryGet(1237, out var fdSelectOp) && fdSelectOp.Length > 0)
                _fdSelect = ReadFdSelect(data, OffsetIn(fdSelectOp[0], start, end), end, charStrings.Count);
            else if (fonts.Length > 1)
                return; // several Font DICTs and no way to tell which a glyph uses

            CharStrings = charStrings;
            _fonts = fonts;
            IsSupported = true;
        }

        /// <summary>
        /// The absolute position of a table-relative offset operand, which has to lie in the table (or at its very end, where a Private DICT
        /// with nothing in it can be).
        /// </summary>
        private static int OffsetIn(double operand, int start, int end)
        {
            if (!(operand >= 0 && operand <= end - start))
                throw new FormatException("A CFF2 offset is not inside the table.");

            return start + (int)operand;
        }

        /// <summary>A Font DICT's Private DICT, of which what matters here is the Local Subrs INDEX and the <c>vsindex</c>.</summary>
        private static FontDictInfo ReadPrivate(byte[] data, CffDict fontDict, int start, int end,
            Dictionary<(int Start, int Size), FontDictInfo> privates, Dictionary<int, CffIndex> localSubrIndexes)
        {
            // A Font DICT with no Private DICT is legal: that Font DICT needs no local subroutines and blends from data set 0.
            if (!fontDict.TryGet(18, out var privateOp) || privateOp.Length < 2)
                return new FontDictInfo(CffIndex.Empty, 0);

            int size = (int)privateOp[0];
            int privateStart = OffsetIn(privateOp[1], start, end);
            if (size < 0 || size > end - privateStart)
                throw new FormatException("A CFF2 Private DICT is not inside the table.");

            if (privates.TryGetValue((privateStart, size), out var known))
                return known;

            var privateDict = CffDict.Parse(data, privateStart, privateStart + size, isCff2: true);

            int vsindex = privateDict.TryGet(22, out var vsindexOp) && vsindexOp.Length > 0 ? (int)vsindexOp[0] : 0;

            var localSubrs = CffIndex.Empty;
            if (privateDict.TryGet(19, out var subrsOp) && subrsOp.Length > 0)
            {
                // The offset of the Local Subrs is from the start of the Private DICT.
                if (!(subrsOp[0] >= 0 && subrsOp[0] < end - privateStart))
                    throw new FormatException("A CFF2 Local Subrs offset is not inside the table.");

                int subrsPos = privateStart + (int)subrsOp[0];
                if (!localSubrIndexes.TryGetValue(subrsPos, out localSubrs))
                {
                    int readAt = subrsPos;
                    localSubrs = CffIndex.ReadCff2(data, ref readAt, end);
                    localSubrIndexes[subrsPos] = localSubrs;
                }
            }

            var info = new FontDictInfo(localSubrs, vsindex);
            privates[(privateStart, size)] = info;
            return info;
        }

        /// <summary>
        /// Reads an FDSelect (format 0: one byte per glyph; format 3: ranges of 16-bit first glyphs with a one-byte Font DICT; format 4: the
        /// same with 32-bit first glyphs and 16-bit Font DICT indexes) into one array with an entry per glyph. A Font DICT index past the
        /// end of the FDArray is kept, so that a glyph that names one fails when it is drawn and its neighbours do not.
        /// </summary>
        private static ushort[] ReadFdSelect(byte[] data, int pos, int end, int glyphCount)
        {
            var select = new ushort[glyphCount];
            if (pos >= end)
                throw new FormatException("A CFF2 FDSelect is cut off.");

            int format = data[pos++];
            switch (format)
            {
                case 0:
                    if (glyphCount > end - pos)
                        throw new FormatException("A CFF2 FDSelect is cut off.");

                    for (var gid = 0; gid < glyphCount; gid++)
                        select[gid] = data[pos + gid];
                    break;

                case 3:
                case 4:
                {
                    // nRanges, then nRanges times (first glyph, Font DICT), then a sentinel that is one past the last glyph.
                    int firstSize = format == 3 ? 2 : 4;
                    int fdSize = format == 3 ? 1 : 2;
                    long rangeCount = format == 3 ? ReadUnsigned(data, ref pos, end, 2) : ReadUnsigned(data, ref pos, end, 4);
                    if (rangeCount * (firstSize + fdSize) + firstSize > end - pos)
                        throw new FormatException("A CFF2 FDSelect is cut off.");

                    long rangeStart = -1;
                    int rangeFd = 0;
                    for (long r = 0; r <= rangeCount; r++)
                    {
                        long first = ReadUnsigned(data, ref pos, end, firstSize);
                        if (rangeStart >= 0)
                        {
                            if (first < rangeStart)
                                throw new FormatException("A CFF2 FDSelect has ranges out of order.");

                            for (long gid = rangeStart; gid < first && gid < glyphCount; gid++)
                                select[gid] = (ushort)rangeFd;
                        }

                        if (r == rangeCount)
                            break;

                        rangeFd = (int)ReadUnsigned(data, ref pos, end, fdSize);
                        rangeStart = first;
                    }

                    break;
                }

                default:
                    throw new FormatException("Unsupported CFF2 FDSelect format.");
            }

            return select;
        }

        private static long ReadUnsigned(byte[] data, ref int pos, int end, int size)
        {
            if (size > end - pos)
                throw new FormatException("A CFF2 table is cut off.");

            long value = 0;
            for (var i = 0; i < size; i++)
                value = (value << 8) | data[pos++];

            return value;
        }
    }
}
