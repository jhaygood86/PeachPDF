// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachDrawing.Core;
using System.Numerics;
using PeachDrawing.PDF.Engine.Drawing;
using System;
using System.Globalization;
using System.Text;

namespace PeachDrawing.PDF.Engine.Internal;

/// <summary>
/// Contains all used encodings for PDF.
/// </summary>
internal static class PdfEncoders
{
    /// <summary>
    /// Gets the encoding in which every character is the byte of the same value.
    /// </summary>
    public static Encoding LatinOneByteEncoding { get; } = new LatinOneByteEncoding();

    /// <summary>
    /// Gets the UTF-16 big-endian encoding without a byte order mark.
    /// </summary>
    public static Encoding RawBigEndianUnicodeEncoding { get; } = new RawBigEndianUnicodeEncoding();

    /// <summary>
    /// Gets the Windows ANSI (code page 1252) encoding.
    /// </summary>
    public static Encoding WinAnsiEncoding { get; } = new WindowsAnsiEncoding();

    /// <summary>
    /// Gets the PDFDocEncoding encoding.
    /// </summary>
    public static Encoding PdfDocumentEncoding { get; } = new PdfDocumentEncoding();

    /// <summary>
    /// Gets the Unicode (UTF-16 little-endian) encoding.
    /// </summary>
    public static Encoding UnicodeEncoding => Encoding.Unicode;

    private static byte[] GetBytes(string text, PdfStringEncoding encoding) =>
        encoding switch
        {
            PdfStringEncoding.LatinOneByteEncoding => LatinOneByteEncoding.GetBytes(text),
            PdfStringEncoding.WinAnsiEncoding => WinAnsiEncoding.GetBytes(text),
            PdfStringEncoding.PDFDocEncoding => PdfDocumentEncoding.GetBytes(text),
            PdfStringEncoding.Unicode => RawBigEndianUnicodeEncoding.GetBytes(text),
            _ => throw new NotImplementedException(encoding.ToString()),
        };

    private static string LatinOneByteString(byte[] bytes) => LatinOneByteEncoding.GetString(bytes, 0, bytes.Length);

    /// <summary>
    /// Converts a string into a string literal.
    /// </summary>
    public static string ToStringLiteral(string text, PdfStringEncoding encoding)
    {
        if (string.IsNullOrEmpty(text))
            return "()";

        return LatinOneByteString(FormatStringLiteral(GetBytes(text, encoding), encoding == PdfStringEncoding.Unicode, true, false));
    }

    /// <summary>
    /// Converts a byte array into a string literal.
    /// </summary>
    public static string ToStringLiteral(byte[]? bytes, bool unicode)
    {
        if (bytes is null || bytes.Length == 0)
            return "()";

        return LatinOneByteString(FormatStringLiteral(bytes, unicode, true, false));
    }

    /// <summary>
    /// Converts a string into a hexadecimal string literal.
    /// </summary>
    public static string ToHexStringLiteral(string text, PdfStringEncoding encoding)
    {
        if (string.IsNullOrEmpty(text))
            return "<>";

        return LatinOneByteString(FormatStringLiteral(GetBytes(text, encoding), encoding == PdfStringEncoding.Unicode, true, true));
    }

    /// <summary>
    /// Converts a byte array into a hexadecimal string literal.
    /// </summary>
    public static string ToHexStringLiteral(byte[]? bytes, bool unicode)
    {
        if (bytes is null || bytes.Length == 0)
            return "<>";

        return LatinOneByteString(FormatStringLiteral(bytes, unicode, true, true));
    }

    /// <summary>
    /// Converts the bytes of a string into the bytes of a PDF string literal: a parenthesized literal with the
    /// necessary escapes, or a hexadecimal one. Unicode strings are always written as hexadecimal literals.
    /// </summary>
    public static byte[] FormatStringLiteral(byte[]? bytes, bool unicode, bool prefix, bool hex)
    {
        if (bytes is null || bytes.Length == 0)
            return hex ? "<>"u8.ToArray() : "()"u8.ToArray();

        System.Diagnostics.Debug.Assert(!unicode || bytes.Length % 2 == 0, "Odd number of bytes in Unicode string.");

        if (unicode)
            return FormatUnicodeHexLiteral(bytes, prefix);

        return hex ? FormatHexLiteral(bytes) : FormatParenthesizedLiteral(bytes);
    }

    private static ReadOnlySpan<byte> HexDigits => "0123456789ABCDEF"u8;

    /// <summary>
    /// The text of <see cref="FormatStringLiteral"/> (unicode, no byte-order mark, hex) for a string whose UTF-16 units are the
    /// glyph indices of <paramref name="glyphs"/> - <c>&lt;</c>, four hex digits a glyph with a line break after every 24th unit
    /// but the first, <c>&gt;</c> - without the intermediate string and byte arrays.
    /// </summary>
    internal static string FormatGlyphIdsAsHexLiteral(System.Collections.Generic.IReadOnlyList<PeachDrawing.Text.Shaping.PlacedGlyph> glyphs)
    {
        if (glyphs.Count == 0) return "<>";

        return string.Create(GlyphIdsHexLiteralLength(glyphs.Count), glyphs, static (output, source) => WriteGlyphIdsAsHexLiteral(source, output));
    }

    /// <summary>The number of characters <see cref="WriteGlyphIdsAsHexLiteral"/> writes for <paramref name="glyphCount"/> glyphs.</summary>
    internal static int GlyphIdsHexLiteralLength(int glyphCount) =>
        // One unit is two bytes; the same break rule as FormatUnicodeHexLiteral (every 48th byte, except the first).
        glyphCount == 0 ? 2 : glyphCount * 4 + (glyphCount * 2 - 1) / 48 + 2;

    /// <summary>
    /// Writes the literal <see cref="FormatGlyphIdsAsHexLiteral"/> returns into <paramref name="output"/>, which must hold
    /// <see cref="GlyphIdsHexLiteralLength"/> characters, so a caller that appends it straight to a content stream allocates no string.
    /// </summary>
    internal static void WriteGlyphIdsAsHexLiteral(System.Collections.Generic.IReadOnlyList<PeachDrawing.Text.Shaping.PlacedGlyph> source, Span<char> output)
    {
        ReadOnlySpan<byte> digits = HexDigits;
        int position = 0;
        output[position++] = '<';

        for (int unit = 0; unit < source.Count; unit++)
        {
            int id = (char)source[unit].GlyphIndex;
            output[position] = (char)digits[(id >> 12) & 0xF];
            output[position + 1] = (char)digits[(id >> 8) & 0xF];
            output[position + 2] = (char)digits[(id >> 4) & 0xF];
            output[position + 3] = (char)digits[id & 0xF];
            position += 4;

            int byteIndex = unit * 2;
            if (byteIndex != 0 && byteIndex % 48 == 0)
                output[position++] = '\n';
        }

        output[position] = '>';
    }

    private static byte[] FormatHexLiteral(byte[] bytes)
    {
        byte[] result = new byte[bytes.Length * 2 + 2];
        result[0] = (byte)'<';
        WriteHex(bytes, result.AsSpan(1));
        result[^1] = (byte)'>';
        return result;
    }

    private static void WriteHex(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        ReadOnlySpan<byte> digits = HexDigits;
        for (int i = 0; i < source.Length; i++)
        {
            destination[2 * i] = digits[source[i] >> 4];
            destination[2 * i + 1] = digits[source[i] & 0xF];
        }
    }

    private static byte[] FormatUnicodeHexLiteral(byte[] bytes, bool prefix)
    {
        // A line break follows every 24th UTF-16 unit, except the first.
        int lineBreaks = (bytes.Length - 1) / 48;
        int head = prefix ? 5 : 1;
        byte[] result = new byte[head + bytes.Length * 2 + lineBreaks + 1];
        Span<byte> output = result;
        (prefix ? "<FEFF"u8 : "<"u8).CopyTo(output);
        int position = head;
        for (int index = 0; index < bytes.Length; index += 2)
        {
            WriteHex(bytes.AsSpan(index, 2), output[position..]);
            position += 4;
            if (index != 0 && index % 48 == 0)
                output[position++] = (byte)'\n';
        }
        output[position] = (byte)'>';
        return result;
    }

    private static byte[] FormatParenthesizedLiteral(byte[] bytes)
    {
        int length = 2;
        foreach (byte value in bytes)
            length += NeedsEscape(value) ? 2 : 1;

        byte[] result = new byte[length];
        Span<byte> output = result;
        output[0] = (byte)'(';
        int position = 1;
        foreach (byte value in bytes)
        {
            if (NeedsEscape(value))
            {
                output[position++] = (byte)'\\';
                output[position++] = value switch
                {
                    (byte)'\n' => (byte)'n',
                    (byte)'\r' => (byte)'r',
                    (byte)'\t' => (byte)'t',
                    (byte)'\b' => (byte)'b',
                    _ => value,
                };
            }
            else
            {
                output[position++] = value;
            }
        }
        output[position] = (byte)')';
        return result;
    }

    // Control characters other than the four with a short escape stay as they are: the string may be encrypted.
    private static bool NeedsEscape(byte value) =>
        value is (byte)'\n' or (byte)'\r' or (byte)'\t' or (byte)'\b' or (byte)'(' or (byte)')' or (byte)'\\';

    /// <summary>
    /// Formats text with the invariant culture.
    /// </summary>
    public static string Format(string format, params object[] args)
    {
        return string.Format(CultureInfo.InvariantCulture, format, args);
    }

    /// <summary>
    /// Converts a double to a string with at most three decimals.
    /// </summary>
    public static string ToString(double val)
    {
        return val.ToString(PdfNumberFormats.ThreeDecimals, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Converts a color into the operands of a PDF color operator.
    /// </summary>
    public static string ToString(PaintColor color, PdfColorMode colorMode, bool withAlpha = false)
    {
        // If not defined let color decide
        if (colorMode == PdfColorMode.Undefined)
            colorMode = color.IsCmyk ? PdfColorMode.Cmyk : PdfColorMode.Rgb;

        if (colorMode == PdfColorMode.Cmyk)
        {
            var (cyan, magenta, yellow, black) = color.ToCmyk();
            return JoinNumbers(PdfNumberFormats.ThreeDecimals, [cyan, magenta, yellow, black]);
        }

        var (red, green, blue) = color.ToRgb();
        string rgb = JoinNumbers(PdfNumberFormats.ThreeDecimals, [red / 255.0, green / 255.0, blue / 255.0]);

        // The opacity is a single-precision value, and rounds at three decimals as such.
        return withAlpha ? rgb + " " + color.Opacity().ToString(PdfNumberFormats.ThreeDecimals, CultureInfo.InvariantCulture) : rgb;
    }

    /// <summary>
    /// Converts a matrix into the six operands of a PDF matrix.
    /// </summary>
    public static string ToString(Matrix3x2 matrix)
    {
        // The matrix elements are single-precision and round at four decimals as such; widening them first would
        // change the last digit of some values.
        return JoinNumbers(PdfNumberFormats.FourDecimals,
            [matrix.M11, matrix.M12, matrix.M21, matrix.M22, matrix.M31, matrix.M32]);
    }

    /// <summary>
    /// Formats the numbers with the invariant culture, separated by single spaces.
    /// </summary>
    private static string JoinNumbers<T>(string format, ReadOnlySpan<T> values) where T : ISpanFormattable
    {
        Span<char> buffer = stackalloc char[values.Length * 32];
        int length = 0;
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0)
                buffer[length++] = ' ';
            if (!values[i].TryFormat(buffer[length..], out int written, format, CultureInfo.InvariantCulture))
            {
                // Only a huge magnitude needs more than the reserved room.
                var texts = new string[values.Length];
                for (int j = 0; j < texts.Length; j++)
                    texts[j] = values[j].ToString(format, CultureInfo.InvariantCulture);
                return string.Join(' ', texts);
            }
            length += written;
        }
        return new string(buffer[..length]);
    }
}
