using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace PeachDrawing.Svg;

/// <summary>
/// Makes an exported font subset loadable by a browser. A PDF only needs the glyph tables of an embedded font, so the subset leaves out
/// <c>name</c>, <c>OS/2</c> and <c>post</c>; a browser's font sanitiser rejects a font without them. This adds minimal versions of
/// whichever are missing and rewrites the table directory and checksums, leaving every other table byte for byte as it was.
/// </summary>
internal static class SvgFontPackager
{
    public static byte[] ForBrowser(ReadOnlySpan<byte> font, string familyName)
    {
        if (font.Length < 12)
            return font.ToArray();

        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        var count = BinaryPrimitives.ReadUInt16BigEndian(font[4..]);
        for (var i = 0; i < count; i++)
        {
            var entry = font.Slice(12 + 16 * i, 16);
            var tag = Encoding.ASCII.GetString(entry[..4]);
            var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(entry[8..]);
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(entry[12..]);
            tables[tag] = font.Slice(offset, length).ToArray();
        }

        var head = tables["head"];
        var hhea = tables["hhea"];
        var unitsPerEm = BinaryPrimitives.ReadUInt16BigEndian(head.AsSpan(18));
        var ascender = BinaryPrimitives.ReadInt16BigEndian(hhea.AsSpan(4));
        var descender = BinaryPrimitives.ReadInt16BigEndian(hhea.AsSpan(6));
        var lineGap = BinaryPrimitives.ReadInt16BigEndian(hhea.AsSpan(8));

        if (!tables.ContainsKey("name"))
            tables["name"] = BuildName(familyName);
        if (!tables.ContainsKey("OS/2"))
            tables["OS/2"] = BuildOs2(unitsPerEm, ascender, descender, lineGap);
        if (!tables.ContainsKey("post"))
            tables["post"] = BuildPost(unitsPerEm);

        return Assemble(BinaryPrimitives.ReadUInt32BigEndian(font), tables);
    }

    private static byte[] BuildName(string familyName)
    {
        // Format 0, four Windows/Unicode BMP/English records: family, subfamily, full name, PostScript name.
        var strings = new[] { (1, familyName), (2, "Regular"), (4, familyName), (6, familyName) };
        var storage = new List<byte>();
        var header = new byte[6 + 12 * strings.Length];
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(2), (ushort)strings.Length);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), (ushort)header.Length);
        for (var i = 0; i < strings.Length; i++)
        {
            var bytes = Encoding.BigEndianUnicode.GetBytes(strings[i].Item2);
            var record = header.AsSpan(6 + 12 * i);
            BinaryPrimitives.WriteUInt16BigEndian(record, 3);
            BinaryPrimitives.WriteUInt16BigEndian(record[2..], 1);
            BinaryPrimitives.WriteUInt16BigEndian(record[4..], 0x409);
            BinaryPrimitives.WriteUInt16BigEndian(record[6..], (ushort)strings[i].Item1);
            BinaryPrimitives.WriteUInt16BigEndian(record[8..], (ushort)bytes.Length);
            BinaryPrimitives.WriteUInt16BigEndian(record[10..], (ushort)storage.Count);
            storage.AddRange(bytes);
        }

        var result = new byte[header.Length + storage.Count];
        header.CopyTo(result, 0);
        storage.CopyTo(result, header.Length);
        return result;
    }

    private static byte[] BuildOs2(int unitsPerEm, int ascender, int descender, int lineGap)
    {
        var t = new byte[96];
        void U16(int at, int value) => BinaryPrimitives.WriteUInt16BigEndian(t.AsSpan(at), (ushort)value);
        void I16(int at, int value) => BinaryPrimitives.WriteInt16BigEndian(t.AsSpan(at), (short)value);

        U16(0, 3);                              // version
        I16(2, unitsPerEm / 2);                 // xAvgCharWidth
        U16(4, 400);                            // usWeightClass
        U16(6, 5);                              // usWidthClass
        I16(10, unitsPerEm / 5 * 3);            // ySubscriptXSize
        I16(12, unitsPerEm / 5 * 3);            // ySubscriptYSize
        I16(16, unitsPerEm / 7);                // ySubscriptYOffset
        I16(18, unitsPerEm / 5 * 3);            // ySuperscriptXSize
        I16(20, unitsPerEm / 5 * 3);            // ySuperscriptYSize
        I16(24, unitsPerEm / 3);                // ySuperscriptYOffset
        I16(26, unitsPerEm / 20);               // yStrikeoutSize
        I16(28, unitsPerEm / 4);                // yStrikeoutPosition
        "NONE"u8.CopyTo(t.AsSpan(58));          // achVendID
        U16(62, 0x40);                          // fsSelection: REGULAR
        U16(64, 0x20);                          // usFirstCharIndex
        U16(66, 0xFFFF);                        // usLastCharIndex
        I16(68, ascender);                      // sTypoAscender
        I16(70, descender);                     // sTypoDescender
        I16(72, lineGap);                       // sTypoLineGap
        U16(74, Math.Max(ascender, 0));         // usWinAscent
        U16(76, Math.Abs(descender));           // usWinDescent
        BinaryPrimitives.WriteUInt32BigEndian(t.AsSpan(78), 1); // ulCodePageRange1: Latin 1
        I16(86, unitsPerEm / 2);                // sxHeight
        I16(88, unitsPerEm * 7 / 10);           // sCapHeight
        U16(92, 0x20);                          // usBreakChar
        U16(94, 1);                             // usMaxContext
        return t;
    }

    private static byte[] BuildPost(int unitsPerEm)
    {
        var t = new byte[32];
        BinaryPrimitives.WriteUInt32BigEndian(t, 0x00030000);                    // version 3.0: no glyph names
        BinaryPrimitives.WriteInt16BigEndian(t.AsSpan(8), (short)(-unitsPerEm / 10)); // underlinePosition
        BinaryPrimitives.WriteInt16BigEndian(t.AsSpan(10), (short)(unitsPerEm / 20)); // underlineThickness
        return t;
    }

    private static byte[] Assemble(uint sfntVersion, SortedDictionary<string, byte[]> tables)
    {
        var count = tables.Count;
        var directory = 12 + 16 * count;
        var total = directory;
        foreach (var table in tables.Values)
            total += (table.Length + 3) & ~3;

        var output = new byte[total];
        BinaryPrimitives.WriteUInt32BigEndian(output, sfntVersion);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(4), (ushort)count);
        var entrySelector = (int)Math.Floor(Math.Log2(count));
        var searchRange = (1 << entrySelector) * 16;
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(6), (ushort)searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(8), (ushort)entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(10), (ushort)(count * 16 - searchRange));

        var position = directory;
        var index = 0;
        var headOffset = -1;
        foreach (var (tag, table) in tables)
        {
            var entry = output.AsSpan(12 + 16 * index++);
            Encoding.ASCII.GetBytes(tag).CopyTo(entry);
            table.CopyTo(output, position);
            if (tag == "head")
            {
                headOffset = position;
                BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(position + 8), 0); // checkSumAdjustment is summed as zero
            }

            BinaryPrimitives.WriteUInt32BigEndian(entry[4..], Checksum(output.AsSpan(position, (table.Length + 3) & ~3)));
            BinaryPrimitives.WriteUInt32BigEndian(entry[8..], (uint)position);
            BinaryPrimitives.WriteUInt32BigEndian(entry[12..], (uint)table.Length);
            position += (table.Length + 3) & ~3;
        }

        if (headOffset >= 0)
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(headOffset + 8), 0xB1B0AFBAu - Checksum(output));

        return output;
    }

    private static uint Checksum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        for (var i = 0; i + 3 < data.Length; i += 4)
            sum += BinaryPrimitives.ReadUInt32BigEndian(data[i..]);

        return sum;
    }
}
