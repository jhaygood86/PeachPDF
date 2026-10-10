using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace PeachDrawing.Text.Internal.Fonts.OpenType
{
    /// <summary>
    /// Rewrites a CFF table keeping only the outlines a document actually
    /// draws.
    /// </summary>
    /// <remarks>
    /// A CJK font carries tens of thousands of glyphs, and a document that
    /// prints a name uses a handful. Without this the whole face is embedded:
    /// Noto Sans CJK put 13MB into a one page PDF.
    ///
    /// Glyph ids are left exactly as they are. Every other approach to
    /// subsetting a CFF renumbers the glyphs, and then has to renumber the
    /// charset, the FDSelect and the CID mapping the PDF was built against to
    /// match. Instead the CharStrings index keeps its original length and the
    /// glyphs the document does not use are replaced by an empty charstring,
    /// which is one byte. The subroutines are treated the same way: they are
    /// called by number, so the ones nothing reaches are emptied rather than
    /// removed. Nothing outside those two indexes has to be understood, only
    /// moved -- the charset, FDSelect and private dictionaries are copied
    /// across untouched.
    ///
    /// What does have to be understood is where those blocks are pointed at
    /// from, because moving them invalidates every offset in the top
    /// dictionary and in each font dictionary. Those dictionaries are
    /// therefore rebuilt with their offsets written at a fixed width, so a
    /// single layout pass settles: the sizes cannot change when the values do.
    /// </remarks>
    internal static class CffSubsetter
    {
        private const int OpCharset = 15;
        private const int OpEncoding = 16;
        private const int OpCharStrings = 17;
        private const int OpPrivate = 18;
        private const int OpSubrs = 19;
        private const int OpFdArray = 1236;
        private const int OpFdSelect = 1237;

        /// <summary>Type 2 endchar: draws nothing.</summary>
        private static readonly byte[] EmptyCharstring = [0x0E];

        /// <summary>Type 2 return: does nothing and comes straight back.</summary>
        private static readonly byte[] EmptySubr = [0x0B];

        /// <summary>
        /// Returns a CFF holding only <paramref name="usedGlyphs"/>, or null
        /// when the font is not one this can rewrite safely.
        /// </summary>
        public static byte[]? Subset(byte[] cff, IReadOnlyCollection<int> usedGlyphs)
        {
            ArgumentNullException.ThrowIfNull(cff);
            ArgumentNullException.ThrowIfNull(usedGlyphs);

            try
            {
                return SubsetCore(cff, usedGlyphs);
            }
            catch (Exception)
            {
                // A font this cannot take apart is embedded whole, as it was
                // before: a larger PDF is a far better outcome than a broken
                // one.
                return null;
            }
        }

        private static byte[]? SubsetCore(byte[] cff, IReadOnlyCollection<int> usedGlyphs)
        {
            if (cff.Length < 4)
                return null;

            // Header: major, minor, hdrSize, offSize.
            int pos = cff[2];

            var nameIndex = ReadIndexBytes(cff, ref pos);
            var topDictIndex = CffIndex.Read(cff, ref pos);
            var stringIndex = ReadIndexBytes(cff, ref pos);
            var globalSubrs = ReadIndexItems(cff, ref pos);

            if (topDictIndex.Count < 1)
                return null;

            var topDict = topDictIndex[0].ToArray();
            var entries = ReadDictEntries(topDict);

            // A font with an Encoding is not CID keyed and is rare enough in a
            // PDF that it is not worth the risk of moving one.
            if (HasEntry(entries, OpEncoding))
                return null;

            if (!TryGetOffset(entries, topDict, OpCharStrings, out var charStringsOffset))
                return null;

            var charStringsPos = charStringsOffset;
            var charStrings = CffIndex.Read(cff, ref charStringsPos);
            var glyphCount = charStrings.Count;

            if (glyphCount == 0)
                return null;

            var isCid = HasEntry(entries, OpFdArray) && HasEntry(entries, OpFdSelect);

            var charsetBytes = isCid ? BuildIdentityCharset(glyphCount) : ReadCharset(cff, entries, topDict, glyphCount);
            var fdSelectBytes = isCid ? ReadFdSelect(cff, entries, topDict, glyphCount) : null;
            var fdForGlyph = isCid ? ParseFdSelect(fdSelectBytes!, glyphCount) : null;

            var privates = new List<PrivateBlock>();
            List<byte[]>? fontDicts = null;

            if (isCid)
            {
                var fdArrayPos = GetOffset(entries, topDict, OpFdArray);
                var fdArray = CffIndex.Read(cff, ref fdArrayPos);
                fontDicts = [];

                for (var i = 0; i < fdArray.Count; i++)
                {
                    var fontDict = fdArray[i].ToArray();
                    fontDicts.Add(fontDict);
                    privates.Add(ReadPrivate(cff, ReadDictEntries(fontDict), fontDict));
                }
            }
            else
            {
                privates.Add(ReadPrivate(cff, entries, topDict));
            }

            var keep = new bool[glyphCount];

            // Glyph 0 is .notdef and always stays.
            keep[0] = true;

            foreach (var glyph in usedGlyphs)
            {
                if (glyph >= 0 && glyph < glyphCount)
                    keep[glyph] = true;
            }

            // Which subroutines the surviving glyphs can still reach. When any
            // charstring cannot be followed, every subroutine is kept: one
            // wrongly dropped would silently mis-draw a glyph.
            var usage = new CffSubrUsage(globalSubrs, fd => privates[fd].LocalSubrs);

            for (var gid = 0; gid < glyphCount && usage.Complete; gid++)
            {
                if (keep[gid])
                    usage.Add(charStrings[gid], fdForGlyph is null ? 0 : fdForGlyph[gid]);
            }

            var newCharStrings = new List<byte[]>(glyphCount);

            for (var gid = 0; gid < glyphCount; gid++)
                newCharStrings.Add(keep[gid] ? charStrings[gid].ToArray() : EmptyCharstring);

            var newGlobalSubrs = usage.Complete ? Blank(globalSubrs, usage.GlobalSubrs) : globalSubrs;

            if (usage.Complete)
            {
                for (var fd = 0; fd < privates.Count; fd++)
                {
                    privates[fd] = privates[fd] with
                    {
                        LocalSubrs = Blank(privates[fd].LocalSubrs, usage.LocalSubrsForFd(fd)),
                    };
                }
            }

            return Write(
                nameIndex, stringIndex, newGlobalSubrs, topDict, entries,
                charsetBytes, fdSelectBytes, newCharStrings, fontDicts, privates);
        }

        private static List<byte[]> Blank(IReadOnlyList<byte[]> subrs, IReadOnlySet<int> used)
        {
            var result = new List<byte[]>(subrs.Count);

            for (var i = 0; i < subrs.Count; i++)
                result.Add(used.Contains(i) ? subrs[i] : EmptySubr);

            return result;
        }

        /// <summary>A private dictionary and the local subroutines it points at.</summary>
        /// <remarks>
        /// The dictionary is rebuilt rather than copied, because its Subrs
        /// operand is an offset from the dictionary to the subroutines and in
        /// a real font they are nowhere near each other -- Noto CJK keeps all
        /// eighteen dictionaries together and the subroutines elsewhere.
        /// Writing the subroutines straight after their dictionary is what
        /// makes the rest of this file's layout free to change, and it is only
        /// possible if that one operand can be corrected.
        /// </remarks>
        private readonly record struct PrivateBlock(byte[] Dict)
        {
            public int DictLength => Dict.Length;

            public List<byte[]> LocalSubrs { get; init; } = [];

            public bool HasSubrs { get; init; }
        }

        private static PrivateBlock ReadPrivate(
            byte[] cff, List<DictEntry> entries, byte[] dict)
        {
            if (!TryGetEntry(entries, OpPrivate, out var entry))
                return new PrivateBlock([]);

            var operands = ReadOperands(dict, entry.Start, entry.End);

            if (operands.Count < 2)
                return new PrivateBlock([]);

            var size = (int)operands[0];
            var offset = (int)operands[1];

            var privateDict = cff.AsSpan(offset, size).ToArray();
            var privateEntries = ReadDictEntries(privateDict);

            if (!TryGetEntry(privateEntries, OpSubrs, out var subrsEntry))
                return new PrivateBlock(privateDict);

            var subrsOperands = ReadOperands(privateDict, subrsEntry.Start, subrsEntry.End);

            if (subrsOperands.Count == 0)
                return new PrivateBlock(privateDict);

            var subrsPos = offset + (int)subrsOperands[0];
            var subrs = ReadIndexItems(cff, ref subrsPos);

            // Built once to learn its length, then again to state it: the
            // subroutines follow the dictionary, so the offset to them is the
            // dictionary's own length. The operand is a fixed width, so the
            // second build is the same size as the first.
            var length = BuildPrivateDict(privateDict, privateEntries, 0).Length;

            return new PrivateBlock(BuildPrivateDict(privateDict, privateEntries, length))
            {
                LocalSubrs = subrs,
                HasSubrs = true,
            };
        }

        private static byte[] BuildPrivateDict(
            byte[] privateDict, List<DictEntry> entries, int subrsAt)
        {
            using var dict = new MemoryStream();

            foreach (var (op, start, end) in entries)
            {
                if (op == OpSubrs)
                    WriteFixedInt(dict, subrsAt);
                else
                    dict.Write(privateDict.AsSpan(start, end - start));

                WriteOperator(dict, op);
            }

            return dict.ToArray();
        }

        private static int[] ParseFdSelect(byte[] fdSelect, int glyphCount)
        {
            var result = new int[glyphCount];

            switch (fdSelect[0])
            {
                case 0:
                    for (var gid = 0; gid < glyphCount; gid++)
                        result[gid] = fdSelect[1 + gid];

                    return result;

                case 3:
                    {
                        var rangeCount = BinaryPrimitives.ReadUInt16BigEndian(fdSelect.AsSpan(1));
                        var at = 3;

                        for (var i = 0; i < rangeCount; i++)
                        {
                            var first = BinaryPrimitives.ReadUInt16BigEndian(fdSelect.AsSpan(at));
                            var fd = fdSelect[at + 2];
                            var next = BinaryPrimitives.ReadUInt16BigEndian(fdSelect.AsSpan(at + 3));

                            for (var gid = first; gid < next && gid < glyphCount; gid++)
                                result[gid] = fd;

                            at += 3;
                        }

                        return result;
                    }

                default:
                    throw new InvalidOperationException($"Unknown FDSelect format {fdSelect[0]}.");
            }
        }

        /// <summary>
        /// The identity charset: glyph <c>i</c> has CID <c>i</c>, for every glyph the font has.
        /// </summary>
        /// <remarks>
        /// A CID-keyed CFF's own charset is a GID-to-CID map, and in a real font it is not the
        /// identity - Noto Sans SC sends GID 0 to CID 1, and ranges CIDs from there. This subset keeps
        /// glyph ids where they are, and the PDF addresses the font by those same ids with
        /// <c>/CIDToGIDMap /Identity</c>, so the two have to agree or a viewer resolves every CID
        /// through the wrong table and draws nothing. Copying the original charset across - which is
        /// what this did at first - happens to work only for a font whose charset already is the
        /// identity.
        ///
        /// Format 0 costs two bytes per glyph, uncompressed and well under the size of the outlines it
        /// sits beside, so there is nothing to gain from the range-compressed formats here.
        /// </remarks>
        private static byte[] BuildIdentityCharset(int glyphCount)
        {
            var charset = new byte[1 + ((glyphCount - 1) * 2)];

            // Format 0: a plain list of SIDs, one per glyph after the first.
            charset[0] = 0;

            for (var gid = 1; gid < glyphCount; gid++)
            {
                charset[1 + ((gid - 1) * 2)] = (byte)((gid >> 8) & 0xFF);
                charset[2 + ((gid - 1) * 2)] = (byte)(gid & 0xFF);
            }

            return charset;
        }

        private static byte[]? ReadCharset(
            byte[] cff, List<DictEntry> entries, byte[] dict, int glyphCount)
        {
            if (!TryGetEntry(entries, OpCharset, out var entry))
                return null;

            var operands = ReadOperands(dict, entry.Start, entry.End);

            if (operands.Count == 0)
                return null;

            var offset = (int)operands[0];

            // 0, 1 and 2 name the predefined charsets rather than an offset.
            if (offset is 0 or 1 or 2)
                return null;

            var format = cff[offset];
            int length;

            switch (format)
            {
                case 0:
                    length = 1 + ((glyphCount - 1) * 2);
                    break;

                case 1:
                case 2:
                    {
                        var rangeSize = format == 1 ? 3 : 4;
                        var covered = 1;
                        var at = offset + 1;

                        while (covered < glyphCount)
                        {
                            var left = format == 1
                                ? cff[at + 2]
                                : BinaryPrimitives.ReadUInt16BigEndian(cff.AsSpan(at + 2));

                            covered += left + 1;
                            at += rangeSize;
                        }

                        length = at - offset;
                        break;
                    }

                default:
                    throw new InvalidOperationException($"Unknown charset format {format}.");
            }

            return cff.AsSpan(offset, length).ToArray();
        }

        private static byte[] ReadFdSelect(
            byte[] cff, List<DictEntry> entries, byte[] dict, int glyphCount)
        {
            var offset = GetOffset(entries, dict, OpFdSelect);
            var format = cff[offset];

            var length = format switch
            {
                0 => 1 + glyphCount,
                // Format 3: the range count, that many three byte ranges, then
                // the sentinel glyph id.
                3 => 3 + (BinaryPrimitives.ReadUInt16BigEndian(cff.AsSpan(offset + 1)) * 3) + 2,
                _ => throw new InvalidOperationException($"Unknown FDSelect format {format}."),
            };

            return cff.AsSpan(offset, length).ToArray();
        }

        private static byte[] Write(
            byte[] nameIndex,
            byte[] stringIndex,
            List<byte[]> globalSubrs,
            byte[] topDict,
            List<DictEntry> topEntries,
            byte[]? charset,
            byte[]? fdSelect,
            List<byte[]> charStrings,
            List<byte[]>? fontDicts,
            List<PrivateBlock> privates)
        {
            var globalSubrsIndex = WriteIndex(globalSubrs);
            var charStringsIndex = WriteIndex(charStrings);

            var privateBytes = new byte[privates.Count][];

            for (var i = 0; i < privates.Count; i++)
            {
                if (!privates[i].HasSubrs)
                {
                    privateBytes[i] = privates[i].Dict;
                    continue;
                }

                var subrs = WriteIndex(privates[i].LocalSubrs);
                var block = new byte[privates[i].Dict.Length + subrs.Length];

                privates[i].Dict.CopyTo(block, 0);
                subrs.CopyTo(block, privates[i].Dict.Length);

                privateBytes[i] = block;
            }

            // Every offset below is written at a fixed five bytes, so the
            // dictionaries are the same size whatever the offsets turn out to
            // be and one pass settles the layout.
            var isCid = fontDicts is not null;
            var hasTopPrivateBlock = !isCid && privateBytes.Length > 0 && privateBytes[0].Length > 0;

            const int headerLength = 4;

            // Built with placeholder offsets purely to measure: the operands
            // are a fixed width, so the real one is the same size, and the
            // index around it is measured rather than predicted because its
            // own offsets shrink to fit what they point at. Preserve the
            // final layout's nullability too: a predefined charset or absent
            // top-level Private block keeps its original DICT operand bytes.
            var placeholderTopDict = BuildTopDict(
                topDict, topEntries,
                charset is null ? null : 0,
                HasEntry(topEntries, OpFdSelect) ? 0 : null,
                0,
                isCid ? 0 : null,
                hasTopPrivateBlock ? (0, 0) : null);

            var topDictIndexLength = WriteIndex([placeholderTopDict]).Length;

            var fdArrayPlaceholder = isCid
                ? WriteIndex(BuildFontDicts(fontDicts!, privates, null)).Length
                : 0;

            var at = headerLength + nameIndex.Length + topDictIndexLength
                     + stringIndex.Length + globalSubrsIndex.Length;

            var charsetAt = at;
            at += charset?.Length ?? 0;

            var fdSelectAt = at;
            at += fdSelect?.Length ?? 0;

            var charStringsAt = at;
            at += charStringsIndex.Length;

            var fdArrayAt = at;
            at += fdArrayPlaceholder;

            var privateAt = new int[privates.Count];

            for (var i = 0; i < privates.Count; i++)
            {
                privateAt[i] = at;
                at += privateBytes[i].Length;
            }

            var fdArrayIndex = isCid ? WriteIndex(BuildFontDicts(fontDicts!, privates, privateAt)) : [];

            if (isCid && fdArrayIndex.Length != fdArrayPlaceholder)
                throw new InvalidOperationException("The font dictionaries did not settle to a fixed size.");

            var newTopDict = BuildTopDict(
                topDict, topEntries,
                charset is null ? null : charsetAt,
                fdSelect is null ? null : fdSelectAt,
                charStringsAt,
                isCid ? fdArrayAt : null,
                hasTopPrivateBlock ? (privates[0].DictLength, privateAt[0]) : null);

            if (newTopDict.Length != placeholderTopDict.Length)
                throw new InvalidOperationException("The top dictionary did not settle to a fixed size.");

            using var stream = new MemoryStream(at);

            stream.WriteByte(1);            // major
            stream.WriteByte(0);            // minor
            stream.WriteByte(headerLength); // hdrSize
            stream.WriteByte(4);            // offSize

            stream.Write(nameIndex);
            stream.Write(WriteIndex([newTopDict]));
            stream.Write(stringIndex);
            stream.Write(globalSubrsIndex);

            if (charset is not null)
                stream.Write(charset);

            if (fdSelect is not null)
                stream.Write(fdSelect);

            stream.Write(charStringsIndex);

            if (isCid)
                stream.Write(fdArrayIndex);

            foreach (var block in privateBytes)
                stream.Write(block);

            return stream.ToArray();
        }

        private static List<byte[]> BuildFontDicts(
            List<byte[]> fontDicts, List<PrivateBlock> privates, int[]? privateAt)
        {
            var result = new List<byte[]>(fontDicts.Count);

            for (var i = 0; i < fontDicts.Count; i++)
            {
                var entries = ReadDictEntries(fontDicts[i]);
                using var dict = new MemoryStream();

                foreach (var (op, start, end) in entries)
                {
                    if (op == OpPrivate)
                    {
                        WriteFixedInt(dict, privates[i].DictLength);
                        WriteFixedInt(dict, privateAt is null ? 0 : privateAt[i]);
                    }
                    else
                    {
                        dict.Write(fontDicts[i].AsSpan(start, end - start));
                    }

                    WriteOperator(dict, op);
                }

                result.Add(dict.ToArray());
            }

            return result;
        }

        private static byte[] BuildTopDict(
            byte[] topDict,
            List<DictEntry> entries,
            int? charsetAt,
            int? fdSelectAt,
            int charStringsAt,
            int? fdArrayAt,
            (int Size, int Offset)? privateAt)
        {
            using var dict = new MemoryStream();

            foreach (var (op, start, end) in entries)
            {
                switch (op)
                {
                    case OpCharset when charsetAt is not null:
                        WriteFixedInt(dict, charsetAt.Value);
                        break;

                    case OpCharStrings:
                        WriteFixedInt(dict, charStringsAt);
                        break;

                    case OpFdArray when fdArrayAt is not null:
                        WriteFixedInt(dict, fdArrayAt.Value);
                        break;

                    case OpFdSelect when fdSelectAt is not null:
                        WriteFixedInt(dict, fdSelectAt.Value);
                        break;

                    case OpPrivate when privateAt is not null:
                        WriteFixedInt(dict, privateAt.Value.Size);
                        WriteFixedInt(dict, privateAt.Value.Offset);
                        break;

                    default:
                        dict.Write(topDict.AsSpan(start, end - start));
                        break;
                }

                WriteOperator(dict, op);
            }

            return dict.ToArray();
        }



        /// <summary>
        /// Writes an integer as the five byte form whatever its value, so a
        /// dictionary's size does not depend on offsets not yet known.
        /// </summary>
        private static void WriteFixedInt(Stream stream, int value)
        {
            Span<byte> buffer = stackalloc byte[5];
            buffer[0] = 29;
            BinaryPrimitives.WriteInt32BigEndian(buffer[1..], value);
            stream.Write(buffer);
        }

        private static void WriteOperator(Stream stream, int op)
        {
            if (op >= 1200)
            {
                stream.WriteByte(12);
                stream.WriteByte((byte)(op - 1200));
                return;
            }

            stream.WriteByte((byte)op);
        }

        private static byte[] WriteIndex(List<byte[]> items)
        {
            using var stream = new MemoryStream();

            if (items.Count == 0)
            {
                stream.WriteByte(0);
                stream.WriteByte(0);
                return stream.ToArray();
            }

            var total = 1;

            foreach (var item in items)
                total += item.Length;

            // Offsets are one based and the last points past the data, so the
            // largest value written is the total length.
            var offSize = total switch
            {
                <= 0xFF => 1,
                <= 0xFFFF => 2,
                <= 0xFFFFFF => 3,
                _ => 4,
            };

            Span<byte> buffer = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)items.Count);
            stream.Write(buffer);
            stream.WriteByte((byte)offSize);

            var offset = 1;
            WriteOffset(stream, offset, offSize);

            foreach (var item in items)
            {
                offset += item.Length;
                WriteOffset(stream, offset, offSize);
            }

            foreach (var item in items)
                stream.Write(item);

            return stream.ToArray();
        }

        private static void WriteOffset(Stream stream, int value, int offSize)
        {
            for (var shift = (offSize - 1) * 8; shift >= 0; shift -= 8)
                stream.WriteByte((byte)(value >> shift));
        }

        private static byte[] ReadIndexBytes(byte[] data, ref int pos)
        {
            var start = pos;
            CffIndex.Read(data, ref pos);
            return data.AsSpan(start, pos - start).ToArray();
        }

        private static List<byte[]> ReadIndexItems(byte[] data, ref int pos)
        {
            var index = CffIndex.Read(data, ref pos);
            var items = new List<byte[]>(index.Count);

            for (var i = 0; i < index.Count; i++)
                items.Add(index[i].ToArray());

            return items;
        }

        private static bool TryGetOffset(
            List<DictEntry> entries, byte[] dict, int op, out int offset)
        {
            offset = 0;

            if (!TryGetEntry(entries, op, out var entry))
                return false;

            var operands = ReadOperands(dict, entry.Start, entry.End);

            if (operands.Count == 0)
                return false;

            offset = (int)operands[^1];
            return true;
        }

        private static int GetOffset(List<DictEntry> entries, byte[] dict, int op) =>
            TryGetOffset(entries, dict, op, out var offset)
                ? offset
                : throw new InvalidOperationException($"The dictionary has no operator {op}.");

        /// <summary>
        /// Every operator in a dictionary, with the bytes of its operands.
        /// </summary>
        /// <remarks>
        /// The operands are kept as bytes rather than values so an entry this
        /// code has no interest in can be copied across exactly as it was,
        /// reals included.
        /// </remarks>
        /// <summary>One operator of a dictionary, and the bytes of its operands.</summary>
        private readonly record struct DictEntry(int Op, int Start, int End);

        private static bool TryGetEntry(List<DictEntry> entries, int op, out DictEntry found)
        {
            foreach (var entry in entries)
            {
                if (entry.Op == op)
                {
                    found = entry;
                    return true;
                }
            }

            found = default;
            return false;
        }

        private static bool HasEntry(List<DictEntry> entries, int op) => TryGetEntry(entries, op, out _);

        /// <remarks>
        /// In order, because a CID keyed font must state ROS first and a
        /// reader is entitled to insist on it.
        /// </remarks>
        private static List<DictEntry> ReadDictEntries(byte[] dict)
        {
            var entries = new List<DictEntry>();
            var operandStart = 0;
            var pos = 0;

            while (pos < dict.Length)
            {
                int b0 = dict[pos];

                if (b0 <= 21)
                {
                    var operandEnd = pos;
                    var op = b0;
                    pos++;

                    if (b0 == 12)
                    {
                        op = 1200 + dict[pos];
                        pos++;
                    }

                    entries.Add(new DictEntry(op, operandStart, operandEnd));
                    operandStart = pos;
                    continue;
                }

                pos += b0 switch
                {
                    28 => 3,
                    29 => 5,
                    30 => RealLength(dict, pos),
                    >= 32 and <= 246 => 1,
                    >= 247 and <= 254 => 2,
                    _ => throw new InvalidOperationException($"Unexpected dictionary byte {b0}."),
                };
            }

            return entries;
        }

        private static List<double> ReadOperands(byte[] dict, int start, int end)
        {
            var operands = new List<double>();
            var pos = start;

            while (pos < end)
            {
                int b0 = dict[pos];

                switch (b0)
                {
                    case 28:
                        operands.Add(BinaryPrimitives.ReadInt16BigEndian(dict.AsSpan(pos + 1)));
                        pos += 3;
                        break;

                    case 29:
                        operands.Add(BinaryPrimitives.ReadInt32BigEndian(dict.AsSpan(pos + 1)));
                        pos += 5;
                        break;

                    case 30:
                        // A real: never an offset, so its value is not needed.
                        operands.Add(0);
                        pos += RealLength(dict, pos);
                        break;

                    case >= 32 and <= 246:
                        operands.Add(b0 - 139);
                        pos++;
                        break;

                    case >= 247 and <= 250:
                        operands.Add(((b0 - 247) * 256) + dict[pos + 1] + 108);
                        pos += 2;
                        break;

                    case >= 251 and <= 254:
                        operands.Add((-(b0 - 251) * 256) - dict[pos + 1] - 108);
                        pos += 2;
                        break;

                    default:
                        throw new InvalidOperationException($"Unexpected operand byte {b0}.");
                }
            }

            return operands;
        }

        private static int RealLength(byte[] dict, int pos)
        {
            var length = 1;

            while (true)
            {
                var b = dict[pos + length];
                length++;

                if ((b & 0x0F) == 0x0F || (b >> 4) == 0x0F)
                    break;
            }

            return length;
        }
    }
}
