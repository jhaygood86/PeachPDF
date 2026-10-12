// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachDrawing.Text.Shaping;

using System;
using System.Diagnostics;
using System.Text;

namespace PeachPDF.PDF.Advanced;

/// <summary>
/// Represents a PDF font.
/// </summary>
internal class PdfFont : PdfDictionary
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfFont"/> class.
    /// </summary>
    public PdfFont(PdfDocument document)
        : base(document)
    { }

    internal PdfFontDescriptor FontDescriptor
    {
        get
        {
            Debug.Assert(_fontDescriptor != null);
            return _fontDescriptor;
        }
        set { _fontDescriptor = value; }
    }
    PdfFontDescriptor _fontDescriptor = null!;

    /// <summary>
    /// Gets a value indicating whether this instance is symbol font.
    /// </summary>
    public bool IsSymbolFont
    {
        get { return _fontDescriptor.IsSymbolFont; }
    }

    internal void AddChars(string text)
    {
        if (_characterMapInfo != null)
            _characterMapInfo.AddChars(text);
    }

    internal void AddShapedText(string text, ShapeSettings features, string? logicalText = null)
    {
        if (_characterMapInfo != null)
            _characterMapInfo.AddShapedText(text, features, logicalText);
    }

    internal void AddGlyphIndices(string glyphIndices)
    {
        if (_characterMapInfo != null)
            _characterMapInfo.AddGlyphIndices(glyphIndices);
    }

    internal void AddShapedGlyph(int glyphIndex, string sourceText)
    {
        if (_characterMapInfo != null)
            _characterMapInfo.AddShapedGlyph(glyphIndex, sourceText);
    }

    /// <summary>
    /// Gets or sets the CharacterMapInfo.
    /// </summary>
    internal CharacterMapInfo CharacterMapInfo
    {
        get { return _characterMapInfo; }
        set { _characterMapInfo = value; }
    }
    internal CharacterMapInfo _characterMapInfo = null!;

    /// <summary>
    /// Gets or sets ToUnicodeMap.
    /// </summary>
    internal PdfToUnicodeMap ToUnicodeMap
    {
        get { return _toUnicode; }
        set { _toUnicode = value; }
    }
    internal PdfToUnicodeMap _toUnicode = null!;

    /// <summary>
    /// Adds a tag of exactly six uppercase letters to the font name 
    /// according to PDF Reference Section 5.5.3 'Font Subsets'
    /// </summary>
    internal static string CreateEmbeddedFontSubsetName(string name)
    {
        StringBuilder s = new StringBuilder(64);
        byte[] bytes = Guid.NewGuid().ToByteArray();
        for (int idx = 0; idx < 6; idx++)
            s.Append((char)('A' + bytes[idx] % 26));
        s.Append('+');
        if (name.StartsWith("/"))
            s.Append(name.Substring(1));
        else
            s.Append(name);
        return s.ToString();
    }

    /// <summary>
    /// Predefined keys common to all font dictionaries.
    /// </summary>
    internal class Keys : KeysBase
    {
        /// <summary>
        /// (Required) The type of PDF object that this dictionary describes;
        /// must be Font for a font dictionary.
        /// </summary>
        [PdfKey(PdfKeyType.Name | PdfKeyType.Required, FixedValue = "Font")]
        public const string Type = "/Type";

        /// <summary>
        /// (Required) The type of font.
        /// </summary>
        [PdfKey(PdfKeyType.Name | PdfKeyType.Required)]
        public const string Subtype = "/Subtype";

        /// <summary>
        /// (Required) The PostScript name of the font.
        /// </summary>
        [PdfKey(PdfKeyType.Name | PdfKeyType.Required)]
        public const string BaseFont = "/BaseFont";

        /// <summary>
        /// (Required except for the standard 14 fonts; must be an indirect reference)
        /// A font descriptor describing the font's metrics other than its glyph widths.
        /// Note: For the standard 14 fonts, the entries FirstChar, LastChar, Widths, and 
        /// FontDescriptor must either all be present or all be absent. Ordinarily, they are
        /// absent; specifying them enables a standard font to be overridden.
        /// </summary>
        [PdfKey(PdfKeyType.Dictionary | PdfKeyType.MustBeIndirect, typeof(PdfFontDescriptor))]
        public const string FontDescriptor = "/FontDescriptor";
    }
}
