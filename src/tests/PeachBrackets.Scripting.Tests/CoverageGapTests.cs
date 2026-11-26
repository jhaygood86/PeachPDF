using System.Text;
using PeachBrackets.Scripting.Internal;
using PeachBrackets.Scripting.Internal.Idna;

namespace PeachBrackets.Scripting.Tests;

/// <summary>The text decoders behind TextDecoder, the domain mapping and the reflected number kinds, exercised directly.</summary>
public class CoverageGapTests
{
    private static string? Decode(string encoding, bool fatal, params byte[][] chunks)
    {
        var id = TextEncodings.Create(encoding, fatal);
        Assert.True(id > 0, encoding);
        var text = new StringBuilder();
        for (var i = 0; i < chunks.Length; i++)
        {
            var part = TextEncodings.Run(id, chunks[i], i == chunks.Length - 1);
            if (part is null)
                return null;
            text.Append(part);
        }

        return text.ToString();
    }

    [Fact]
    public void AnUnknownEncoding_AndAnUnknownDecoder_AreRefused()
    {
        Assert.Equal(-1, TextEncodings.Create("iso-8859-10", false));
        Assert.Null(TextEncodings.Run(int.MaxValue, [1], true));
    }

    [Fact]
    public void Utf16_JoinsUnitsAcrossChunks_AndReplacesWhatIsBroken()
    {
        Assert.Equal("a😀", Decode("utf-16le", false, [0x61], [0x00, 0x3D, 0xD8], [0x00, 0xDE]));
        Assert.Equal("a😀", Decode("utf-16be", false, [0x00, 0x61, 0xD8, 0x3D, 0xDE, 0x00]));
        Assert.Equal("�a", Decode("utf-16le", false, [0x00, 0xDC, 0x61, 0x00]));
        Assert.Equal("�a", Decode("utf-16le", false, [0x3D, 0xD8, 0x61, 0x00]));
        Assert.Equal("a�", Decode("utf-16le", false, [0x61, 0x00, 0x62]));
        Assert.Equal("�", Decode("utf-16le", false, [0x3D, 0xD8]));
        Assert.Null(Decode("utf-16le", true, [0x00, 0xDC]));
        Assert.Null(Decode("utf-16le", true, [0x3D, 0xD8, 0x61, 0x00]));
        Assert.Null(Decode("utf-16le", true, [0x61]));
        Assert.Null(Decode("utf-16le", true, [0x3D, 0xD8]));
    }

    [Fact]
    public void SingleByteEncodings_MapTheirBytes_AndTreatTheUndefinedOnesAsErrors()
    {
        Assert.Equal("€é", Decode("windows-1252", false, [0x80, 0xE9]));
        Assert.Equal("\u0080", Decode("iso-8859-2", false, [0x80]));
        Assert.Equal("�", Decode("iso-8859-3", false, [0xA5]));
        Assert.Null(Decode("iso-8859-3", true, [0xA5]));
        Assert.Equal("‘’€₯ͺ", Decode("iso-8859-7", false, [0xA1, 0xA2, 0xA4, 0xA5, 0xAA]));
        Assert.Equal("Ω", Decode("macintosh", false, [0xBD]));
        Assert.Equal("Ґ", Decode("x-mac-cyrillic", false, [0xA2]));
        Assert.Equal("Ц", Decode("koi8-r", false, [0xE3]));
    }

    [Fact]
    public void XUserDefined_MapsHighBytesToThePrivateUseArea()
    {
        Assert.Equal("a", Decode("x-user-defined", false, [0x61, 0x80, 0xFF]));
    }

    [Fact]
    public void MultiByteEncodings_DecodeThroughTheRuntime_AndCanBeFatal()
    {
        Assert.Equal("日本", Decode("shift_jis", false, [0x93, 0xFA], [0x96, 0x7B]));
        Assert.Equal("中", Decode("gbk", false, [0xD6, 0xD0]));
        Assert.Equal("한", Decode("euc-kr", false, [0xC7, 0xD1]));
        Assert.NotNull(Decode("big5", false, [0xFF]));
        _ = Decode("shift_jis", true, [0xFF, 0xFF]);
    }

    [Theory]
    [InlineData("a..b")]
    [InlineData("-a.com")]
    [InlineData("a-.com")]
    [InlineData("ab--c.com")]
    [InlineData("xn--ls8h=")]
    [InlineData("a b")]
    [InlineData("a/b")]
    [InlineData("͸")]
    [InlineData("ᄀ‍")]
    [InlineData("a‍b")]
    [InlineData("١٢a")]
    [InlineData("xn--a.b")]
    public void MoreInvalidDomains_AreErrorsOrMappedWithoutThrowing(string input)
    {
        var exception = Record.Exception(() => Uts46.ToAscii(input));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("日本語.jp", "xn--wgv71a119e.jp")]
    [InlineData("ＡＢＣ.com", "abc.com")]
    [InlineData("a­b.com", "ab.com")]
    [InlineData("münchen.de", "xn--mnchen-3ya.de")]
    [InlineData("1.2.3.4", "1.2.3.4")]
    public void MoreDomains_AreMappedToAscii(string input, string expected) => Assert.Equal(expected, Uts46.ToAscii(input));
}
