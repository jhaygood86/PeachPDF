// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachDrawing.Core;
using PeachPDF.PDF.Drawing;
using PeachDrawing.Text;
using System.Diagnostics;
using System.Text;

namespace PeachPDF.PDF.Advanced;

/// <summary>
/// Represents a composite font. Used for Unicode encoding.
/// </summary>
internal sealed class PdfType0Font : PdfFont
{
    public PdfType0Font(PdfDocument document)
        : base(document)
    { }

    public PdfType0Font(PdfDocument document, TypefaceFont font)
        : base(document)
    {
        Elements.SetName(Keys.Type, "/Font");
        Elements.SetName(Keys.Subtype, "/Type0");
        Elements.SetName(Keys.Encoding, "/Identity-H");

        FontDescriptor = new PdfFontDescriptor(document, font.Typeface);
        _characterMapInfo = new CharacterMapInfo(font.Typeface);
        _descendantFont = new PdfCidFont(document, FontDescriptor, font);
        _descendantFont.CharacterMapInfo = _characterMapInfo;

        // Create ToUnicode map
        _toUnicode = new PdfToUnicodeMap(document, _characterMapInfo);
        document.Internals.AddObject(_toUnicode);
        Elements.Add(Keys.ToUnicode, _toUnicode);

        BaseFont = PdfTypefaceMetrics.GetBaseName(font.Typeface);
        // CID fonts are always embedded
        BaseFont = PdfFont.CreateEmbeddedFontSubsetName(BaseFont);

        FontDescriptor.FontName = BaseFont;
        _descendantFont.BaseFont = BaseFont;

        PdfArray descendantFonts = new PdfArray(document);
        Owner._irefTable.Add(_descendantFont);
        descendantFonts.Elements.Add(_descendantFont.Reference);
        Elements[Keys.DescendantFonts] = descendantFonts;
    }

    public string BaseFont
    {
        get => Elements.GetName(Keys.BaseFont);
        set { Elements.SetName(Keys.BaseFont, value); }
    }

    internal PdfCidFont DescendantFont => _descendantFont;
    readonly PdfCidFont _descendantFont = null!;

    internal override void PrepareForSave()
    {
        base.PrepareForSave();

        // Use GetGlyphIndices to create the widths array.
        Typeface typeface = FontDescriptor._typeface;
        StringBuilder w = new StringBuilder("[");
        if (_characterMapInfo is not null)
        {
            int[] glyphIndices = _characterMapInfo.GetGlyphIndices();
            int count = glyphIndices.Length;
            int[] glyphWidths = new int[count];

            for (int idx = 0; idx < count; idx++)
                glyphWidths[idx] = PdfTypefaceMetrics.GlyphWidth(typeface, glyphIndices[idx]);

            //TODO: optimize order of indices

            for (int idx = 0; idx < count; idx++)
                w.AppendFormat("{0}[{1}]", glyphIndices[idx], glyphWidths[idx]);
            w.Append("]");
            _descendantFont.Elements.SetValue(PdfCidFont.Keys.W, new PdfLiteral(w.ToString()));

        }
        _descendantFont.PrepareForSave();
        _toUnicode.PrepareForSave();
    }

    /// <summary>
    /// Predefined keys of this dictionary.
    /// </summary>
    internal new sealed class Keys : PdfFont.Keys
    {
        /// <summary>
        /// (Required) The type of PDF object that this dictionary describes;
        /// must be Font for a font dictionary.
        /// </summary>
        [PdfKey(PdfKeyType.Name | PdfKeyType.Required, FixedValue = "Font")]
        public new const string Type = "/Type";

        /// <summary>
        /// (Required) The type of font; must be Type0 for a Type 0 font.
        /// </summary>
        [PdfKey(PdfKeyType.Name | PdfKeyType.Required)]
        public new const string Subtype = "/Subtype";

        /// <summary>
        /// (Required) The PostScript name of the font. In principle, this is an arbitrary
        /// name, since there is no font program associated directly with a Type 0 font
        /// dictionary. The conventions described here ensure maximum compatibility
        /// with existing Acrobat products.
        /// If the descendant is a Type 0 CIDFont, this name should be the concatenation
        /// of the CIDFont's BaseFont name, a hyphen, and the CMap name given in the
        /// Encoding entry (or the CMapName entry in the CMap). If the descendant is a
        /// Type 2 CIDFont, this name should be the same as the CIDFont's BaseFont name.
        /// </summary>
        [PdfKey(PdfKeyType.Name | PdfKeyType.Required)]
        public new const string BaseFont = "/BaseFont";

        /// <summary>
        /// (Required) The name of a predefined CMap, or a stream containing a CMap
        /// that maps character codes to font numbers and CIDs. If the descendant is a
        /// Type 2 CIDFont whose associated TrueType font program is not embedded
        /// in the PDF file, the Encoding entry must be a predefined CMap name.
        /// </summary>
        [PdfKey(PdfKeyType.StreamOrName | PdfKeyType.Required)]
        public const string Encoding = "/Encoding";

        /// <summary>
        /// (Required) A one-element array specifying the CIDFont dictionary that is the
        /// descendant of this Type 0 font.
        /// </summary>
        [PdfKey(PdfKeyType.Array | PdfKeyType.Required)]
        public const string DescendantFonts = "/DescendantFonts";

        /// <summary>
        /// ((Optional) A stream containing a CMap file that maps character codes to
        /// Unicode values.
        /// </summary>
        [PdfKey(PdfKeyType.Stream | PdfKeyType.Optional)]
        public const string ToUnicode = "/ToUnicode";

        /// <summary>
        /// Gets the KeysMeta for these keys.
        /// </summary>
        internal static PdfDictionaryMetadata Meta { get; } = CreateMeta(typeof(Keys));
    }

    /// <summary>
    /// Gets the KeysMeta of this dictionary type.
    /// </summary>
    internal override PdfDictionaryMetadata Meta => Keys.Meta;
}
