using System.Linq;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace PeachDrawing.PDF.Import;

/// <summary>
/// Completes the font program embedded in a PDF so the font loader accepts it. A subset in a PDF routinely drops the
/// <c>name</c>, <c>OS/2</c> and <c>post</c> tables (a PDF names its fonts in its own dictionaries and needs none of them to
/// draw), but the loader reads family, style, vertical metrics and underline data from them. Missing ones are written
/// with neutral values derived from <c>head</c> and <c>hhea</c>; the glyph data is not touched.
/// </summary>
internal static class SfntRepair
{
    readonly record struct Table(string Tag, byte[] Data);

    /// <summary>Returns <paramref name="font"/> with any of <c>name</c>, <c>OS/2</c> and <c>post</c> it lacked added.</summary>
    public static byte[] AddMissingTables(byte[] font, string familyName)
    {
        if (font.Length < 12 || (font[0] == 't' && font[1] == 't' && font[2] == 'c')) return font;
        int count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        if (12 + count * 16 > font.Length) return font;
        var tables = new List<Table>(count + 3);
        var have = new HashSet<string>();
        for (int i = 0; i < count; i++)
        {
            int rec = 12 + i * 16;
            string tag = Encoding.ASCII.GetString(font, rec, 4);
            uint offset = BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(rec + 8));
            uint length = BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(rec + 12));
            if (offset > font.Length || length > font.Length - offset) return font; // damaged directory: leave it to the loader to refuse
            tables.Add(new Table(tag, font.AsSpan((int)offset, (int)length).ToArray()));
            have.Add(tag);
        }
        if (have.Contains("name") && have.Contains("OS/2") && have.Contains("post") && have.Contains("cmap")) return font;

        int unitsPerEm = 1000;
        short ascender = 800, descender = -200, lineGap = 0;
        foreach (var t in tables)
        {
            if (t.Tag == "head" && t.Data.Length >= 20) unitsPerEm = Math.Max(1, (int)BinaryPrimitives.ReadUInt16BigEndian(t.Data.AsSpan(18)));
            if (t.Tag == "hhea" && t.Data.Length >= 10)
            {
                ascender = BinaryPrimitives.ReadInt16BigEndian(t.Data.AsSpan(4));
                descender = BinaryPrimitives.ReadInt16BigEndian(t.Data.AsSpan(6));
                lineGap = BinaryPrimitives.ReadInt16BigEndian(t.Data.AsSpan(8));
            }
        }
        if (!have.Contains("cmap")) tables.Add(new Table("cmap", BuildEmptyCmap()));
        if (!have.Contains("name")) tables.Add(new Table("name", BuildName(familyName)));
        if (!have.Contains("OS/2")) tables.Add(new Table("OS/2", BuildOs2(unitsPerEm, ascender, descender, lineGap)));
        if (!have.Contains("post")) tables.Add(new Table("post", BuildPost(unitsPerEm)));
        tables.Sort((a, b) => string.CompareOrdinal(a.Tag, b.Tag));
        return Write(font.AsSpan(0, 4), tables);
    }

    /// <summary>A format 4 cmap that maps nothing: a subset used through glyph indexes needs the table to exist, not to map.</summary>
    static byte[] BuildEmptyCmap()
    {
        var d = new byte[12 + 24];
        BinaryPrimitives.WriteUInt16BigEndian(d.AsSpan(2), 1);        // numTables
        BinaryPrimitives.WriteUInt16BigEndian(d.AsSpan(4), 3);        // platform: Windows
        BinaryPrimitives.WriteUInt16BigEndian(d.AsSpan(6), 1);        // encoding: Unicode BMP
        BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(8), 12);       // subtable offset
        var t = d.AsSpan(12);
        BinaryPrimitives.WriteUInt16BigEndian(t, 4);                  // format
        BinaryPrimitives.WriteUInt16BigEndian(t[2..], 24);            // length
        BinaryPrimitives.WriteUInt16BigEndian(t[6..], 2);             // segCountX2
        BinaryPrimitives.WriteUInt16BigEndian(t[14..], 0xFFFF);       // endCode
        BinaryPrimitives.WriteUInt16BigEndian(t[18..], 0xFFFF);       // startCode
        BinaryPrimitives.WriteUInt16BigEndian(t[20..], 1);            // idDelta
        return d;
    }

    static byte[] BuildName(string family)
    {
        var records = new (int Id, string Text)[] { (1, family), (2, "Regular"), (4, family) };
        var strings = new List<byte[]>();
        foreach (var r in records) strings.Add(Encoding.BigEndianUnicode.GetBytes(r.Text));
        int storage = 6 + records.Length * 12;
        var data = new byte[storage + strings.Sum(s => s.Length)];
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2), (ushort)records.Length);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4), (ushort)storage);
        int offset = 0;
        for (int i = 0; i < records.Length; i++)
        {
            int rec = 6 + i * 12;
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(rec), 3);       // platform: Windows
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(rec + 2), 1);   // encoding: Unicode BMP
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(rec + 4), 0x409);
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(rec + 6), (ushort)records[i].Id);
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(rec + 8), (ushort)strings[i].Length);
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(rec + 10), (ushort)offset);
            strings[i].CopyTo(data, storage + offset);
            offset += strings[i].Length;
        }
        return data;
    }

    static byte[] BuildOs2(int upm, short ascender, short descender, short lineGap)
    {
        var d = new byte[78];
        void U16(int at, int v) => BinaryPrimitives.WriteUInt16BigEndian(d.AsSpan(at), (ushort)v);
        void S16(int at, int v) => BinaryPrimitives.WriteInt16BigEndian(d.AsSpan(at), (short)v);
        U16(0, 0);              // version 0
        U16(4, 400);            // usWeightClass
        U16(6, 5);              // usWidthClass: normal
        S16(26, upm / 20);      // yStrikeoutSize
        S16(28, upm * 3 / 10);  // yStrikeoutPosition
        U16(62, 0x40);          // fsSelection: REGULAR
        U16(64, 0x20);          // usFirstCharIndex
        U16(66, 0xFFFF);        // usLastCharIndex
        S16(68, ascender);      // sTypoAscender
        S16(70, descender);     // sTypoDescender
        S16(72, lineGap);       // sTypoLineGap
        U16(74, Math.Max(0, (int)ascender));          // usWinAscent
        U16(76, Math.Abs((int)descender));            // usWinDescent
        return d;
    }

    static byte[] BuildPost(int upm)
    {
        var d = new byte[32];
        BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(0), 0x00030000);   // version 3.0: no glyph names
        BinaryPrimitives.WriteInt16BigEndian(d.AsSpan(8), (short)(-upm / 10));   // underlinePosition
        BinaryPrimitives.WriteInt16BigEndian(d.AsSpan(10), (short)Math.Max(1, upm / 20)); // underlineThickness
        return d;
    }

    static byte[] Write(ReadOnlySpan<byte> version, List<Table> tables)
    {
        int n = tables.Count;
        int entrySelector = 0;
        while ((1 << (entrySelector + 1)) <= n) entrySelector++;
        int searchRange = (1 << entrySelector) * 16;
        int dataStart = 12 + n * 16;
        int total = dataStart;
        foreach (var t in tables) total += (t.Data.Length + 3) & ~3;
        var o = new byte[total];
        version.CopyTo(o);
        BinaryPrimitives.WriteUInt16BigEndian(o.AsSpan(4), (ushort)n);
        BinaryPrimitives.WriteUInt16BigEndian(o.AsSpan(6), (ushort)searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(o.AsSpan(8), (ushort)entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(o.AsSpan(10), (ushort)(n * 16 - searchRange));
        int pos = dataStart;
        for (int i = 0; i < n; i++)
        {
            var t = tables[i];
            int rec = 12 + i * 16;
            Encoding.ASCII.GetBytes(t.Tag, o.AsSpan(rec, 4));
            t.Data.CopyTo(o, pos);
            uint sum = 0;
            for (int k = 0; k < ((t.Data.Length + 3) & ~3); k += 4)
            {
                uint word = 0;
                for (int b = 0; b < 4; b++) word = (word << 8) | (k + b < t.Data.Length ? t.Data[k + b] : (uint)0);
                sum = unchecked(sum + word);
            }
            BinaryPrimitives.WriteUInt32BigEndian(o.AsSpan(rec + 4), sum);
            BinaryPrimitives.WriteUInt32BigEndian(o.AsSpan(rec + 8), (uint)pos);
            BinaryPrimitives.WriteUInt32BigEndian(o.AsSpan(rec + 12), (uint)t.Data.Length);
            pos += (t.Data.Length + 3) & ~3;
        }
        return o;
    }
}
