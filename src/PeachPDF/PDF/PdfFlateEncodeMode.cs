// Derived from PDFsharp (MIT) - see LICENSE.md

namespace PeachPDF.PDF;

/// <summary>
/// Sets the mode for the Deflater (PdfFlateEncoder).
/// </summary>
internal enum PdfFlateEncodeMode
{
    /// <summary>
    /// The default mode.
    /// </summary>
    Default,

    /// <summary>
    /// Fast encoding, but larger PDF files.
    /// </summary>
    BestSpeed,

    /// <summary>
    /// Best compression, but takes more time.
    /// </summary>
    BestCompression,
}
