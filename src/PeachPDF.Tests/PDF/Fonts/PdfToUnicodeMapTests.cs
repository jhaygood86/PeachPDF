using System.Text;
using PeachPDF.PDF;
using PeachPDF.PDF.Advanced;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.PdfEngineTests.Fonts;

public class PdfToUnicodeMapTests
{
    private static string BuildMap(bool compress, params string[] texts)
    {
        var document = new PdfDocument();
        document.Options.CompressContentStreams = compress;
        var characterMapInfo = new CharacterMapInfo(TypefaceFixtures.FromFile(BundledFonts.Ttf));
        foreach (string text in texts)
            characterMapInfo.AddChars(text);

        var map = new PdfToUnicodeMap(document, characterMapInfo);
        map.PrepareForSave();
        return Encoding.UTF8.GetString(map.Stream!.Value);
    }

    [Fact]
    public void Map_IsPlainUtf8_WithByteOrderMark_AndOnlyLineFeeds()
    {
        string cmap = BuildMap(compress: false, "AB");

        Assert.StartsWith("﻿/CIDInit /ProcSet findresource begin\n", cmap);
        Assert.DoesNotContain('\r', cmap);
        Assert.EndsWith("endcmap CMapName currentdict /CMap defineresource pop end end", cmap);
    }

    [Fact]
    public void Map_ListsOneBfRangeEntryPerGlyph_WithItsSourceTextAsUtf16Hex()
    {
        string cmap = BuildMap(compress: false, "A\U0001F600");

        Assert.Contains("1 begincodespacerange\n<", cmap);
        Assert.Contains("endcodespacerange\n2 beginbfrange\n<", cmap);
        // 'A' is one UTF-16 unit; the emoji needs a surrogate pair (8 hex digits).
        Assert.Contains("><0041>\n", cmap);
        Assert.Contains("><D83DDE00>\n", cmap);
        Assert.Contains("endbfrange\nendcmap", cmap);
    }
}
