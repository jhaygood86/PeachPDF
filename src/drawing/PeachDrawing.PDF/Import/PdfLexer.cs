using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text;

namespace PeachDrawing.PDF.Import;

/// <summary>The kinds of token <see cref="PdfLexer"/> produces.</summary>
internal enum PdfTokenKind
{
    /// <summary>End of data.</summary>
    Eof,
    /// <summary>An integer.</summary>
    Integer,
    /// <summary>A real number.</summary>
    Real,
    /// <summary>A name; <see cref="PdfToken.Text"/> holds the decoded name without its slash.</summary>
    Name,
    /// <summary>A string; <see cref="PdfToken.Text"/> holds one char per byte (0-255).</summary>
    String,
    /// <summary>A bare keyword such as <c>obj</c>, <c>R</c>, <c>true</c> or a content operator.</summary>
    Keyword,
    /// <summary><c>[</c>.</summary>
    ArrayStart,
    /// <summary><c>]</c>.</summary>
    ArrayEnd,
    /// <summary><c>&lt;&lt;</c>.</summary>
    DictStart,
    /// <summary><c>&gt;&gt;</c>.</summary>
    DictEnd,
}

/// <summary>One lexical token.</summary>
internal readonly record struct PdfToken(PdfTokenKind Kind, string Text = "", long Integer = 0, double Real = 0, long Start = 0);

/// <summary>
/// Splits PDF syntax (file objects and content streams alike) into tokens, over a byte buffer. A string keeps its
/// bytes as one char each so a font code or an encrypted payload survives untouched.
/// </summary>
internal sealed class PdfLexer
{
    readonly byte[] _data;
    readonly int _end;

    /// <summary>Creates a lexer over <paramref name="data"/> starting at <paramref name="position"/>.</summary>
    public PdfLexer(byte[] data, int position = 0, int? end = null)
    {
        _data = data;
        Position = position;
        _end = end ?? data.Length;
    }

    /// <summary>The offset of the next unread byte.</summary>
    public int Position { get; set; }

    /// <summary>The data being read.</summary>
    public byte[] Data => _data;

    /// <summary>The offset one past the last readable byte.</summary>
    public int End => _end;

    /// <summary>Whether <paramref name="b"/> is PDF white space (ISO 32000-1, 7.2.2).</summary>
    public static bool IsWhite(int b) => b is 0 or 9 or 10 or 12 or 13 or 32;

    /// <summary>Whether <paramref name="b"/> is a PDF delimiter.</summary>
    public static bool IsDelimiter(int b) => b is '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '/' or '%';

    /// <summary>Skips white space and comments.</summary>
    public void SkipWhite()
    {
        while (Position < _end)
        {
            int b = _data[Position];
            if (IsWhite(b)) Position++;
            else if (b == '%')
            {
                while (Position < _end && _data[Position] != 10 && _data[Position] != 13) Position++;
            }
            else break;
        }
    }

    /// <summary>Reads the next token.</summary>
    public PdfToken Next()
    {
        while (true)
        {
            SkipWhite();
            if (Position >= _end) return new PdfToken(PdfTokenKind.Eof, Start: Position);
            long start = Position;
            int b = _data[Position];
            switch (b)
            {
                case '[': Position++; return new PdfToken(PdfTokenKind.ArrayStart, Start: start);
                case ']': Position++; return new PdfToken(PdfTokenKind.ArrayEnd, Start: start);
                case '{': Position++; return new PdfToken(PdfTokenKind.Keyword, "{", Start: start);
                case '}': Position++; return new PdfToken(PdfTokenKind.Keyword, "}", Start: start);
                case '(': return ReadLiteralString(start);
                case '/': return ReadName(start);
                case '<':
                    if (Position + 1 < _end && _data[Position + 1] == '<') { Position += 2; return new PdfToken(PdfTokenKind.DictStart, Start: start); }
                    return ReadHexString(start);
                case '>':
                    if (Position + 1 < _end && _data[Position + 1] == '>') { Position += 2; return new PdfToken(PdfTokenKind.DictEnd, Start: start); }
                    Position++; // a stray '>' is skipped
                    continue;
                case ')':
                    Position++; // a stray ')' is skipped
                    continue;
            }
            var t = ReadRegular(start);
            if (t.Kind == PdfTokenKind.Eof) continue;
            return t;
        }
    }

    /// <summary>Reads the next token without consuming it.</summary>
    public PdfToken Peek()
    {
        int save = Position;
        var t = Next();
        Position = save;
        return t;
    }

    PdfToken ReadRegular(long start)
    {
        int s = Position;
        while (Position < _end && !IsWhite(_data[Position]) && !IsDelimiter(_data[Position])) Position++;
        if (Position == s) { Position++; return new PdfToken(PdfTokenKind.Eof, Start: start); }
        string text = Encoding.Latin1.GetString(_data, s, Position - s);
        if (TryParseNumber(text, out long i, out double d, out bool isInt))
            return isInt ? new PdfToken(PdfTokenKind.Integer, text, i, i, start) : new PdfToken(PdfTokenKind.Real, text, (long)d, d, start);
        return new PdfToken(PdfTokenKind.Keyword, text, Start: start);
    }

    static bool TryParseNumber(string t, out long i, out double d, out bool isInt)
    {
        i = 0; d = 0; isInt = false;
        int p = 0;
        bool neg = false;
        // Tolerate "--5" and "+5" style damage the way real readers do.
        while (p < t.Length && (t[p] == '+' || t[p] == '-')) { if (t[p] == '-') neg = true; p++; }
        if (p >= t.Length) return false;
        bool dot = false, any = false;
        double val = 0, scale = 1;
        long ival = 0;
        for (; p < t.Length; p++)
        {
            char c = t[p];
            if (c >= '0' && c <= '9')
            {
                any = true;
                if (dot) { scale /= 10; val += (c - '0') * scale; }
                else { val = val * 10 + (c - '0'); ival = unchecked(ival * 10 + (c - '0')); }
            }
            else if (c == '.' && !dot) dot = true;
            else if (c == '-' && any) break; // "12-3": keep the leading number
            else return false;
        }
        if (!any) return false;
        isInt = !dot;
        i = neg ? -ival : ival;
        d = neg ? -val : val;
        return true;
    }

    PdfToken ReadName(long start)
    {
        Position++;
        var sb = new StringBuilder();
        while (Position < _end)
        {
            int b = _data[Position];
            if (IsWhite(b) || IsDelimiter(b)) break;
            if (b == '#' && Position + 2 < _end && HexVal(_data[Position + 1]) >= 0 && HexVal(_data[Position + 2]) >= 0)
            {
                sb.Append((char)(HexVal(_data[Position + 1]) * 16 + HexVal(_data[Position + 2])));
                Position += 3;
            }
            else { sb.Append((char)b); Position++; }
        }
        return new PdfToken(PdfTokenKind.Name, sb.ToString(), Start: start);
    }

    PdfToken ReadLiteralString(long start)
    {
        Position++;
        var sb = new StringBuilder();
        int depth = 1;
        while (Position < _end)
        {
            int b = _data[Position++];
            if (b == '\\')
            {
                if (Position >= _end) break;
                int e = _data[Position++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 13: if (Position < _end && _data[Position] == 10) Position++; break;
                    case 10: break;
                    default:
                        if (e >= '0' && e <= '7')
                        {
                            int v = e - '0';
                            for (int k = 0; k < 2 && Position < _end && _data[Position] >= '0' && _data[Position] <= '7'; k++)
                                v = v * 8 + (_data[Position++] - '0');
                            sb.Append((char)(v & 0xFF));
                        }
                        else sb.Append((char)e);
                        break;
                }
            }
            else if (b == '(') { depth++; sb.Append('('); }
            else if (b == ')') { if (--depth == 0) break; sb.Append(')'); }
            else if (b == 13)
            {
                // An unescaped end-of-line inside a string reads as a single LF.
                if (Position < _end && _data[Position] == 10) Position++;
                sb.Append('\n');
            }
            else sb.Append((char)b);
        }
        return new PdfToken(PdfTokenKind.String, sb.ToString(), Start: start);
    }

    PdfToken ReadHexString(long start)
    {
        Position++;
        var sb = new StringBuilder();
        int hi = -1;
        while (Position < _end)
        {
            int b = _data[Position++];
            if (b == '>') break;
            int v = HexVal(b);
            if (v < 0) continue;
            if (hi < 0) hi = v;
            else { sb.Append((char)(hi * 16 + v)); hi = -1; }
        }
        if (hi >= 0) sb.Append((char)(hi * 16));
        return new PdfToken(PdfTokenKind.String, sb.ToString(), Start: start);
    }

    static int HexVal(int b) => b switch
    {
        >= '0' and <= '9' => b - '0',
        >= 'a' and <= 'f' => b - 'a' + 10,
        >= 'A' and <= 'F' => b - 'A' + 10,
        _ => -1,
    };
}
