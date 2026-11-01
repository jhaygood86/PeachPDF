using System;
using System.Collections.Generic;
using System.IO;

namespace PeachDrawing.PDF.Import;

/// <summary>
/// The parts of a Compact Font Format program (Adobe TN 5176) the importer needs to use it as a font: how many glyphs it has, what each is
/// called (or, in a CID-keyed font, which CID it is), its font matrix and box, and a custom built-in encoding. The glyph outlines are read by
/// the font engine; this only describes the font.
/// </summary>
internal sealed class CffFont
{
    /// <summary>The number of glyphs.</summary>
    public int GlyphCount { get; private init; }

    /// <summary>Whether the font is CID-keyed (it has a ROS operator): glyphs are then identified by CID, not by name.</summary>
    public bool IsCid { get; private init; }

    /// <summary>For a name-keyed font, the name of each glyph by index (null when a glyph's SID is not in the standard set or the string index).</summary>
    public string?[] GlyphNames { get; private init; } = [];

    /// <summary>For a CID-keyed font, the CID of each glyph by index.</summary>
    public int[] CidOfGlyph { get; private init; } = [];

    /// <summary>The font matrix; <c>[0.001 0 0 0.001 0 0]</c> when the font gives none.</summary>
    public double[] FontMatrix { get; private init; } = [0.001, 0, 0, 0.001, 0, 0];

    /// <summary>The font bounding box (xmin ymin xmax ymax), or all zero.</summary>
    public double[] FontBBox { get; private init; } = [0, 0, 0, 0];

    /// <summary>The font's own code-to-glyph encoding when it is a custom one; empty for the predefined Standard and Expert encodings.</summary>
    public Dictionary<int, int> BuiltInEncoding { get; private init; } = new();

    /// <summary>Reads <paramref name="cff"/>; null when it is not a CFF program this reader understands.</summary>
    public static CffFont? Parse(byte[] cff)
    {
        try { return ParseCore(cff); }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException or InvalidDataException or OverflowException) { return null; }
    }

    static CffFont? ParseCore(byte[] d)
    {
        if (d.Length < 4 || d[0] != 1) return null;
        int pos = d[2];                                       // hdrSize
        var names = ReadIndex(d, ref pos);                    // Name INDEX
        var topDicts = ReadIndex(d, ref pos);
        var strings = ReadIndex(d, ref pos);
        ReadIndex(d, ref pos);                                // Global Subr INDEX
        if (names is null || topDicts is null || strings is null || topDicts.Count == 0) return null;

        var top = ParseDict(d, topDicts[0].Offset, topDicts[0].Length);
        int charStringsOffset = (int)Number(top, 17, 0);
        if (charStringsOffset <= 0) return null;
        int p = charStringsOffset;
        var charStrings = ReadIndex(d, ref p);
        if (charStrings is null) return null;
        int count = charStrings.Count;

        bool isCid = top.ContainsKey(1230);                   // ROS = 12 30
        int charsetOffset = (int)Number(top, 15, 0);
        var sids = ReadCharset(d, charsetOffset, count);

        var matrix = top.TryGetValue(1207, out var fm) && fm.Count == 6 ? fm.ToArray() : [0.001, 0, 0, 0.001, 0, 0];
        var bbox = top.TryGetValue(5, out var fb) && fb.Count == 4 ? fb.ToArray() : [0, 0, 0, 0];

        string?[] glyphNames = [];
        int[] cids = [];
        if (isCid) cids = sids;
        else
        {
            glyphNames = new string?[count];
            for (int g = 0; g < count; g++) glyphNames[g] = SidToName(sids[g], strings, d);
        }

        var encoding = new Dictionary<int, int>();
        int encodingOffset = (int)Number(top, 16, 0);
        if (!isCid && encodingOffset > 1) ReadEncoding(d, encodingOffset, count, encoding);

        return new CffFont { GlyphCount = count, IsCid = isCid, GlyphNames = glyphNames, CidOfGlyph = cids, FontMatrix = matrix, FontBBox = bbox, BuiltInEncoding = encoding };
    }

    /// <summary>Maps a CID to the glyph index that has it (a CID-keyed font only); the CIDs of a name-keyed font are its glyph indexes.</summary>
    public Dictionary<int, int> GlyphOfCid()
    {
        var map = new Dictionary<int, int>();
        for (int g = 0; g < CidOfGlyph.Length; g++) map.TryAdd(CidOfGlyph[g], g);
        return map;
    }

    // ------------------------------------------------------------ INDEX and DICT

    readonly record struct Entry(int Offset, int Length);

    static List<Entry>? ReadIndex(byte[] d, ref int pos)
    {
        int count = (d[pos] << 8) | d[pos + 1];
        pos += 2;
        var entries = new List<Entry>(count);
        if (count == 0) return entries;
        int offSize = d[pos++];
        if (offSize is < 1 or > 4) return null;
        int offsets = pos;
        int dataStart = offsets + (count + 1) * offSize - 1;
        int Offset(int i)
        {
            int v = 0;
            for (int k = 0; k < offSize; k++) v = (v << 8) | d[offsets + i * offSize + k];
            return v;
        }
        int prev = Offset(0);
        for (int i = 1; i <= count; i++)
        {
            int next = Offset(i);
            if (next < prev || dataStart + next > d.Length) return null;
            entries.Add(new Entry(dataStart + prev, next - prev));
            prev = next;
        }
        pos = dataStart + prev;
        return entries;
    }

    /// <summary>Parses a DICT into operator -> operands; two-byte operators (12 x) are keyed as 1200 + x.</summary>
    static Dictionary<int, List<double>> ParseDict(byte[] d, int offset, int length)
    {
        var dict = new Dictionary<int, List<double>>();
        var operands = new List<double>();
        int end = offset + length;
        for (int i = offset; i < end;)
        {
            int b0 = d[i++];
            if (b0 <= 21)
            {
                int op = b0;
                if (b0 == 12) op = 1200 + d[i++];
                dict[op] = operands;
                operands = [];
            }
            else if (b0 == 28) { operands.Add((short)((d[i] << 8) | d[i + 1])); i += 2; }
            else if (b0 == 29) { operands.Add((d[i] << 24) | (d[i + 1] << 16) | (d[i + 2] << 8) | d[i + 3]); i += 4; }
            else if (b0 == 30)
            {
                var sb = new System.Text.StringBuilder();
                bool done = false;
                while (!done && i < end)
                {
                    int b = d[i++];
                    foreach (int nibble in new[] { b >> 4, b & 15 })
                    {
                        if (nibble == 15) { done = true; break; }
                        if (nibble <= 9) sb.Append((char)('0' + nibble));
                        else if (nibble == 10) sb.Append('.');
                        else if (nibble == 11) sb.Append('E');
                        else if (nibble == 12) sb.Append("E-");
                        else if (nibble == 14) sb.Append('-');
                    }
                }
                operands.Add(double.TryParse(sb.ToString().Replace("\0", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var real) ? real : 0);
            }
            else if (b0 is >= 32 and <= 246) operands.Add(b0 - 139);
            else if (b0 is >= 247 and <= 250) operands.Add((b0 - 247) * 256 + d[i++] + 108);
            else if (b0 is >= 251 and <= 254) operands.Add(-(b0 - 251) * 256 - d[i++] - 108);
        }
        return dict;
    }

    static double Number(Dictionary<int, List<double>> dict, int op, double fallback) =>
        dict.TryGetValue(op, out var v) && v.Count > 0 ? v[^1] : fallback;

    // ------------------------------------------------------------ charset, encoding, strings

    static int[] ReadCharset(byte[] d, int offset, int count)
    {
        var sids = new int[count];
        if (offset <= 2)
        {
            // Predefined charsets (ISOAdobe, Expert, ExpertSubset): ISOAdobe is SID = glyph index; the expert sets name no glyph here.
            for (int g = 0; g < count; g++) sids[g] = offset == 0 && g <= 228 ? g : 0;
            return sids;
        }
        int p = offset;
        int format = d[p++];
        int glyph = 1;
        if (format == 0)
        {
            for (; glyph < count; glyph++) { sids[glyph] = (d[p] << 8) | d[p + 1]; p += 2; }
        }
        else
        {
            while (glyph < count)
            {
                int first = (d[p] << 8) | d[p + 1];
                p += 2;
                int left;
                if (format == 1) left = d[p++];
                else { left = (d[p] << 8) | d[p + 1]; p += 2; }
                for (int k = 0; k <= left && glyph < count; k++) sids[glyph++] = first + k;
            }
        }
        return sids;
    }

    static string? SidToName(int sid, List<Entry> strings, byte[] d)
    {
        if (sid < PdfGlyphNames.StandardStrings.Length) return PdfGlyphNames.StandardStrings[sid];
        int index = sid - PdfGlyphNames.StandardStringCount;
        if (index < 0 || index >= strings.Count) return null;
        return System.Text.Encoding.Latin1.GetString(d, strings[index].Offset, strings[index].Length);
    }

    static void ReadEncoding(byte[] d, int offset, int count, Dictionary<int, int> encoding)
    {
        int p = offset;
        int format = d[p++];
        if ((format & 0x7F) == 0)
        {
            int codes = d[p++];
            for (int i = 0; i < codes && i + 1 < count; i++) encoding[d[p++]] = i + 1;
        }
        else
        {
            int ranges = d[p++];
            int glyph = 1;
            for (int i = 0; i < ranges; i++)
            {
                int first = d[p++], left = d[p++];
                for (int k = 0; k <= left && glyph < count; k++) encoding[first + k] = glyph++;
            }
        }
        // Supplemental encodings (a code assigned to a named glyph) are not read.
    }
}
