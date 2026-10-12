using System.Globalization;
using System.Numerics;
using System.Text;
using PeachDrawing.Core;
using PeachPDF.PDF;
using PeachPDF.PDF.Internal;

namespace PeachPDF.Tests.PdfEngineTests.Encodings;

public class PdfStringLiteralTests
{
    /// <summary>The string-building algorithm the span-based code replaced, kept as the oracle.</summary>
    private static byte[] ReferenceFormat(byte[] bytes, bool unicode, bool prefix, bool hex)
    {
        if (bytes.Length == 0)
            return hex ? "<>"u8.ToArray() : "()"u8.ToArray();

        var pdf = new StringBuilder();
        if (!unicode)
        {
            if (!hex)
            {
                pdf.Append('(');
                foreach (byte value in bytes)
                {
                    char ch = (char)value;
                    if (ch < 32)
                    {
                        switch (ch)
                        {
                            case '\n': pdf.Append("\\n"); break;
                            case '\r': pdf.Append("\\r"); break;
                            case '\t': pdf.Append("\\t"); break;
                            case '\b': pdf.Append("\\b"); break;
                            default: pdf.Append(ch); break;
                        }
                    }
                    else
                    {
                        switch (ch)
                        {
                            case '(': pdf.Append("\\("); break;
                            case ')': pdf.Append("\\)"); break;
                            case '\\': pdf.Append("\\\\"); break;
                            default: pdf.Append(ch); break;
                        }
                    }
                }
                pdf.Append(')');
            }
            else
            {
                pdf.Append('<');
                foreach (byte value in bytes)
                    pdf.Append(value.ToString("X2", CultureInfo.InvariantCulture));
                pdf.Append('>');
            }
        }
        else
        {
            pdf.Append(prefix ? "<FEFF" : "<");
            for (int index = 0; index < bytes.Length; index += 2)
            {
                pdf.Append(bytes[index].ToString("X2", CultureInfo.InvariantCulture));
                pdf.Append(bytes[index + 1].ToString("X2", CultureInfo.InvariantCulture));
                if (index != 0 && index % 48 == 0)
                    pdf.Append('\n');
            }
            pdf.Append('>');
        }

        return Encoding.Latin1.GetBytes(pdf.ToString());
    }

    public static TheoryData<int> Lengths() => new() { 1, 2, 3, 4, 7, 8, 23, 24, 25, 46, 47, 48, 49, 50, 95, 96, 97, 98, 255, 256, 1000 };

    [Theory]
    [MemberData(nameof(Lengths))]
    public void FormatStringLiteral_MatchesReference_ForEveryMode(int length)
    {
        var random = new Random(length);
        byte[] bytes = new byte[length];
        random.NextBytes(bytes);
        // Make sure the characters that need escaping are present.
        ReadOnlySpan<byte> special = "\n\r\t\b()\\\f"u8;
        for (int i = 0; i < Math.Min(length, special.Length); i++)
            bytes[i] = special[i];

        foreach (bool hex in new[] { false, true })
        {
            Assert.Equal(ReferenceFormat(bytes, false, true, hex), PdfEncoders.FormatStringLiteral(bytes, false, true, hex));

            if (length % 2 == 0)
            {
                foreach (bool prefix in new[] { false, true })
                    Assert.Equal(ReferenceFormat(bytes, true, prefix, hex), PdfEncoders.FormatStringLiteral(bytes, true, prefix, hex));
            }
        }
    }

    [Fact]
    public void FormatStringLiteral_EmptyInput_GivesEmptyLiteral()
    {
        Assert.Equal("()"u8.ToArray(), PdfEncoders.FormatStringLiteral([], false, false, false));
        Assert.Equal("<>"u8.ToArray(), PdfEncoders.FormatStringLiteral(null, true, true, true));
        Assert.Equal("()", PdfEncoders.ToStringLiteral((byte[]?)null, false));
        Assert.Equal("<>", PdfEncoders.ToHexStringLiteral("", PdfStringEncoding.WinAnsiEncoding));
    }

    [Fact]
    public void ToStringLiteral_UnsupportedEncoding_Throws()
    {
        Assert.Throws<NotImplementedException>(() => PdfEncoders.ToStringLiteral("x", (PdfStringEncoding)12345));
    }

    [Fact]
    public void LatinOneByteEncoding_RoundTripsEveryByte_AndTruncatesWideCharacters()
    {
        Encoding latin = PdfEncoders.LatinOneByteEncoding;
        byte[] all = new byte[256];
        for (int i = 0; i < all.Length; i++)
            all[i] = (byte)i;

        string text = latin.GetString(all, 0, all.Length);
        Assert.Equal(256, text.Length);
        for (int i = 0; i < 256; i++)
            Assert.Equal((char)i, text[i]);

        Assert.Equal(all, latin.GetBytes(text));
        Assert.Equal(256, latin.GetByteCount(text));
        Assert.Equal(256, latin.GetCharCount(all));
        Assert.Equal(new byte[] { 0x41, 0x23 }, latin.GetBytes("Aģ"));

        char[] chars = new char[256];
        Assert.Equal(256, latin.GetChars(all, chars));
        Assert.Equal(text, new string(chars));
        Assert.Throws<ArgumentException>(() => latin.GetBytes("abc".AsSpan(), new byte[2]));
        Assert.Throws<ArgumentException>(() => latin.GetChars(all, new char[2]));
    }

    [Fact]
    public void ColorAndMatrixText_UsesInvariantRoundingOfTheOriginalFloatValues()
    {
        // 36.31805f is stored as 36.3180503...; widened to a double it would print 36.318, but the float prints 36.3181.
        var matrix = new Matrix3x2(36.31805f, 0f, 0f, 10.8f, 208.8979f, 282.6203f);
        Assert.Equal("36.3181 0 0 10.8 208.8979 282.6203", PdfEncoders.ToString(matrix));

        Assert.Equal("1 0 0", PdfEncoders.ToString(PaintColor.FromArgb(255, 255, 0, 0), PdfColorMode.Rgb));
        Assert.Equal("1 0 0 0.502", PdfEncoders.ToString(PaintColor.FromArgb(128, 255, 0, 0), PdfColorMode.Rgb, withAlpha: true));
        Assert.Equal("0 1 1 0", PdfEncoders.ToString(PaintColor.FromArgb(255, 255, 0, 0), PdfColorMode.Cmyk));
    }

    [Fact]
    public void ColorAndMatrixText_HugeValueFallsBackToFullFormatting()
    {
        var matrix = new Matrix3x2(float.MaxValue, 0f, 0f, 1f, 0f, 0f);
        string text = PdfEncoders.ToString(matrix);
        Assert.StartsWith("340282", text);
        Assert.EndsWith(" 0 0 1 0 0", text);
    }
}
