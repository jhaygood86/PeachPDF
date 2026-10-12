// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachDrawing.Text;
using System;

namespace PeachPDF.PDF.Advanced;

/// <summary>
/// The PDF font descriptor flags.
/// </summary>
[Flags]
internal enum PdfFontDescriptorFlags
{
    /// <summary>
    /// All glyphs have the same width (as opposed to proportional or variable-pitch
    /// fonts, which have different widths).
    /// </summary>
    FixedPitch = 1 << 0,

    /// <summary>
    /// Glyphs have serifs, which are short strokes drawn at an angle on the top and
    /// bottom of glyph stems. (Sans serif fonts do not have serifs.)
    /// </summary>
    Serif = 1 << 1,

    /// <summary>
    /// Font contains glyphs outside the Adobe standard Latin character set. This
    /// flag and the Nonsymbolic flag cannot both be set or both be clear.
    /// </summary>
    Symbolic = 1 << 2,

    /// <summary>
    /// Glyphs resemble cursive handwriting.
    /// </summary>
    Script = 1 << 3,

    /// <summary>
    /// Font uses the Adobe standard Latin character set or a subset of it.
    /// </summary>
    Nonsymbolic = 1 << 5,

    /// <summary>
    /// Glyphs have dominant vertical strokes that are slanted.
    /// </summary>
    Italic = 1 << 6,

    /// <summary>
    /// Font contains no lowercase letters; typically used for display purposes,
    /// such as for titles or headlines.
    /// </summary>
    AllCap = 1 << 16,

    /// <summary>
    /// Font contains both uppercase and lowercase letters. The uppercase letters are
    /// similar to those in the regular version of the same typeface family. The glyphs
    /// for the lowercase letters have the same shapes as the corresponding uppercase
    /// letters, but they are sized and their proportions adjusted so that they have the
    /// same size and stroke weight as lowercase glyphs in the same typeface family.
    /// </summary>
    SmallCap = 1 << 17,

    /// <summary>
    /// Determines whether bold glyphs are painted with extra pixels even at very small
    /// text sizes.
    /// </summary>
    ForceBold = 1 << 18,
}

/// <summary>
/// A PDF font descriptor specifies metrics and other attributes of a simple font, 
/// as distinct from the metrics of individual glyphs.
/// </summary>
internal sealed class PdfFontDescriptor : PdfDictionary
{
    internal PdfFontDescriptor(PdfDocument document, Typeface typeface)
        : base(document)
    {
        _typeface = typeface;
        var metrics = typeface.Metrics;
        Elements.SetName(Keys.Type, "/FontDescriptor");

        Elements.SetInteger(Keys.Ascent, PdfTypefaceMetrics.DesignUnitsToPdf(typeface, metrics.CellAscent));
        Elements.SetInteger(Keys.CapHeight, PdfTypefaceMetrics.DesignUnitsToPdf(typeface, metrics.CapHeight));
        Elements.SetInteger(Keys.Descent, PdfTypefaceMetrics.DesignUnitsToPdf(typeface, metrics.CellDescent));
        Elements.SetInteger(Keys.Flags, (int)FlagsFromMetrics(metrics));
        Elements.SetRectangle(Keys.FontBBox, new PdfRectangle(
          PdfTypefaceMetrics.DesignUnitsToPdf(typeface, metrics.XMin),
          PdfTypefaceMetrics.DesignUnitsToPdf(typeface, metrics.YMin),
          PdfTypefaceMetrics.DesignUnitsToPdf(typeface, metrics.XMax),
          PdfTypefaceMetrics.DesignUnitsToPdf(typeface, metrics.YMax)));
        // The font name is set by the PDF font that owns this descriptor.
        Elements.SetReal(Keys.ItalicAngle, metrics.ItalicAngle);
        // The value doesn't matter to a reader that draws from the embedded font, so it is always 0.
        Elements.SetInteger(Keys.StemV, 0);
        Elements.SetInteger(Keys.XHeight, PdfTypefaceMetrics.DesignUnitsToPdf(typeface, metrics.XHeight));
    }

    /// <summary>The typeface this descriptor describes and embeds.</summary>
    internal readonly Typeface _typeface;

    /// <summary>
    /// Gets or sets the name of the font.
    /// </summary>
    public string FontName
    {
        get => Elements.GetName(Keys.FontName);
        set { Elements.SetName(Keys.FontName, value); }
    }

    /// <summary>
    /// Gets a value indicating whether this instance is symbol font.
    /// </summary>
    public bool IsSymbolFont => _isSymbolFont;
    bool _isSymbolFont;

            PdfFontDescriptorFlags FlagsFromMetrics(TypefaceMetrics metrics)
    {
        PdfFontDescriptorFlags flags = 0;
        _isSymbolFont = metrics.IsSymbolic;
        flags |= metrics.IsSymbolic ? PdfFontDescriptorFlags.Symbolic : PdfFontDescriptorFlags.Nonsymbolic;

        // The 'post' table's isFixedPitch is the authoritative OpenType signal for a monospaced
        // font (unlike PANOSE, which is advisory) - nonzero means every glyph advances by the
        // same width.
        if (metrics.IsFixedPitch)
            flags |= PdfFontDescriptorFlags.FixedPitch;

        // Only the 'OS/2' serif family classes (1-7) map to PDF's Serif flag; Sans Serif, Ornamentals,
        // Scripts and Symbolic are none of them.
        if (metrics.HasSerifs)
            flags |= PdfFontDescriptorFlags.Serif;

        if (metrics.IsItalicStyle)
            flags |= PdfFontDescriptorFlags.Italic;

        return flags;
    }

    /// <summary>
    /// Predefined keys of this dictionary.
    /// </summary>
    internal sealed class Keys : KeysBase
    {
        /// <summary>
        /// (Required) The type of PDF object that this dictionary describes; must be
        /// FontDescriptor for a font descriptor.
        /// </summary>
        [PdfKey(PdfKeyType.Name | PdfKeyType.Required, FixedValue = "FontDescriptor")]
        public const string Type = "/Type";

        /// <summary>
        /// (Required) The PostScript name of the font. This name should be the same as the 
        /// value of BaseFont in the font or CIDFont dictionary that refers to this font descriptor.
        /// </summary>
        [PdfKey(PdfKeyType.Name | PdfKeyType.Required)]
        public const string FontName = "/FontName";

        /// <summary>
        /// (Optional; PDF 1.5; strongly recommended for Type 3 fonts in Tagged PDF documents)
        /// A string specifying the preferred font family name. For example, for the font 
        /// Times Bold Italic, the FontFamily is Times.
        /// </summary>
        [PdfKey(PdfKeyType.String | PdfKeyType.Optional)]
        public const string FontFamily = "/FontFamily";

        /// <summary>
        /// (Optional; PDF 1.5; strongly recommended for Type 3 fonts in Tagged PDF documents)
        /// The font stretch value. It must be one of the following names (ordered from 
        /// narrowest to widest): UltraCondensed, ExtraCondensed, Condensed, SemiCondensed, 
        /// Normal, SemiExpanded, Expanded, ExtraExpanded or UltraExpanded.
        /// Note: The specific interpretation of these values varies from font to font. 
        /// For example, Condensed in one font may appear most similar to Normal in another.
        /// </summary>
        [PdfKey(PdfKeyType.Name | PdfKeyType.Optional)]
        public const string FontStretch = "/FontStretch";

        /// <summary>
        /// (Optional; PDF 1.5; strongly recommended for Type 3 fonts in Tagged PDF documents)
        /// The weight (thickness) component of the fully-qualified font name or font specifier.
        /// The possible values are 100, 200, 300, 400, 500, 600, 700, 800, or 900, where each
        /// number indicates a weight that is at least as dark as its predecessor. A value of 
        /// 400 indicates a normal weight; 700 indicates bold.
        /// Note: The specific interpretation of these values varies from font to font. 
        /// For example, 300 in one font may appear most similar to 500 in another.
        /// </summary>
        [PdfKey(PdfKeyType.Real | PdfKeyType.Optional)]
        public const string FontWeight = "/FontWeight";

        /// <summary>
        /// (Required) A collection of flags defining various characteristics of the font.
        /// </summary>
        [PdfKey(PdfKeyType.Integer | PdfKeyType.Required)]
        public const string Flags = "/Flags";

        /// <summary>
        /// (Required, except for Type 3 fonts) A rectangle (see Section 3.8.4, "Rectangles"),
        /// expressed in the glyph coordinate system, specifying the font bounding box. This 
        /// is the smallest rectangle enclosing the shape that would result if all of the 
        /// glyphs of the font were placed with their origins coincident and then filled.
        /// </summary>
        [PdfKey(PdfKeyType.Rectangle | PdfKeyType.Required)]
        public const string FontBBox = "/FontBBox";

        /// <summary>
        /// (Required) The angle, expressed in degrees counterclockwise from the vertical, of
        /// the dominant vertical strokes of the font. (For example, the 9-o'clock position is 90 
        /// degrees, and the 3-o'clock position is -90 degrees.) The value is negative for fonts 
        /// that slope to the right, as almost all italic fonts do.
        /// </summary>
        [PdfKey(PdfKeyType.Real | PdfKeyType.Required)]
        public const string ItalicAngle = "/ItalicAngle";

        /// <summary>
        /// (Required, except for Type 3 fonts) The maximum height above the baseline reached 
        /// by glyphs in this font, excluding the height of glyphs for accented characters.
        /// </summary>
        [PdfKey(PdfKeyType.Real | PdfKeyType.Required)]
        public const string Ascent = "/Ascent";

        /// <summary>
        /// (Required, except for Type 3 fonts) The maximum depth below the baseline reached 
        /// by glyphs in this font. The value is a negative number.
        /// </summary>
        [PdfKey(PdfKeyType.Real | PdfKeyType.Required)]
        public const string Descent = "/Descent";

        /// <summary>
        /// (Optional) The spacing between baselines of consecutive lines of text.
        /// Default value: 0.
        /// </summary>
        [PdfKey(PdfKeyType.Real | PdfKeyType.Optional)]
        public const string Leading = "/Leading";

        /// <summary>
        /// (Required for fonts that have Latin characters, except for Type 3 fonts) The vertical
        /// coordinate of the top of flat capital letters, measured from the baseline.
        /// </summary>
        [PdfKey(PdfKeyType.Real | PdfKeyType.Required)]
        public const string CapHeight = "/CapHeight";

        /// <summary>
        /// (Optional) The font's x height: the vertical coordinate of the top of flat nonascending
        /// lowercase letters (like the letter x), measured from the baseline, in fonts that have 
        /// Latin characters. Default value: 0.
        /// </summary>
        [PdfKey(PdfKeyType.Real | PdfKeyType.Optional)]
        public const string XHeight = "/XHeight";

        /// <summary>
        /// (Required, except for Type 3 fonts) The thickness, measured horizontally, of the dominant 
        /// vertical stems of glyphs in the font.
        /// </summary>
        [PdfKey(PdfKeyType.Real | PdfKeyType.Required)]
        public const string StemV = "/StemV";

        /// <summary>
        /// (Optional) The thickness, measured vertically, of the dominant horizontal stems 
        /// of glyphs in the font. Default value: 0.
        /// </summary>
        [PdfKey(PdfKeyType.Real | PdfKeyType.Optional)]
        public const string StemH = "/StemH";

        /// <summary>
        /// (Optional) The average width of glyphs in the font. Default value: 0.
        /// </summary>
        [PdfKey(PdfKeyType.Real | PdfKeyType.Optional)]
        public const string AvgWidth = "/AvgWidth";

        /// <summary>
        /// (Optional) The maximum width of glyphs in the font. Default value: 0.
        /// </summary>
        [PdfKey(PdfKeyType.Real | PdfKeyType.Optional)]
        public const string MaxWidth = "/MaxWidth";

        /// <summary>
        /// (Optional) The width to use for character codes whose widths are not specified in a 
        /// font dictionary's Widths array. This has a predictable effect only if all such codes 
        /// map to glyphs whose actual widths are the same as the value of the MissingWidth entry.
        /// Default value: 0.
        /// </summary>
        [PdfKey(PdfKeyType.Real | PdfKeyType.Optional)]
        public const string MissingWidth = "/MissingWidth";

        /// <summary>
        /// (Optional) A stream containing a Type 1 font program.
        /// </summary>
        [PdfKey(PdfKeyType.Stream | PdfKeyType.Optional)]
        public const string FontFile = "/FontFile";

        /// <summary>
        /// (Optional; PDF 1.1) A stream containing a TrueType font program.
        /// </summary>
        [PdfKey(PdfKeyType.Stream | PdfKeyType.Optional)]
        public const string FontFile2 = "/FontFile2";

        /// <summary>
        /// (Optional; PDF 1.2) A stream containing a font program whose format is specified 
        /// by the Subtype entry in the stream dictionary.
        /// </summary>
        [PdfKey(PdfKeyType.Stream | PdfKeyType.Optional)]
        public const string FontFile3 = "/FontFile3";

        /// <summary>
        /// (Optional; meaningful only in Type 1 fonts; PDF 1.1) A string listing the character
        /// names defined in a font subset. The names in this string must be in PDF syntax - that is,
        /// each name preceded by a slash (/). The names can appear in any order. The name .notdef
        /// should be omitted; it is assumed to exist in the font subset. If this entry is absent,
        /// the only indication of a font subset is the subset tag in the FontName entry.
        /// </summary>
        [PdfKey(PdfKeyType.String | PdfKeyType.Optional)]
        public const string CharSet = "/CharSet";

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
