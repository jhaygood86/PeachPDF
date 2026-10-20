using System.IO;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using PeachDrawing.Core;
using PeachDrawing.PDF.Engine;

namespace PeachDrawing.PDF.Import;

/// <summary>One shown character code of a string, resolved against its font.</summary>
internal readonly record struct PdfShownGlyph(int Code, int GlyphIndex, double Width, string Text, bool IsWordSpace);

/// <summary>
/// A font of an imported document, reduced to what showing text needs: how a string splits into character codes, which
/// glyph and advance each code has, and the Unicode text each stands for (the font's <c>/ToUnicode</c> map first, then
/// its encoding). Embedded TrueType and OpenType programs are drawn by glyph index through the canvas; a font with
/// no embedded sfnt program (a bare CFF or Type 1 program, a Type 3 font, a standard-14 name) is not supported yet.
/// </summary>
internal sealed class PdfImportedFont
{
    readonly bool _composite;
    readonly string _family;
    readonly RenderContext _context;
    readonly Dictionary<int, string> _toUnicode;
    readonly Dictionary<int, double> _widths = new();
    readonly double _defaultWidth;
    readonly byte[]? _cidToGid;
    readonly Func<int, string?> _encodingText;
    readonly Font _probe;

    PdfImportedFont(bool composite, string family, RenderContext context, Dictionary<int, string> toUnicode, double defaultWidth,
        byte[]? cidToGid, Func<int, string?> encodingText, Font probe)
    {
        _composite = composite;
        _family = family;
        _context = context;
        _toUnicode = toUnicode;
        _defaultWidth = defaultWidth;
        _cidToGid = cidToGid;
        _encodingText = encodingText;
        _probe = probe;
    }

    /// <summary>The font at <paramref name="size"/>, or null when the context cannot make it.</summary>
    public Font? GetFont(double size) => _context.GetFont(_family, size, PaintFontStyle.Regular);

    static readonly ConditionalWeakTable<RenderContext, HashSet<string>> Registered = new();

    /// <summary>Loads <paramref name="fontDict"/>; returns null and a reason in <paramref name="why"/> when it cannot be drawn.</summary>
    public static PdfImportedFont? Load(PdfSourceDocument doc, PdfDictionary fontDict, RenderContext context, out string? why)
    {
        why = null;
        string? subtype = doc.NameOf(fontDict.GetRaw("Subtype"));
        bool composite = subtype == "/Type0";
        PdfDictionary descriptorOwner = fontDict;
        PdfDictionary? cidFont = null;
        if (composite)
        {
            string? encoding = doc.NameOf(fontDict.GetRaw("Encoding"));
            if (encoding is not ("/Identity-H" or "/Identity-V"))
            {
                why = "Type0 encoding " + (encoding ?? "(embedded CMap)");
                return null;
            }
            cidFont = doc.ArrayOf(fontDict, "DescendantFonts") is { Elements.Count: > 0 } df ? doc.Resolve(df.Elements[0]) as PdfDictionary : null;
            if (cidFont is null) { why = "Type0 without descendant"; return null; }
            descriptorOwner = cidFont;
        }
        else if (subtype is not ("/TrueType" or "/Type1" or "/MMType1"))
        {
            why = "font " + subtype;
            return null;
        }

        var descriptor = doc.DictOf(descriptorOwner, "FontDescriptor");
        PdfDictionary? file = null;
        if (descriptor is not null)
        {
            file = doc.DictOf(descriptor, "FontFile2");
            if (file is null && doc.DictOf(descriptor, "FontFile3") is { } f3 && doc.NameOf(f3.GetRaw("Subtype")) == "/OpenType") file = f3;
        }
        if (file is null) { why = "no embedded TrueType/OpenType program"; return null; }
        byte[] data = doc.DecodeStream(file).Data;
        if (!IsSfnt(data)) { why = "embedded font is not an sfnt program"; return null; }

        string family = "pdfimport-" + Convert.ToHexString(SHA1.HashData(data))[..16];
        var set = Registered.GetOrCreateValue(context);
        Font? probe;
        lock (set)
        {
            if (set.Add(family))
            {
                try { context.AddFontFromBytes(family, SfntRepair.AddMissingTables(data, family)).GetAwaiter().GetResult(); }
                catch (Exception)
                {
                    // An untrusted embedded program: any failure to read it means the font is reported unsupported, not thrown.
                    set.Remove(family);
                    why = "embedded font could not be loaded";
                    return null;
                }
            }
            probe = context.GetFont(family, 10, PaintFontStyle.Regular);
        }
        if (probe is null) { why = "embedded font could not be loaded"; return null; }

        var toUnicode = new Dictionary<int, string>();
        if (doc.Resolve(fontDict.GetRaw("ToUnicode")) is PdfDictionary { Stream: not null } tu)
            ParseToUnicode(doc.DecodeStream(tu).Data, toUnicode);

        double defaultWidth = 0;
        byte[]? cidToGid = null;
        PdfImportedFont font;
        if (composite)
        {
            defaultWidth = doc.NumberOf(cidFont!.GetRaw("DW"), 1000) / 1000.0;
            if (doc.Resolve(cidFont!.GetRaw("CIDToGIDMap")) is PdfDictionary { Stream: not null } map) cidToGid = doc.DecodeStream(map).Data;
            font = new PdfImportedFont(true, family, context, toUnicode, defaultWidth, cidToGid, _ => null, probe);
            ReadCidWidths(doc, cidFont!, font._widths);
        }
        else
        {
            var enc = BuildSimpleEncoding(doc, fontDict);
            var typeface = probe.Typeface;
            Func<int, string?> text = code => enc(code);
            font = new PdfImportedFont(false, family, context, toUnicode, doc.NumberOf(descriptor!.GetRaw("MissingWidth")) / 1000.0, null, text, probe);
            int first = doc.IntOf(fontDict.GetRaw("FirstChar"));
            if (doc.ArrayOf(fontDict, "Widths") is { } w)
                for (int i = 0; i < w.Elements.Count; i++) font._widths[first + i] = doc.NumberOf(w.Elements[i]) / 1000.0;
            _ = typeface;
        }
        return font;
    }

    static bool IsSfnt(byte[] d) => d.Length > 12 && (
        (d[0] == 0 && d[1] == 1 && d[2] == 0 && d[3] == 0) ||
        (d[0] == 'O' && d[1] == 'T' && d[2] == 'T' && d[3] == 'O') ||
        (d[0] == 't' && d[1] == 'r' && d[2] == 'u' && d[3] == 'e') ||
        (d[0] == 't' && d[1] == 't' && d[2] == 'c' && d[3] == 'f'));

    static void ReadCidWidths(PdfSourceDocument doc, PdfDictionary cidFont, Dictionary<int, double> widths)
    {
        if (doc.ArrayOf(cidFont, "W") is not { } w) return;
        int i = 0;
        while (i < w.Elements.Count)
        {
            int first = doc.IntOf(w.Elements[i]);
            if (i + 1 >= w.Elements.Count) break;
            if (doc.Resolve(w.Elements[i + 1]) is PdfArray list)
            {
                for (int k = 0; k < list.Elements.Count; k++) widths[first + k] = doc.NumberOf(list.Elements[k]) / 1000.0;
                i += 2;
            }
            else if (i + 2 < w.Elements.Count)
            {
                int last = doc.IntOf(w.Elements[i + 1]);
                double width = doc.NumberOf(w.Elements[i + 2]) / 1000.0;
                for (int c = first; c <= last && c - first < 70000; c++) widths[c] = width;
                i += 3;
            }
            else break;
        }
    }

    // ------------------------------------------------------------ showing

    /// <summary>Splits a shown string (one char per byte) into glyphs.</summary>
    public IEnumerable<PdfShownGlyph> Decode(string shown)
    {
        if (_composite)
        {
            for (int i = 0; i + 1 < shown.Length; i += 2)
            {
                int cid = (shown[i] << 8) | shown[i + 1];
                int gid = cid;
                if (_cidToGid is not null && cid * 2 + 1 < _cidToGid.Length) gid = (_cidToGid[cid * 2] << 8) | _cidToGid[cid * 2 + 1];
                _toUnicode.TryGetValue(cid, out var text);
                yield return new PdfShownGlyph(cid, gid, _widths.TryGetValue(cid, out var w) ? w : _defaultWidth, text ?? "", false);
            }
            yield break;
        }
        var typeface = _probe.Typeface;
        foreach (char ch in shown)
        {
            int code = ch & 0xFF;
            string? text = _toUnicode.TryGetValue(code, out var t) ? t : _encodingText(code);
            int gid = 0;
            if (typeface is not null)
            {
                ushort mapped = 0;
                bool found = false;
                if (!string.IsNullOrEmpty(text) && Rune.TryGetRuneAt(text, 0, out var rune)) found = typeface.TryMapRune(rune, out mapped) && mapped != 0;
                if (!found) found = typeface.TryMapRune(new Rune(code), out mapped) && mapped != 0;
                if (!found) found = typeface.TryMapRune(new Rune(0xF000 + code), out mapped) && mapped != 0;
                gid = found ? mapped : 0;
                if (!_widths.ContainsKey(code) && gid != 0)
                    _widths[code] = typeface.GetAdvance(mapped) / (double)Math.Max(1, typeface.Metrics.UnitsPerEm);
            }
            yield return new PdfShownGlyph(code, gid, _widths.TryGetValue(code, out var w) ? w : _defaultWidth, text ?? "", code == 32);
        }
    }

    // ------------------------------------------------------------ ToUnicode

    /// <summary>Reads the <c>bfchar</c> and <c>bfrange</c> sections of a ToUnicode CMap into <paramref name="map"/>.</summary>
    internal static void ParseToUnicode(byte[] cmap, Dictionary<int, string> map)
    {
        var lx = new PdfLexer(cmap);
        var prev = new List<PdfToken>();
        while (true)
        {
            var t = lx.Next();
            if (t.Kind == PdfTokenKind.Eof) break;
            if (t.Kind != PdfTokenKind.Keyword) { prev.Add(t); if (prev.Count > 4) prev.RemoveAt(0); continue; }
            if (t.Text == "beginbfchar")
            {
                while (true)
                {
                    var a = lx.Next();
                    if (a.Kind != PdfTokenKind.String) break;
                    var b = lx.Next();
                    if (b.Kind != PdfTokenKind.String) break;
                    map[Code(a.Text)] = Utf16(b.Text);
                }
            }
            else if (t.Text == "beginbfrange")
            {
                while (true)
                {
                    var a = lx.Next();
                    if (a.Kind != PdfTokenKind.String) break;
                    var b = lx.Next();
                    var c = lx.Next();
                    if (b.Kind != PdfTokenKind.String) break;
                    int lo = Code(a.Text), hi = Code(b.Text);
                    if (c.Kind == PdfTokenKind.String)
                    {
                        string start = Utf16(c.Text);
                        for (int code = lo; code <= hi && code - lo < 70000; code++)
                            map[code] = Increment(start, code - lo);
                    }
                    else if (c.Kind == PdfTokenKind.ArrayStart)
                    {
                        int code = lo;
                        while (true)
                        {
                            var e = lx.Next();
                            if (e.Kind != PdfTokenKind.String) break;
                            map[code++] = Utf16(e.Text);
                        }
                    }
                    else break;
                }
            }
            prev.Clear();
        }
    }

    static int Code(string bytes)
    {
        int v = 0;
        foreach (char ch in bytes) v = (v << 8) | (ch & 0xFF);
        return v;
    }

    static string Utf16(string bytes)
    {
        var raw = new byte[bytes.Length];
        for (int i = 0; i < raw.Length; i++) raw[i] = (byte)bytes[i];
        return Encoding.BigEndianUnicode.GetString(raw);
    }

    static string Increment(string start, int delta)
    {
        if (start.Length == 0) return start;
        var chars = start.ToCharArray();
        chars[^1] = (char)(chars[^1] + delta);
        return new string(chars);
    }

    // ------------------------------------------------------------ simple-font encodings

    static readonly char[] WinAnsiHigh =
    [
        '€', '\u0081', '‚', 'ƒ', '„', '…', '†', '‡', 'ˆ', '‰', 'Š', '‹', 'Œ', '\u008D', 'Ž', '\u008F',
        '\u0090', '‘', '’', '“', '”', '•', '–', '—', '˜', '™', 'š', '›', 'œ', '\u009D', 'ž', 'Ÿ',
    ];

    static readonly Dictionary<string, string> GlyphNames = new()
    {
        ["space"] = " ", ["exclam"] = "!", ["quotedbl"] = "\"", ["numbersign"] = "#", ["dollar"] = "$", ["percent"] = "%", ["ampersand"] = "&",
        ["quotesingle"] = "'", ["parenleft"] = "(", ["parenright"] = ")", ["asterisk"] = "*", ["plus"] = "+", ["comma"] = ",", ["hyphen"] = "-",
        ["period"] = ".", ["slash"] = "/", ["colon"] = ":", ["semicolon"] = ";", ["less"] = "<", ["equal"] = "=", ["greater"] = ">", ["question"] = "?",
        ["at"] = "@", ["bracketleft"] = "[", ["backslash"] = "\\", ["bracketright"] = "]", ["asciicircum"] = "^", ["underscore"] = "_", ["grave"] = "`",
        ["braceleft"] = "{", ["bar"] = "|", ["braceright"] = "}", ["asciitilde"] = "~", ["zero"] = "0", ["one"] = "1", ["two"] = "2", ["three"] = "3",
        ["four"] = "4", ["five"] = "5", ["six"] = "6", ["seven"] = "7", ["eight"] = "8", ["nine"] = "9", ["bullet"] = "•", ["endash"] = "–",
        ["emdash"] = "—", ["quoteleft"] = "‘", ["quoteright"] = "’", ["quotedblleft"] = "“", ["quotedblright"] = "”",
        ["fi"] = "fi", ["fl"] = "fl", ["ff"] = "ff", ["ffi"] = "ffi", ["ffl"] = "ffl", ["Euro"] = "€", ["nbspace"] = " ", ["periodcentered"] = "·",
    };

    static string? NameToText(string name)
    {
        if (GlyphNames.TryGetValue(name, out var t)) return t;
        if (name.Length == 1) return name;
        if (name.StartsWith("uni", StringComparison.Ordinal) && name.Length >= 7 && int.TryParse(name.AsSpan(3, 4), System.Globalization.NumberStyles.HexNumber, null, out int u)) return char.ConvertFromUtf32(u);
        if (name[0] == 'u' && name.Length is >= 5 and <= 7 && int.TryParse(name.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out int u2) && u2 <= 0x10FFFF) return char.ConvertFromUtf32(u2);
        return null;
    }

    static Func<int, string?> BuildSimpleEncoding(PdfSourceDocument doc, PdfDictionary fontDict)
    {
        var differences = new Dictionary<int, string>();
        string? baseName = null;
        var enc = doc.Resolve(fontDict.GetRaw("Encoding"));
        if (enc is PdfName n) baseName = n.Value;
        else if (enc is PdfDictionary ed)
        {
            baseName = doc.NameOf(ed.GetRaw("BaseEncoding"));
            if (doc.ArrayOf(ed, "Differences") is { } diff)
            {
                int code = 0;
                foreach (var item in diff.Elements)
                {
                    var r = doc.Resolve(item);
                    if (r is PdfInteger or PdfReal) code = doc.IntOf(r);
                    else if (r is PdfName gn)
                    {
                        if (NameToText(gn.Value.TrimStart('/')) is { } text) differences[code] = text;
                        code++;
                    }
                }
            }
        }
        return code =>
        {
            if (differences.TryGetValue(code, out var d)) return d;
            if (code is >= 0x80 and <= 0x9F && baseName is null or "/WinAnsiEncoding") return WinAnsiHigh[code - 0x80].ToString();
            return code is >= 0x20 ? ((char)code).ToString() : null;
        };
    }
}
