// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachDrawing.PDF.Engine.Filters;
using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text;

namespace PeachDrawing.PDF.Engine.Advanced;

/// <summary>
/// Represents a ToUnicode map for composite font.
/// </summary>
internal sealed class PdfToUnicodeMap : PdfDictionary
{
    public PdfToUnicodeMap(PdfDocument document)
        : base(document)
    { }

    public PdfToUnicodeMap(PdfDocument document, CharacterMapInfo characterMapInfo)
        : base(document)
    {
        _characterMapInfo = characterMapInfo;
    }

    /// <summary>
    /// Gets or sets the CMap info.
    /// </summary>
    public CharacterMapInfo CharacterMapInfo
    {
        get => _characterMapInfo;
        set => _characterMapInfo = value;
    }
    CharacterMapInfo _characterMapInfo = null!;

    /// <summary>
    /// Creates the ToUnicode map from the CharacterMapInfo.
    /// </summary>
    internal override void PrepareForSave()
    {
        base.PrepareForSave();

        // This code comes literally from PDF Reference
        string prefix =
          "/CIDInit /ProcSet findresource begin\n" +
          "12 dict begin\n" +
          "begincmap\n" +
          "/CIDSystemInfo << /Registry (Adobe)/Ordering (UCS)/Supplement 0>> def\n" +
          "/CMapName /Adobe-Identity-UCS def /CMapType 2 def\n";
        string suffix = "endcmap CMapName currentdict /CMap defineresource pop end end";

        // Glyph index -> its source text. Most entries are a single character (from
        // CharacterToGlyphIndex, keyed by Unicode scalar value); a GSUB ligature glyph (from
        // LigatureGlyphToText, populated by CharacterMapInfo.AddShapedText) has no single scalar value
        // to key on, so its multi-character source text is carried directly instead.
        Dictionary<int, string> glyphIndexToText = new Dictionary<int, string>();
        int lowIndex = 65536, hiIndex = -1;
        foreach (KeyValuePair<int, int> entry in _characterMapInfo.CharacterToGlyphIndex)
        {
            int index = entry.Value;         // glyph index
            lowIndex = Math.Min(lowIndex, index);
            hiIndex = Math.Max(hiIndex, index);
            glyphIndexToText[index] = char.ConvertFromUtf32(entry.Key);
        }
        foreach (KeyValuePair<int, string> entry in _characterMapInfo.LigatureGlyphToText)
        {
            int index = entry.Key;
            lowIndex = Math.Min(lowIndex, index);
            hiIndex = Math.Max(hiIndex, index);
            glyphIndexToText[index] = entry.Value;
        }

        // Lines end with a plain line feed on every platform, so the same document gives the same bytes everywhere.
        var cmap = new StringBuilder(prefix);
        cmap.Append("1 begincodespacerange\n");
        cmap.Append($"<{lowIndex:X4}><{hiIndex:X4}>\n");
        cmap.Append("endcodespacerange\n");

        // Sorting seems not necessary. The limit is 100 entries, we will see.
        cmap.Append(glyphIndexToText.Count).Append(" beginbfrange\n");
        foreach (KeyValuePair<int, string> entry in glyphIndexToText)
        {
            AppendHex4(cmap.Append('<'), entry.Key).Append("><");
            AppendHex4(cmap, entry.Key).Append("><");
            AppendUtf16BigEndianHex(cmap, entry.Value).Append(">\n");
        }
        cmap.Append("endbfrange\n");
        cmap.Append(suffix);

        // The stream starts with a UTF-8 byte order mark, as it always has.
        // The text is all ASCII, so it is encoded chunk by chunk straight into the one buffer.
        ReadOnlySpan<byte> preamble = Encoding.UTF8.GetPreamble();
        byte[] bytes = new byte[preamble.Length + cmap.Length];
        preamble.CopyTo(bytes);
        int written = preamble.Length;
        foreach (ReadOnlyMemory<char> chunk in cmap.GetChunks())
            written += Encoding.UTF8.GetBytes(chunk.Span, bytes.AsSpan(written));
        // Compress like content streams
        if (Owner.Options.CompressContentStreams)
        {
            bytes = StreamCompression.Encode(_document.Options, bytes, out var filterName);
            Elements.SetName("/Filter", filterName);
        }
        //PdfStream stream = CreateStream(bytes);
        else
        {
            Elements.Remove("/Filter");
        }

        if (Stream is null)
            CreateStream(bytes);
        else
        {
            Stream.Value = bytes;
            Elements.SetInteger(PdfStream.Keys.Length, Stream.Length);
        }
    }

    /// <summary>
    /// Appends <paramref name="text"/> as the UTF-16 (big-endian) hex string a ToUnicode
    /// <c>bfrange</c> destination expects: a single BMP character yields 4 hex digits, an
    /// astral character its 8-hex surrogate pair, and a multi-character string (a GSUB
    /// ligature's source text) the concatenation of all of them - PDF's bfrange destination
    /// syntax accepts a multi-code-unit string for exactly this "one glyph, several characters"
    /// case.
    /// </summary>
    internal static StringBuilder AppendUtf16BigEndianHex(StringBuilder builder, string text)
    {
        foreach (char c in text)
            AppendHex4(builder, c);
        return builder;
    }

    /// <summary>Appends <paramref name="value"/> as at least four uppercase hex digits (the <c>X4</c> format).</summary>
    private static StringBuilder AppendHex4(StringBuilder builder, int value)
    {
        const string digits = "0123456789ABCDEF";
        if (value is >= 0 and <= 0xFFFF)
        {
            Span<char> buffer = stackalloc char[4];
            buffer[0] = digits[(value >> 12) & 0xF];
            buffer[1] = digits[(value >> 8) & 0xF];
            buffer[2] = digits[(value >> 4) & 0xF];
            buffer[3] = digits[value & 0xF];
            return builder.Append(buffer);
        }

        Span<char> wide = stackalloc char[16];
        value.TryFormat(wide, out int count, "X4", CultureInfo.InvariantCulture);
        return builder.Append(wide[..count]);
    }

    internal sealed class Keys : PdfStream.Keys
    {
        // No new keys.
    }
}
