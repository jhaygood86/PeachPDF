using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeachDrawing.PDF.Import;

/// <summary>One character code read from a shown string: its value, how many bytes it took, and the CID the CMap gives it.</summary>
/// <param name="Code">the code, the bytes read as one big-endian number</param>
/// <param name="Length">the number of bytes the code took, 1 to 4</param>
/// <param name="Cid">the character identifier in the font's character collection</param>
internal readonly record struct PdfCharCode(uint Code, int Length, int Cid);

/// <summary>
/// A CMap (ISO 32000-1 9.7.5): how the bytes of a string shown with a composite font split into character codes, and which CID each code is. A
/// predefined CMap (<c>UniJIS-UCS2-H</c>, <c>90ms-RKSJ-V</c>...) comes from the bundled data, an embedded one from its stream, and Identity-H/V
/// is a two-byte code that is its own CID. A CMap may build on another (<c>usecmap</c>); the codes it does not define are looked up in the parent.
/// </summary>
internal sealed class PdfCMap
{
    /// <summary>A range of codes of one length that a CMap accepts; a code matches when every byte is inside the matching bytes of the two ends.</summary>
    internal readonly record struct CodeSpace(int Length, uint Low, uint High)
    {
        internal bool Matches(uint code)
        {
            for (int i = 0; i < Length; i++)
            {
                int shift = 8 * (Length - 1 - i);
                uint b = (code >> shift) & 0xFF;
                if (b < ((Low >> shift) & 0xFF) || b > ((High >> shift) & 0xFF)) return false;
            }

            return true;
        }
    }

    /// <summary>Codes <see cref="Low"/> to <see cref="High"/> have the CIDs starting at <see cref="Cid"/>.</summary>
    internal readonly record struct CidRange(uint Low, uint High, int Cid);

    private readonly List<CodeSpace> _spaces = [];
    private readonly List<CidRange>[] _ranges = [[], [], [], []];
    private CidRange[][]? _sorted;
    private CodeSpace[]? _allSpaces;

    /// <summary>Makes an empty CMap.</summary>
    /// <param name="name">its name, as <c>/Encoding</c> names it</param>
    internal PdfCMap(string name) => Name = name;

    /// <summary>The CMap's name.</summary>
    internal string Name { get; }

    /// <summary>Whether the text runs top to bottom (<c>WMode</c> 1, the <c>-V</c> CMaps).</summary>
    internal bool Vertical { get; set; }

    /// <summary>The CMap this one builds on, or null.</summary>
    internal PdfCMap? Parent { get; set; }

    /// <summary>Whether this is Identity-H/V: two bytes that are their own CID.</summary>
    internal bool IsIdentity { get; private init; }

    /// <summary>The Identity-H (or, vertical, Identity-V) CMap.</summary>
    internal static PdfCMap Identity(bool vertical) => new(vertical ? "Identity-V" : "Identity-H")
    {
        Vertical = vertical,
        IsIdentity = true,
        _spaces = { new CodeSpace(2, 0x0000, 0xFFFF) },
    };

    internal IReadOnlyList<CodeSpace> Spaces => _spaces;

    internal int RangeCount => _ranges[0].Count + _ranges[1].Count + _ranges[2].Count + _ranges[3].Count;

    internal IReadOnlyList<CidRange> RangesOfLength(int length) => _ranges[length - 1];

    internal void AddSpace(int length, uint low, uint high)
    {
        if (length is < 1 or > 4) return;
        _spaces.Add(new CodeSpace(length, low, high));
        Invalidate();
    }

    internal void AddRange(int length, uint low, uint high, int cid)
    {
        if (length is < 1 or > 4 || high < low) return;
        _ranges[length - 1].Add(new CidRange(low, high, cid));
        Invalidate();
    }

    private void Invalidate()
    {
        _sorted = null;
        _allSpaces = null;
    }

    private CidRange[][] Sorted()
    {
        if (_sorted is { } ready) return ready;
        var sorted = new CidRange[4][];
        for (int i = 0; i < 4; i++)
        {
            var copy = _ranges[i].ToArray();
            Array.Sort(copy, static (a, b) => a.Low.CompareTo(b.Low));
            sorted[i] = copy;
        }

        return _sorted = sorted;
    }

    private CodeSpace[] AllSpaces()
    {
        if (_allSpaces is { } ready) return ready;
        var all = new List<CodeSpace>(_spaces);
        for (var p = Parent; p is not null; p = p.Parent) all.AddRange(p._spaces);
        return _allSpaces = [.. all];
    }

    /// <summary>The CID of a code, or -1 when no range of this CMap or its parents holds it.</summary>
    /// <param name="code">the code</param>
    /// <param name="length">the number of bytes the code took</param>
    internal int Lookup(uint code, int length)
    {
        for (var map = this; map is not null; map = map.Parent)
        {
            if (map.IsIdentity) return (int)code;
            var ranges = map.Sorted()[length - 1];
            int lo = 0, hi = ranges.Length - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >>> 1;
                if (ranges[mid].Low <= code) { found = mid; lo = mid + 1; } else hi = mid - 1;
            }

            if (found >= 0 && code <= ranges[found].High) return ranges[found].Cid + (int)(code - ranges[found].Low);
        }

        return -1;
    }

    /// <summary>
    /// Splits a shown string (one char per byte) into character codes: at each place the shortest length for which a code space matches is taken
    /// (9.7.6.2), and bytes no space accepts are read as one code of the shortest length any space has, with CID 0.
    /// </summary>
    /// <param name="shown">the string, one char per byte</param>
    internal IEnumerable<PdfCharCode> Decode(string shown)
    {
        var spaces = AllSpaces();
        int shortest = 4;
        foreach (var s in spaces) shortest = Math.Min(shortest, s.Length);
        if (spaces.Length == 0) shortest = 1;

        int i = 0;
        while (i < shown.Length)
        {
            int taken = 0;
            uint code = 0;
            for (int length = 1; length <= 4 && i + length <= shown.Length; length++)
            {
                code = (code << 8) | (uint)(shown[i + length - 1] & 0xFF);
                foreach (var space in spaces)
                    if (space.Length == length && space.Matches(code)) { taken = length; break; }
                if (taken != 0) break;
            }

            if (taken == 0)
            {
                taken = Math.Min(shortest, shown.Length - i);
                code = 0;
                for (int k = 0; k < taken; k++) code = (code << 8) | (uint)(shown[i + k] & 0xFF);
                yield return new PdfCharCode(code, taken, 0);
            }
            else
            {
                int cid = Lookup(code, taken);
                yield return new PdfCharCode(code, taken, cid < 0 ? 0 : cid);
            }

            i += taken;
        }
    }

    // ------------------------------------------------------------ the text form

    /// <summary>
    /// Reads a CMap as PostScript text, as an embedded CMap stream and the Adobe CMap files are written: <c>begincodespacerange</c>, <c>begincidrange</c>,
    /// <c>begincidchar</c>, <c>usecmap</c> and <c>/WMode</c>. Anything else is skipped.
    /// </summary>
    /// <param name="data">the CMap text</param>
    /// <param name="name">the name to give it</param>
    /// <param name="resolveParent">finds the CMap a <c>usecmap</c> names</param>
    internal static PdfCMap Parse(ReadOnlySpan<byte> data, string name, Func<string, PdfCMap?> resolveParent)
    {
        var map = new PdfCMap(name);
        var tokens = Tokenize(data);
        int i = 0;
        while (i < tokens.Count)
        {
            var t = tokens[i++];
            if (t.Kind != TokenKind.Word) continue;
            switch (t.Text)
            {
                case "usecmap" when i >= 2 && tokens[i - 2].Kind == TokenKind.Name:
                    map.Parent = resolveParent(tokens[i - 2].Text);
                    break;
                case "def" when i >= 3 && tokens[i - 3] is { Kind: TokenKind.Name, Text: "WMode" } && tokens[i - 2].Kind == TokenKind.Number:
                    map.Vertical = tokens[i - 2].Number == 1;
                    break;
                case "begincodespacerange":
                    while (i + 1 < tokens.Count && tokens[i].Kind == TokenKind.Hex && tokens[i + 1].Kind == TokenKind.Hex)
                    {
                        if (tokens[i].Bytes.Length == tokens[i + 1].Bytes.Length) map.AddSpace(tokens[i].Bytes.Length, Value(tokens[i].Bytes), Value(tokens[i + 1].Bytes));
                        i += 2;
                    }

                    break;
                case "begincidrange":
                    while (i + 2 < tokens.Count && tokens[i].Kind == TokenKind.Hex && tokens[i + 1].Kind == TokenKind.Hex && tokens[i + 2].Kind == TokenKind.Number)
                    {
                        if (tokens[i].Bytes.Length == tokens[i + 1].Bytes.Length)
                            map.AddRange(tokens[i].Bytes.Length, Value(tokens[i].Bytes), Value(tokens[i + 1].Bytes), (int)tokens[i + 2].Number);
                        i += 3;
                    }

                    break;
                case "begincidchar":
                    while (i + 1 < tokens.Count && tokens[i].Kind == TokenKind.Hex && tokens[i + 1].Kind == TokenKind.Number)
                    {
                        uint code = Value(tokens[i].Bytes);
                        map.AddRange(tokens[i].Bytes.Length, code, code, (int)tokens[i + 1].Number);
                        i += 2;
                    }

                    break;
            }
        }

        return map;
    }

    private static uint Value(byte[] bytes)
    {
        uint v = 0;
        foreach (var b in bytes) v = (v << 8) | b;
        return v;
    }

    private enum TokenKind { Word, Name, Number, Hex }

    private readonly record struct Token(TokenKind Kind, string Text = "", double Number = 0, byte[]? HexBytes = null)
    {
        internal byte[] Bytes => HexBytes ?? [];
    }

    private static List<Token> Tokenize(ReadOnlySpan<byte> d)
    {
        var tokens = new List<Token>();
        int i = 0;
        static bool Space(byte b) => b is 0 or 9 or 10 or 12 or 13 or 32;
        static bool Delimiter(byte b) => b is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';
        while (i < d.Length)
        {
            byte b = d[i];
            if (Space(b)) { i++; continue; }
            if (b == '%')
            {
                while (i < d.Length && d[i] is not (10 or 13)) i++;
                continue;
            }

            if (b == '<' && i + 1 < d.Length && d[i + 1] == '<') { i += 2; continue; }
            if (b == '>' && i + 1 < d.Length && d[i + 1] == '>') { i += 2; continue; }
            if (b == '<')
            {
                i++;
                var hex = new List<byte>();
                int pending = -1;
                while (i < d.Length && d[i] != '>')
                {
                    int v = HexDigit(d[i]);
                    if (v >= 0)
                    {
                        if (pending < 0) pending = v;
                        else { hex.Add((byte)((pending << 4) | v)); pending = -1; }
                    }

                    i++;
                }

                if (pending >= 0) hex.Add((byte)(pending << 4));
                i++;
                tokens.Add(new Token(TokenKind.Hex, HexBytes: [.. hex]));
                continue;
            }

            if (b == '(')
            {
                int depth = 1;
                i++;
                while (i < d.Length && depth > 0)
                {
                    if (d[i] == '\\') i++;
                    else if (d[i] == '(') depth++;
                    else if (d[i] == ')') depth--;
                    i++;
                }

                continue;
            }

            if (b is (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)')' or (byte)'>') { i++; continue; }
            int start = i;
            i++;
            while (i < d.Length && !Space(d[i]) && !Delimiter(d[i])) i++;
            string text = System.Text.Encoding.ASCII.GetString(d[start..i]);
            if (b == '/') tokens.Add(new Token(TokenKind.Name, text[1..]));
            else if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)) tokens.Add(new Token(TokenKind.Number, text, number));
            else tokens.Add(new Token(TokenKind.Word, text));
        }

        return tokens;
    }

    private static int HexDigit(byte b) => b switch
    {
        >= (byte)'0' and <= (byte)'9' => b - '0',
        >= (byte)'a' and <= (byte)'f' => b - 'a' + 10,
        >= (byte)'A' and <= (byte)'F' => b - 'A' + 10,
        _ => -1,
    };
}
