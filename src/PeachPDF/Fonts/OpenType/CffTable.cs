#region PeachPDF - A .NET library for rendering HTML to PDF
//
// Reader for the OpenType `CFF ` (Compact Font Format) table - enough of it
// to hand Type2CharstringInterpreter one glyph's charstring bytes plus the
// local/global subroutine INDEXes it needs to run them (issue #1117:
// background-clip: text needed vector glyph outlines for CFF-flavored
// ("OTTO") fonts, which carry no `glyf` table at all).
//
// Deliberately NOT read: `charset`/`Encoding`/the String INDEX. Glyph lookup
// here is always by GID, and per the CFF spec "the GID is the index into the
// CharStrings INDEX" - charset only matters for name/Unicode-to-GID lookup,
// which PeachPDF's shaping pipeline (cmap/GSUB) already does upstream of
// this table, exactly as it does for a glyf font's `loca`-indexed lookup.
//
// CID-keyed CFF (the `ROS` operator present in the Top DICT - common in CJK
// "Pro"/Source Han Sans/Noto Sans CJK OpenType-CFF builds, issue #1122) is
// also supported: FDArray (12 36, an INDEX of per-glyph Font DICTs, each
// with its own Private DICT/local Subrs, resolved exactly like the
// top-level Private DICT one level deeper) and FDSelect (12 37, a per-GID
// selector, format 0 or 3) pick the right local Subrs per glyph via
// LocalSubrsFor(gid). A CID font missing either operator, or one that fails
// to parse, still reports IsSupported = false rather than guessing at the
// wrong (top-level, likely absent) local subrs.
//
// https://adobe-type-tools.github.io/font-tech-notes/pdfs/5176.CFF.pdf
//
#endregion

using System;
using System.Collections.Generic;

namespace PeachPDF.Fonts.OpenType
{
    /// <summary>
    /// One CFF INDEX structure - a sequence of variable-length byte strings (glyph charstrings, a
    /// subroutine set, a DICT) - resolved to each entry's absolute byte range in the font's own
    /// backing array, so an entry can be read directly with no further offset arithmetic.
    /// </summary>
    internal readonly struct CffIndex
    {
        private readonly byte[] _data;
        private readonly int[] _absoluteOffsets; // Count+1 entries; entry i spans [_absoluteOffsets[i], _absoluteOffsets[i+1])

        public static readonly CffIndex Empty = new(Array.Empty<byte>(), [0]);

        private CffIndex(byte[] data, int[] absoluteOffsets)
        {
            _data = data;
            _absoluteOffsets = absoluteOffsets;
        }

        public int Count => _absoluteOffsets.Length - 1;

        public ReadOnlySpan<byte> this[int index] =>
            _data.AsSpan(_absoluteOffsets[index], _absoluteOffsets[index + 1] - _absoluteOffsets[index]);

        /// <summary>The absolute start offset of entry <paramref name="index"/> - used to locate a DICT's own byte range.</summary>
        public int StartOffset(int index) => _absoluteOffsets[index];

        /// <summary>The absolute end offset of entry <paramref name="index"/>.</summary>
        public int EndOffset(int index) => _absoluteOffsets[index + 1];

        /// <summary>
        /// Reads one INDEX structure from <paramref name="data"/> starting at <paramref name="pos"/>,
        /// advancing <paramref name="pos"/> past it (to the first byte after the INDEX's own object
        /// data - where the next structure, if any, begins).
        /// </summary>
        public static CffIndex Read(byte[] data, ref int pos)
        {
            int count = ReadU16(data, ref pos);
            if (count == 0) return Empty;

            int offSize = data[pos++];
            if (offSize is < 1 or > 4) throw new FormatException("Invalid CFF INDEX offSize.");

            var rawOffsets = new int[count + 1];
            for (var i = 0; i <= count; i++)
                rawOffsets[i] = ReadOffset(data, ref pos, offSize);

            // Offsets are 1-based, relative to the byte immediately following the offset array itself.
            int dataStart = pos - 1;
            var absoluteOffsets = new int[count + 1];
            for (var i = 0; i <= count; i++)
                absoluteOffsets[i] = dataStart + rawOffsets[i];

            pos = absoluteOffsets[count];
            return new CffIndex(data, absoluteOffsets);
        }

        /// <summary>A big-endian 16-bit read, advancing <paramref name="pos"/> past it - shared with <see cref="CffTable.ReadFdSelect"/>, which needs the same read for FDSelect's own 16-bit fields.</summary>
        internal static int ReadU16(byte[] data, ref int pos)
        {
            int v = (data[pos] << 8) | data[pos + 1];
            pos += 2;
            return v;
        }

        private static int ReadOffset(byte[] data, ref int pos, int offSize)
        {
            var v = 0;
            for (var i = 0; i < offSize; i++)
                v = (v << 8) | data[pos++];
            return v;
        }
    }

    /// <summary>
    /// A parsed CFF Top DICT or Private DICT - operator number to its operand list, exactly as they
    /// appeared (an escaped two-byte operator <c>12 n</c> is keyed as <c>1200 + n</c>, matching the
    /// operator names' own numbering in the CFF spec, e.g. <c>ROS</c> = <c>12 30</c> = key 1230).
    /// </summary>
    internal sealed class CffDict
    {
        private readonly Dictionary<int, double[]> _entries = [];

        public bool TryGet(int op, out double[] operands) => _entries.TryGetValue(op, out operands!);

        public bool Has(int op) => _entries.ContainsKey(op);

        /// <summary>Parses the DICT occupying <c>data[start..end)</c>.</summary>
        public static CffDict Parse(byte[] data, int start, int end)
        {
            var dict = new CffDict();
            var operands = new List<double>();
            var p = start;

            while (p < end)
            {
                int b0 = data[p];

                switch (b0)
                {
                    case <= 21:
                        p++;
                        int op = b0;
                        if (b0 == 12)
                        {
                            // A truncated DICT can end right after the escape prefix, with no second
                            // operator byte before `end` - reading data[p] unchecked would read into
                            // whatever data follows this DICT instead of failing cleanly.
                            if (p >= end)
                                return dict;
                            op = 1200 + data[p];
                            p++;
                        }
                        dict._entries[op] = operands.ToArray();
                        operands.Clear();
                        break;

                    case 28:
                        p++;
                        operands.Add((short)((data[p] << 8) | data[p + 1]));
                        p += 2;
                        break;

                    case 29:
                        p++;
                        operands.Add((data[p] << 24) | (data[p + 1] << 16) | (data[p + 2] << 8) | data[p + 3]);
                        p += 4;
                        break;

                    case 30:
                        p++;
                        operands.Add(ReadReal(data, ref p));
                        break;

                    case >= 32 and <= 246:
                        operands.Add(b0 - 139);
                        p++;
                        break;

                    case >= 247 and <= 250:
                        operands.Add((b0 - 247) * 256 + data[p + 1] + 108);
                        p += 2;
                        break;

                    case >= 251 and <= 254:
                        operands.Add(-(b0 - 251) * 256 - data[p + 1] - 108);
                        p += 2;
                        break;

                    default: // 255 and any other reserved byte are not valid DICT operand/operator lead bytes
                        p++;
                        break;
                }
            }

            return dict;
        }

        /// <summary>
        /// Decodes a DICT real-number operand (lead byte 30): packed BCD nibbles - '0'-'9', '.', 'E',
        /// 'E-', a reserved nibble (ignored), '-', and a terminating nibble - two nibbles per byte
        /// until the terminator.
        /// </summary>
        private static double ReadReal(byte[] data, ref int p)
        {
            var text = new System.Text.StringBuilder();
            var done = false;

            while (!done)
            {
                byte b = data[p++];
                done = AppendNibble(text, b >> 4) || AppendNibble(text, b & 0xF);
            }

            return double.TryParse(text.ToString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double value) ? value : 0;
        }

        /// <summary>Appends one decoded nibble to <paramref name="text"/>; returns true at the terminator nibble (0xf).</summary>
        private static bool AppendNibble(System.Text.StringBuilder text, int nibble)
        {
            switch (nibble)
            {
                case <= 9: text.Append((char)('0' + nibble)); break;
                case 0xa: text.Append('.'); break;
                case 0xb: text.Append('E'); break;
                case 0xc: text.Append("E-"); break;
                case 0xe: text.Append('-'); break;
                case 0xf: return true;
                // 0xd is reserved - ignored.
            }
            return false;
        }
    }

    /// <summary>
    /// A font's `CFF ` table, parsed only as far as <see cref="Type2CharstringInterpreter"/> needs:
    /// the CharStrings INDEX (one entry per glyph, by GID), the Global Subr INDEX, and either the
    /// top-level Private DICT's Local Subr INDEX (an ordinary font) or, for a CID-keyed font, each
    /// FDArray Font DICT's own Local Subr INDEX plus the FDSelect per-GID map - see
    /// <see cref="LocalSubrsFor"/>.
    /// </summary>
    internal sealed class CffTable
    {
        public CffIndex CharStrings { get; private set; } = CffIndex.Empty;
        public CffIndex GlobalSubrs { get; private set; } = CffIndex.Empty;
        public CffIndex LocalSubrs { get; private set; } = CffIndex.Empty;

        private CffIndex[]? _fdLocalSubrs;
        private byte[]? _fdSelect;

        /// <summary>Whether the Top DICT carries a <c>ROS</c> operator - a CID-keyed font.</summary>
        public bool IsCidKeyed { get; private set; }

        /// <summary>
        /// False when this table could not be parsed at all, uses legacy Type 1 charstrings
        /// (<c>CharstringType</c> other than 2), or is CID-keyed but missing/malformed
        /// <c>FDArray</c>/<c>FDSelect</c> - the caller's cue to treat this font as having no usable
        /// glyph outlines, same as an absent `glyf` table.
        /// </summary>
        public bool IsSupported { get; private set; }

        /// <summary>
        /// The Local Subrs INDEX that applies to <paramref name="gid"/>: the top-level
        /// <see cref="LocalSubrs"/> for an ordinary font, or the FDSelect-chosen FDArray entry's own
        /// Local Subrs for a CID-keyed one. A charstring's subroutine calls never cross which
        /// glyph/FD they belong to, so this is resolved once per glyph decode rather than per
        /// <c>callsubr</c>.
        /// </summary>
        public CffIndex LocalSubrsFor(int gid) =>
            IsCidKeyed && _fdSelect is { } fdSelect && gid >= 0 && gid < fdSelect.Length
                ? _fdLocalSubrs![fdSelect[gid]]
                : LocalSubrs;

        public CffTable(OpenTypeFontface face)
            : this(face.FontSource.Bytes, face.TableDictionary[TableTagNames.Cff].Offset)
        {
        }

        /// <summary>
        /// Parses the CFF table occupying <paramref name="data"/> starting at
        /// <paramref name="tableStart"/> - split out from the <see cref="OpenTypeFontface"/>-based
        /// constructor above so this binary-format logic is directly testable against a small
        /// synthetic byte array, with no fake fontface needed.
        /// </summary>
        internal CffTable(byte[] data, int tableStart)
        {
            try
            {
                Parse(data, tableStart);
            }
            catch (Exception)
            {
                // A malformed/truncated CFF table degrades to "no outlines from this font" rather
                // than breaking the whole render - same fail-soft contract GlyphOutlineDecoder's own
                // TryGetGlyphOutline has for a malformed glyf glyph.
                IsSupported = false;
            }
        }

        private void Parse(byte[] data, int tableStart)
        {
            int hdrSize = data[tableStart + 2]; // major(1) minor(1) hdrSize(1) offSize(1)
            int p = tableStart + hdrSize;

            CffIndex.Read(data, ref p); // Name INDEX - contents unused
            var topDictIndex = CffIndex.Read(data, ref p);
            CffIndex.Read(data, ref p); // String INDEX - unused, see file header remarks
            GlobalSubrs = CffIndex.Read(data, ref p);

            if (topDictIndex.Count == 0) return; // no font in this table - IsSupported stays false

            var topDict = CffDict.Parse(data, topDictIndex.StartOffset(0), topDictIndex.EndOffset(0));

            // CharstringType (12 6) defaults to 2 (Type 2) when absent - only reject an explicit,
            // different value (legacy Type 1 charstrings, effectively never seen in the wild).
            if (topDict.TryGet(1206, out var charstringType) && charstringType is [var typeValue, ..] && (int)typeValue != 2)
                return;

            IsCidKeyed = topDict.Has(1230); // ROS

            if (!topDict.TryGet(17, out var charStringsOp) || charStringsOp.Length == 0)
                return; // no CharStrings offset - unusable

            int charStringsPos = tableStart + (int)charStringsOp[0];
            CharStrings = CffIndex.Read(data, ref charStringsPos);

            if (topDict.TryGet(18, out var privateOp) && privateOp.Length >= 2)
                LocalSubrs = ReadLocalSubrs(data, tableStart + (int)privateOp[1], (int)privateOp[0]);

            if (!IsCidKeyed)
            {
                IsSupported = true;
                return;
            }

            // FDArray (12 36) and FDSelect (12 37) are both required to resolve a CID-keyed glyph's
            // local Subrs - without either, IsSupported stays false rather than guessing.
            if (!topDict.TryGet(1236, out var fdArrayOp) || fdArrayOp.Length == 0 ||
                !topDict.TryGet(1237, out var fdSelectOp) || fdSelectOp.Length == 0)
                return;

            int fdArrayPos = tableStart + (int)fdArrayOp[0];
            var fdArray = CffIndex.Read(data, ref fdArrayPos);

            // Every slot is seeded with CffIndex.Empty (never the default(CffIndex) an unassigned array
            // element would otherwise hold) - a Font DICT with no Private operator is legal CFF (that FD
            // simply needs no private-scoped data), and CffIndex.Count on a default-initialized instance
            // (null backing fields) throws, which Type2CharstringInterpreter's callsubr would hit the
            // moment that FD's own glyph called a local subroutine.
            var fdLocalSubrs = new CffIndex[fdArray.Count];
            Array.Fill(fdLocalSubrs, CffIndex.Empty);
            for (var i = 0; i < fdArray.Count; i++)
            {
                var fontDict = CffDict.Parse(data, fdArray.StartOffset(i), fdArray.EndOffset(i));
                if (fontDict.TryGet(18, out var fdPrivateOp) && fdPrivateOp.Length >= 2)
                    fdLocalSubrs[i] = ReadLocalSubrs(data, tableStart + (int)fdPrivateOp[1], (int)fdPrivateOp[0]);
            }

            _fdLocalSubrs = fdLocalSubrs;
            _fdSelect = ReadFdSelect(data, tableStart + (int)fdSelectOp[0], CharStrings.Count);
            IsSupported = true;
        }

        /// <summary>Resolves a Private DICT's own Local Subrs INDEX - shared by the top-level Private DICT and each FDArray entry's own.</summary>
        private static CffIndex ReadLocalSubrs(byte[] data, int privOffset, int privSize)
        {
            var privateDict = CffDict.Parse(data, privOffset, privOffset + privSize);
            if (!privateDict.TryGet(19, out var subrsOp) || subrsOp.Length == 0)
                return CffIndex.Empty;

            int localSubrsPos = privOffset + (int)subrsOp[0];
            return CffIndex.Read(data, ref localSubrsPos);
        }

        /// <summary>
        /// Reads an FDSelect table (format 0: a flat one-byte-per-GID array; format 3: a sorted list of
        /// (first GID, FD index) ranges terminated by a sentinel GID) into one flat per-GID array -
        /// simpler for <see cref="LocalSubrsFor"/> to index than re-deriving the range each lookup, and
        /// cheap even for a large CJK glyph set.
        /// </summary>
        private static byte[] ReadFdSelect(byte[] data, int pos, int glyphCount)
        {
            var fdSelect = new byte[glyphCount];
            int format = data[pos++];

            switch (format)
            {
                case 0:
                    for (var gid = 0; gid < glyphCount; gid++)
                        fdSelect[gid] = data[pos + gid];
                    break;

                case 3:
                {
                    int nRanges = CffIndex.ReadU16(data, ref pos);

                    int rangeStart = -1;
                    byte rangeFd = 0;
                    for (var r = 0; r < nRanges; r++)
                    {
                        int first = CffIndex.ReadU16(data, ref pos);
                        byte fd = data[pos];
                        pos += 1;

                        if (rangeStart >= 0)
                            for (var gid = rangeStart; gid < first && gid < glyphCount; gid++)
                                fdSelect[gid] = rangeFd;

                        rangeStart = first;
                        rangeFd = fd;
                    }

                    int sentinel = CffIndex.ReadU16(data, ref pos);
                    if (rangeStart >= 0)
                        for (var gid = rangeStart; gid < sentinel && gid < glyphCount; gid++)
                            fdSelect[gid] = rangeFd;
                    break;
                }

                default:
                    throw new FormatException("Unsupported FDSelect format.");
            }

            return fdSelect;
        }
    }
}
