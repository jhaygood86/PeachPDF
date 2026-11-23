using System.Text;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.Advanced;
using PeachDrawing.Text.Shaping;
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

    [Fact]
    public void ShapedArabic_NoGlyphGetsAnEmptyDestination()
    {
        // The bundled Arabic font draws a letter as a body glyph plus separate dot glyphs; the dots are
        // shaped from no characters of their own. They once got an empty destination ("<013D><013D><>"),
        // which is not valid ToUnicode and makes PdfPig throw reading the page.
        var document = new PdfDocument();
        document.Options.CompressContentStreams = false;
        var info = new CharacterMapInfo(TypefaceFixtures.FromFile(BundledFonts.Arabic));
        info.AddShapedText("\u0628\u064A\u062A", ShapeSettings.Default);

        var map = new PdfToUnicodeMap(document, info);
        map.PrepareForSave();
        string cmap = Encoding.UTF8.GetString(map.Stream!.Value);

        Assert.DoesNotContain("><>", cmap);
        // BEH, YEH and TEH can share one body glyph (they differ only in the dots), so the first drawn keeps the glyph's own code
        // and a later one gets a code of its own that draws the same glyph.
        Assert.Contains("\u0628", info.LigatureGlyphToText.Values);
        Assert.NotEmpty(info.VariantCidToText);
        foreach (var variant in info.VariantCidToText)
            Assert.Contains($"<{variant.Key:X4}><{variant.Key:X4}><{(int)variant.Value[0]:X4}>\n", cmap);
        // The component glyphs map to an invisible character rather than being left unmapped, which makes
        // readers extract their glyph code as text.
        Assert.Contains(CharacterMapInfo.NoText, info.LigatureGlyphToText.Values);
        Assert.Contains("<200B>\n", cmap);
    }

    [Fact]
    public void EmptySourceText_IsWrittenAsAnInvisibleCharacter()
    {
        var document = new PdfDocument();
        document.Options.CompressContentStreams = false;
        var info = new CharacterMapInfo(TypefaceFixtures.FromFile(BundledFonts.Ttf));
        info.AddShapedGlyph(5, "");

        var map = new PdfToUnicodeMap(document, info);
        map.PrepareForSave();
        string cmap = Encoding.UTF8.GetString(map.Stream!.Value);

        Assert.DoesNotContain("><>", cmap);
        Assert.Contains("><200B>\n", cmap);
    }
}
