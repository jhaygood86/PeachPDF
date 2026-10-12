// Derived from PDFsharp (MIT) - see LICENSE.md

namespace PeachPDF.PDF;

internal static class PdfConstants
{
    /// <summary>The sine of the 20 degree skew applied to simulate an italic style when a font has none.</summary>
    public const double ItalicSkewAngleSinus = 0.34202014332566873304409961468226;

    /// <summary>The stroke width, as a fraction of the font size, used to simulate a bold style.</summary>
    public const double BoldEmphasis = 0.02;
}
