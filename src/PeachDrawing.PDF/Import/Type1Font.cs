using System.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PeachDrawing.PDF.Import;

/// <summary>
/// A Type 1 font program (Adobe Type 1 Font Format): the clear-text header, the eexec-encrypted private part and the charstring-encrypted
/// subroutines and glyph programs. <see cref="ToCff"/> turns it into a CFF program (Type 1 charstrings converted to Type 2) so the font
/// engine, which reads CFF outlines, can draw it.
/// </summary>
/// <remarks>
/// The "encryption" of a Type 1 font (eexec, charstring) is the format's fixed-key obfuscation, a few lines of arithmetic that every
/// reader has to undo to read the glyphs; it protects nothing and is unrelated to PDF encryption.
/// </remarks>
internal sealed class Type1Font
{
    const ushort EexecKey = 55665, CharStringKey = 4330, C1 = 52845, C2 = 22719;

    readonly Dictionary<string, byte[]> _charStrings = new(StringComparer.Ordinal);
    readonly List<string> _glyphOrder = [];
    readonly List<byte[]?> _subrs = [];

    Type1Font() { }

    /// <summary>The font name (<c>/FontName</c>), or "Type1".</summary>
    public string Name { get; private set; } = "Type1";

    /// <summary>The font matrix; <c>[0.001 0 0 0.001 0 0]</c> when the font gives none.</summary>
    public double[] FontMatrix { get; private set; } = [0.001, 0, 0, 0.001, 0, 0];

    /// <summary>The font bounding box.</summary>
    public double[] FontBBox { get; private set; } = [0, 0, 0, 0];

    /// <summary>The font's own encoding (code to glyph name), or null when it is the Adobe StandardEncoding.</summary>
    public Dictionary<int, string>? Encoding { get; private set; }

    /// <summary>The names of the glyphs the font has, in file order.</summary>
    public IReadOnlyList<string> GlyphNames => _glyphOrder;

    /// <summary>Reads a Type 1 program (a PDF's <c>/FontFile</c> stream, or a PFB file); null when it is not one this reader understands.</summary>
    public static Type1Font? Parse(byte[] data)
    {
        try { return ParseCore(data); }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException or FormatException or OverflowException or InvalidDataException) { return null; }
    }

    static Type1Font? ParseCore(byte[] raw)
    {
        var data = raw.Length > 6 && raw[0] == 0x80 ? StripPfbHeaders(raw) : raw;
        int eexec = IndexOf(data, "eexec", 0);
        if (eexec < 0) return null;
        var clear = System.Text.Encoding.Latin1.GetString(data, 0, eexec);

        int start = eexec + 5;
        while (start < data.Length && data[start] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') start++;
        var cipher = data.AsSpan(start).ToArray();
        // A hexadecimal eexec section (PFA) is four or more hex digits; binary ones are checked by their first four bytes.
        if (cipher.Length >= 4 && Array.TrueForAll(cipher[..4], IsHexOrSpace)) cipher = FromHex(cipher);
        var privateBytes = Decrypt(cipher, EexecKey, 4);

        var font = new Type1Font();
        font.ReadClearText(clear);
        font.ReadPrivate(privateBytes);
        return font._charStrings.Count == 0 ? null : font;
    }

    static byte[] StripPfbHeaders(byte[] pfb)
    {
        var o = new MemoryStream();
        int p = 0;
        while (p + 6 <= pfb.Length && pfb[p] == 0x80)
        {
            int type = pfb[p + 1];
            if (type == 3) break;
            int length = pfb[p + 2] | (pfb[p + 3] << 8) | (pfb[p + 4] << 16) | (pfb[p + 5] << 24);
            if (length < 0 || p + 6 + length > pfb.Length) length = pfb.Length - p - 6;
            o.Write(pfb, p + 6, length);
            p += 6 + length;
        }
        return o.ToArray();
    }

    static bool IsHexOrSpace(byte b) => b is >= (byte)'0' and <= (byte)'9' or >= (byte)'a' and <= (byte)'f' or >= (byte)'A' and <= (byte)'F' or (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t';

    static byte[] FromHex(byte[] hex)
    {
        var o = new List<byte>(hex.Length / 2);
        int hi = -1;
        foreach (byte b in hex)
        {
            int v = b switch { >= (byte)'0' and <= (byte)'9' => b - '0', >= (byte)'a' and <= (byte)'f' => b - 'a' + 10, >= (byte)'A' and <= (byte)'F' => b - 'A' + 10, _ => -1 };
            if (v < 0) continue;
            if (hi < 0) hi = v; else { o.Add((byte)(hi * 16 + v)); hi = -1; }
        }
        return o.ToArray();
    }

    /// <summary>Undoes the Type 1 stream cipher (<paramref name="key"/> 55665 for eexec, 4330 for charstrings) and drops the first <paramref name="skip"/> bytes.</summary>
    internal static byte[] Decrypt(byte[] cipher, ushort key, int skip)
    {
        var plain = new byte[cipher.Length];
        ushort r = key;
        for (int i = 0; i < cipher.Length; i++)
        {
            byte c = cipher[i];
            plain[i] = (byte)(c ^ (r >> 8));
            r = (ushort)((c + r) * C1 + C2);
        }
        return skip >= plain.Length ? [] : plain[skip..];
    }

    internal static int IndexOf(byte[] data, string needle, int from)
    {
        var n = System.Text.Encoding.ASCII.GetBytes(needle);
        return from >= data.Length ? -1 : data.AsSpan(from).IndexOf(n) is var i and >= 0 ? i + from : -1;
    }

    // ------------------------------------------------------------ clear text

    void ReadClearText(string text)
    {
        var name = System.Text.RegularExpressions.Regex.Match(text, @"/FontName\s*/(\S+)");
        if (name.Success) Name = name.Groups[1].Value;
        if (Numbers(text, "/FontMatrix") is { Length: 6 } fm) FontMatrix = fm;
        if (Numbers(text, "/FontBBox") is { Length: 4 } bb) FontBBox = bb;

        int enc = text.IndexOf("/Encoding", StringComparison.Ordinal);
        if (enc >= 0 && !text.AsSpan(enc, Math.Min(60, text.Length - enc)).Contains("StandardEncoding", StringComparison.Ordinal))
        {
            var map = new Dictionary<int, string>();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text[enc..], @"dup\s+(\d+)\s*/(\S+)\s+put"))
                map[int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)] = m.Groups[2].Value;
            if (map.Count > 0) Encoding = map;
        }
    }

    static double[]? Numbers(string text, string key)
    {
        int at = text.IndexOf(key, StringComparison.Ordinal);
        if (at < 0) return null;
        int open = text.IndexOfAny(['[', '{'], at);
        int close = open < 0 ? -1 : text.IndexOfAny([']', '}'], open);
        if (open < 0 || close < 0) return null;
        var parts = text[(open + 1)..close].Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var values = new double[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])) return null;
        return values;
    }

    // ------------------------------------------------------------ private part: Subrs and CharStrings

    void ReadPrivate(byte[] d)
    {
        int lenIV = 4;
        int p = IndexOf(d, "/lenIV", 0);
        if (p >= 0) { int q = p + 6; lenIV = (int)ReadNumber(d, ref q); }

        int subrs = IndexOf(d, "/Subrs", 0);
        int charStrings = IndexOf(d, "/CharStrings", 0);
        if (charStrings < 0) return;
        if (subrs >= 0 && subrs < charStrings)
        {
            int q = subrs + 6;
            int count = (int)ReadNumber(d, ref q);
            ReadToken(d, ref q);                             // "array"
            for (int i = 0; i < count; i++) _subrs.Add(null);
            while (true)
            {
                int save = q;
                string token = ReadToken(d, ref q);
                if (token != "dup") { q = save; break; }
                int index = (int)ReadNumber(d, ref q);
                int length = (int)ReadNumber(d, ref q);
                ReadToken(d, ref q);                         // RD or -|
                q++;                                         // the single space before the binary data
                if (index >= 0 && index < _subrs.Count && q + length <= d.Length)
                    _subrs[index] = DecryptCharString(d.AsSpan(q, length).ToArray(), lenIV);
                q += length;
                if (ReadToken(d, ref q) == "noaccess") ReadToken(d, ref q);   // NP, |, or "noaccess put"
            }
        }

        int c = charStrings + 12;
        // Skip "N dict dup begin".
        while (c < d.Length)
        {
            int save = c;
            string token = ReadToken(d, ref c);
            if (token == "begin") break;
            if (token.Length == 0) { c = save + 1; }
        }
        while (c < d.Length)
        {
            SkipWhite(d, ref c);
            if (c >= d.Length || d[c] != '/') break;
            c++;
            string name = ReadToken(d, ref c);
            int length = (int)ReadNumber(d, ref c);
            ReadToken(d, ref c);                             // RD
            c++;
            if (length < 0 || c + length > d.Length) break;
            if (!_charStrings.ContainsKey(name)) _glyphOrder.Add(name);
            _charStrings[name] = DecryptCharString(d.AsSpan(c, length).ToArray(), lenIV);
            c += length;
            if (ReadToken(d, ref c) == "noaccess") ReadToken(d, ref c);   // ND, |-, or "noaccess def"
        }
    }

    static byte[] DecryptCharString(byte[] cipher, int lenIV) => lenIV < 0 ? cipher : Decrypt(cipher, CharStringKey, lenIV);

    static void SkipWhite(byte[] d, ref int p)
    {
        while (p < d.Length && d[p] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or 0) p++;
    }

    static string ReadToken(byte[] d, ref int p)
    {
        SkipWhite(d, ref p);
        int s = p;
        while (p < d.Length && d[p] is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or 0 or (byte)'/' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}')) p++;
        if (p == s && p < d.Length) p++;
        return System.Text.Encoding.Latin1.GetString(d, s, p - s);
    }

    static double ReadNumber(byte[] d, ref int p)
    {
        string token = ReadToken(d, ref p);
        return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    // ------------------------------------------------------------ interpreting charstrings

    internal readonly record struct Point(double X, double Y);

    internal sealed class Contour
    {
        public Point Start;
        public readonly List<(Point C1, Point C2, Point End, bool IsLine)> Segments = [];
    }

    internal sealed class Outline
    {
        public double Width;
        public readonly List<Contour> Contours = [];
    }

    /// <summary>Runs the charstring of <paramref name="name"/> and returns its outline in glyph space; null when the font has no such glyph.</summary>
    internal Outline? GetOutline(string name, int depth = 0)
    {
        if (!_charStrings.TryGetValue(name, out var code)) return null;
        var runner = new Interpreter(this, depth);
        runner.Run(code, 0);
        return runner.Result;
    }

    sealed class Interpreter(Type1Font font, int depth)
    {
        readonly List<double> _stack = [];
        readonly List<double> _ps = [];
        readonly List<Point> _flex = [];
        Point _cur;
        double _sbx;
        bool _flexing, _done;
        Contour? _contour;
        public Outline Result { get; } = new();

        public void Run(byte[] code, int level)
        {
            if (level > 12) return;
            for (int i = 0; i < code.Length && !_done;)
            {
                int b = code[i++];
                if (b >= 32)
                {
                    if (b <= 246) _stack.Add(b - 139);
                    else if (b <= 250) _stack.Add((b - 247) * 256 + code[i++] + 108);
                    else if (b <= 254) _stack.Add(-(b - 251) * 256 - code[i++] - 108);
                    else { _stack.Add((code[i] << 24) | (code[i + 1] << 16) | (code[i + 2] << 8) | code[i + 3]); i += 4; }
                    continue;
                }
                switch (b)
                {
                    case 1 or 3: _stack.Clear(); break;                                  // hstem, vstem
                    case 4: MoveTo(0, Pop(0)); break;                                    // vmoveto
                    case 5: { var (dx, dy) = (Arg(0), Arg(1)); LineTo(dx, dy); } break;
                    case 6: LineTo(Arg(0), 0); break;
                    case 7: LineTo(0, Arg(0)); break;
                    case 8: Curve(Arg(0), Arg(1), Arg(2), Arg(3), Arg(4), Arg(5)); break;
                    case 9: _stack.Clear(); break;                                       // closepath: contours are closed when filled
                    case 10: { int n = (int)Pop(0); if (n >= 0 && n < font._subrs.Count && font._subrs[n] is { } sub) Run(sub, level + 1); } break;
                    case 11: return;                                                     // return
                    case 13: _sbx = Arg(0); Result.Width = Arg(1); _cur = new Point(_sbx, 0); _stack.Clear(); break;   // hsbw
                    case 14: _done = true; break;                                        // endchar
                    case 21: MoveTo(Arg(0), Arg(1)); break;
                    case 22: MoveTo(Arg(0), 0); break;
                    case 30: Curve(0, Arg(0), Arg(1), Arg(2), Arg(3), 0); break;         // vhcurveto
                    case 31: Curve(Arg(0), 0, Arg(1), Arg(2), 0, Arg(3)); break;         // hvcurveto
                    case 12: Escape(code[i++]); break;
                    default: _stack.Clear(); break;
                }
            }
        }

        double Arg(int i) => i < _stack.Count ? _stack[i] : 0;

        double Pop(double fallback)
        {
            if (_stack.Count == 0) return fallback;
            double v = _stack[^1];
            _stack.RemoveAt(_stack.Count - 1);
            return v;
        }

        void MoveTo(double dx, double dy)
        {
            _cur = new Point(_cur.X + dx, _cur.Y + dy);
            _stack.Clear();
            if (_flexing) return;
            _contour = new Contour { Start = _cur };
            Result.Contours.Add(_contour);
        }

        void LineTo(double dx, double dy)
        {
            Begin();
            _cur = new Point(_cur.X + dx, _cur.Y + dy);
            _contour!.Segments.Add((default, default, _cur, true));
            _stack.Clear();
        }

        void Curve(double dx1, double dy1, double dx2, double dy2, double dx3, double dy3)
        {
            Begin();
            var c1 = new Point(_cur.X + dx1, _cur.Y + dy1);
            var c2 = new Point(c1.X + dx2, c1.Y + dy2);
            _cur = new Point(c2.X + dx3, c2.Y + dy3);
            _contour!.Segments.Add((c1, c2, _cur, false));
            _stack.Clear();
        }

        /// <summary>A path operator with no moveto before it starts a contour at the current point.</summary>
        void Begin()
        {
            if (_contour is not null) return;
            _contour = new Contour { Start = _cur };
            Result.Contours.Add(_contour);
        }

        void Escape(int op)
        {
            switch (op)
            {
                case 0 or 1 or 2: _stack.Clear(); break;                                 // dotsection, vstem3, hstem3
                case 6: Seac(); break;
                case 7: _sbx = Arg(0); Result.Width = Arg(2); _cur = new Point(Arg(0), Arg(1)); _stack.Clear(); break;   // sbw
                case 12: { double b = Pop(1), a = Pop(0); _stack.Add(b == 0 ? 0 : a / b); } break;                       // div
                case 16: CallOtherSubr(); break;
                case 17: _stack.Add(_ps.Count > 0 ? Take(_ps) : 0); break;               // pop
                case 33: _cur = new Point(Arg(0), Arg(1)); _stack.Clear(); break;        // setcurrentpoint
                default: _stack.Clear(); break;
            }
        }

        static double Take(List<double> list)
        {
            double v = list[^1];
            list.RemoveAt(list.Count - 1);
            return v;
        }

        void CallOtherSubr()
        {
            int number = (int)Pop(0);
            int count = (int)Pop(0);
            var args = new List<double>();
            for (int i = 0; i < count; i++) args.Add(Pop(0));        // args[0] is the last one pushed
            switch (number)
            {
                case 1: _flexing = true; _flex.Clear(); break;                           // start flex
                case 2: if (_flexing) _flex.Add(_cur); break;                            // a flex point
                case 0:                                                                  // end flex: three arguments (height, x, y)
                    _flexing = false;
                    if (_flex.Count >= 7)
                    {
                        Begin();
                        _contour!.Segments.Add((_flex[1], _flex[2], _flex[3], false));
                        _contour.Segments.Add((_flex[4], _flex[5], _flex[6], false));
                        _cur = _flex[6];
                    }
                    // The following pops give back x then y of the end point.
                    _ps.Clear();
                    _ps.Add(args.Count > 0 ? args[0] : _cur.Y);
                    _ps.Add(args.Count > 1 ? args[1] : _cur.X);
                    break;
                case 3: _ps.Clear(); _ps.Add(args.Count > 0 ? args[0] : 3); break;       // hint replacement: hand the subr number back
                default:
                    _ps.Clear();
                    for (int i = args.Count - 1; i >= 0; i--) _ps.Add(args[i]);
                    break;
            }
        }

        void Seac()
        {
            double asb = Arg(0), adx = Arg(1), ady = Arg(2);
            int bchar = (int)Arg(3), achar = (int)Arg(4);
            _stack.Clear();
            if (depth >= 2) { _done = true; return; }
            string? baseName = PdfGlyphNames.StandardEncodingName(bchar), accentName = PdfGlyphNames.StandardEncodingName(achar);
            if (baseName is not null && font.GetOutline(baseName, depth + 1) is { } baseGlyph)
                Result.Contours.AddRange(baseGlyph.Contours);
            if (accentName is not null && font.GetOutline(accentName, depth + 1) is { } accent)
            {
                // The accent's own left side bearing point lands at (adx, ady) from the composite's.
                double dx = _sbx + adx - asb, dy = ady;
                foreach (var c in accent.Contours) Result.Contours.Add(Translate(c, dx, dy));
            }
            _done = true;
        }

        static Contour Translate(Contour c, double dx, double dy)
        {
            Point T(Point p) => new(p.X + dx, p.Y + dy);
            var o = new Contour { Start = T(c.Start) };
            foreach (var s in c.Segments) o.Segments.Add((T(s.C1), T(s.C2), T(s.End), s.IsLine));
            return o;
        }
    }

    // ------------------------------------------------------------ to CFF

    /// <summary>
    /// Builds a name-keyed CFF program from the font: Type 1 charstrings converted to Type 2 (absolute coordinates are re-expressed
    /// relative, flex becomes two curves, accents composed by seac are flattened into their composite), the charset from the glyph names and
    /// the font's encoding as a custom CFF encoding. Hinting is dropped.
    /// </summary>
    public byte[] ToCff()
    {
        // Glyph 0 is .notdef; glyphs that have an encoded code come next in code order (the CFF encoding assigns codes to consecutive glyphs).
        var encoding = new Dictionary<int, string>();
        if (Encoding is not null) foreach (var kv in Encoding) encoding[kv.Key] = kv.Value;
        else for (int code = 0; code < 256; code++) if (PdfGlyphNames.StandardEncodingName(code) is { } n) encoding[code] = n;

        var order = new List<string> { ".notdef" };
        var codes = new List<int>();
        var seen = new HashSet<string> { ".notdef" };
        foreach (var code in encoding.Keys.OrderBy(k => k))
        {
            var glyph = encoding[code];
            if (!_charStrings.ContainsKey(glyph) || !seen.Add(glyph)) continue;
            order.Add(glyph);
            codes.Add(code);
        }
        foreach (var glyph in _glyphOrder) if (seen.Add(glyph)) order.Add(glyph);

        var strings = new List<string>();
        int Sid(string name)
        {
            int standard = Array.IndexOf(PdfGlyphNames.StandardStrings, name);
            if (standard >= 0) return standard;
            int index = strings.IndexOf(name);
            if (index < 0) { strings.Add(name); index = strings.Count - 1; }
            return PdfGlyphNames.StandardStringCount + index;
        }

        var charStringBytes = new List<byte[]>();
        foreach (var name in order)
        {
            var outline = name == ".notdef" && !_charStrings.ContainsKey(name) ? null : GetOutline(name);
            charStringBytes.Add(Type2(outline));
        }
        var charset = new List<byte> { 0 };
        for (int g = 1; g < order.Count; g++)
        {
            int sid = Sid(order[g]);
            charset.Add((byte)(sid >> 8));
            charset.Add((byte)sid);
        }
        var encodingTable = new List<byte> { 0, (byte)codes.Count };
        foreach (var code in codes) encodingTable.Add((byte)code);

        var nameIndex = CffWriter.Index([System.Text.Encoding.ASCII.GetBytes(Name)]);
        var stringIndex = CffWriter.Index(strings.ConvertAll(s => System.Text.Encoding.Latin1.GetBytes(s)).ToArray());
        var gsubrIndex = CffWriter.Index([]);
        var charStringIndex = CffWriter.Index(charStringBytes.ToArray());
        byte[] privateDict = [139, 20, 139, 21];                       // defaultWidthX 0, nominalWidthX 0: widths are written in full

        byte[] header = [1, 0, 4, 4];
        int TopSize() => CffWriter.Index([CffWriter.TopDict(this, 0, 0, 0, 0, 0)]).Length;
        int afterIndexes = header.Length + nameIndex.Length + TopSize() + stringIndex.Length + gsubrIndex.Length;
        int charsetOffset = afterIndexes;
        int encodingOffset = charsetOffset + charset.Count;
        int privateOffset = encodingOffset + encodingTable.Count;
        int charStringsOffset = privateOffset + privateDict.Length;
        var top = CffWriter.Index([CffWriter.TopDict(this, charsetOffset, encodingOffset, charStringsOffset, privateDict.Length, privateOffset)]);

        var cff = new List<byte>(header);
        cff.AddRange(nameIndex);
        cff.AddRange(top);
        cff.AddRange(stringIndex);
        cff.AddRange(gsubrIndex);
        cff.AddRange(charset);
        cff.AddRange(encodingTable);
        cff.AddRange(privateDict);
        cff.AddRange(charStringIndex);
        return cff.ToArray();
    }

    /// <summary>Encodes an outline as a Type 2 charstring: the width, then relative moves, lines and curves, then endchar.</summary>
    static byte[] Type2(Outline? outline)
    {
        var o = new List<byte>();
        double width = outline?.Width ?? 0;
        bool first = true;
        Point cur = default;

        void Num(double v)
        {
            int n = (int)Math.Round(v);
            if (n is >= -107 and <= 107) o.Add((byte)(n + 139));
            else if (n is >= 108 and <= 1131) { n -= 108; o.Add((byte)(247 + (n >> 8))); o.Add((byte)(n & 255)); }
            else if (n is >= -1131 and <= -108) { n = -n - 108; o.Add((byte)(251 + (n >> 8))); o.Add((byte)(n & 255)); }
            else { n = Math.Clamp(n, short.MinValue, short.MaxValue); o.Add(28); o.Add((byte)(n >> 8)); o.Add((byte)n); }
        }

        if (outline is not null)
            foreach (var contour in outline.Contours)
            {
                if (contour.Segments.Count == 0) continue;
                if (first) { Num(width); first = false; }
                Num(contour.Start.X - cur.X); Num(contour.Start.Y - cur.Y); o.Add(21);       // rmoveto
                cur = contour.Start;
                foreach (var s in contour.Segments)
                {
                    if (s.IsLine) { Num(s.End.X - cur.X); Num(s.End.Y - cur.Y); o.Add(5); }
                    else
                    {
                        Num(s.C1.X - cur.X); Num(s.C1.Y - cur.Y);
                        Num(s.C2.X - s.C1.X); Num(s.C2.Y - s.C1.Y);
                        Num(s.End.X - s.C2.X); Num(s.End.Y - s.C2.Y);
                        o.Add(8);
                    }
                    cur = s.End;
                }
            }
        if (first) Num(width);                                                              // a glyph with no outline still has a width
        o.Add(14);                                                                          // endchar
        return o.ToArray();
    }
}

/// <summary>Writers for the pieces of a CFF program the Type 1 converter produces.</summary>
internal static class CffWriter
{
    /// <summary>An INDEX with four-byte offsets.</summary>
    public static byte[] Index(byte[][] items)
    {
        var o = new List<byte> { (byte)(items.Length >> 8), (byte)items.Length };
        if (items.Length == 0) return o.ToArray();
        o.Add(4);
        long offset = 1;
        void Off(long v) { o.Add((byte)(v >> 24)); o.Add((byte)(v >> 16)); o.Add((byte)(v >> 8)); o.Add((byte)v); }
        Off(offset);
        foreach (var item in items) { offset += item.Length; Off(offset); }
        foreach (var item in items) o.AddRange(item);
        return o.ToArray();
    }

    static void Int5(List<byte> d, int v) { d.Add(29); d.Add((byte)(v >> 24)); d.Add((byte)(v >> 16)); d.Add((byte)(v >> 8)); d.Add((byte)v); }

    /// <summary>A real number in the DICT's packed-decimal form.</summary>
    static void Real(List<byte> d, double v)
    {
        d.Add(30);
        var text = v.ToString("0.#########", CultureInfo.InvariantCulture);
        var nibbles = new List<int>();
        foreach (char c in text)
        {
            if (c is >= '0' and <= '9') nibbles.Add(c - '0');
            else if (c == '.') nibbles.Add(10);
            else if (c == '-') nibbles.Add(14);
        }
        nibbles.Add(15);
        if (nibbles.Count % 2 == 1) nibbles.Add(15);
        for (int i = 0; i < nibbles.Count; i += 2) d.Add((byte)((nibbles[i] << 4) | nibbles[i + 1]));
    }

    public static byte[] TopDict(Type1Font font, int charset, int encoding, int charStrings, int privateSize, int privateOffset)
    {
        var d = new List<byte>();
        foreach (var v in font.FontMatrix) Real(d, v);
        d.AddRange([12, 7]);
        foreach (var v in font.FontBBox) Int5(d, (int)Math.Round(v));
        d.Add(5);
        Int5(d, charset); d.Add(15);
        Int5(d, encoding); d.Add(16);
        Int5(d, charStrings); d.Add(17);
        Int5(d, privateSize); Int5(d, privateOffset); d.Add(18);
        return d.ToArray();
    }
}
