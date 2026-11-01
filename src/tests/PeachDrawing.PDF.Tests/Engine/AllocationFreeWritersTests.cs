using System.Globalization;
using System.Text;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.Advanced;
using PeachDrawing.PDF.Engine.Filters;
using PeachDrawing.PDF.Engine.Internal;
using PeachDrawing.PDF.Engine.Rendering;
using PeachDrawing.PDF.Import;

namespace PeachPDF.Tests.PdfEngineTests;

/// <summary>The allocation-free writers produce exactly the bytes the string/array chains they replaced did.</summary>
public class AllocationFreeWritersTests
{
    [Fact]
    public void GlyphHexCodeUnit_MatchesTheOldEncoderChain_ForEveryUnit()
    {
        Span<char> unit = stackalloc char[4];
        for (int glyph = 0; glyph <= char.MaxValue; glyph++)
        {
            byte[] bytes = PdfEncoders.RawBigEndianUnicodeEncoding.GetBytes(((char)glyph).ToString());
            bytes = PdfEncoders.FormatStringLiteral(bytes, true, false, true);
            string expected = PdfEncoders.LatinOneByteEncoding.GetString(bytes, 0, bytes.Length);

            PdfGraphicsRenderer.FormatHexCodeUnit((char)glyph, unit);

            Assert.Equal(expected, "<" + new string(unit) + ">");
        }
    }

    [Fact]
    public void ToUnicodeHex_MatchesTheFormatStringSpelling()
    {
        string text = "Aé\U0001F600fi";
        var expected = new StringBuilder();
        foreach (char c in text)
            expected.Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));

        Assert.Equal(expected.ToString(), PdfToUnicodeMap.AppendUtf16BigEndianHex(new StringBuilder(), text).ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(1000)]
    public void Crc32_MatchesTheBitwiseReference(int length)
    {
        var data = new byte[length];
        new Random(length).NextBytes(data);

        Assert.Equal(Reference(data), PdfImageXObject.Crc32(data));

        static uint Reference(byte[] bytes)
        {
            uint c = 0xFFFFFFFF;
            foreach (byte b in bytes)
            {
                c ^= b;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }
            return c ^ 0xFFFFFFFF;
        }
    }

    [Fact]
    public void Crc32_OfTheStandardCheckString()
    {
        Assert.Equal(0xCBF43926u, PdfImageXObject.Crc32("123456789"u8));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(100000)]
    public void FlateEncoder_RoundTrips(int length)
    {
        var data = new byte[length];
        for (int i = 0; i < length; i++) data[i] = (byte)(i % 7);

        byte[] encoded = PdfFlateEncoder.Encode(data);

        using var zlib = new System.IO.Compression.ZLibStream(new MemoryStream(encoded), System.IO.Compression.CompressionMode.Decompress);
        var decoded = new MemoryStream();
        zlib.CopyTo(decoded);
        Assert.Equal(data, decoded.ToArray());
    }
}
