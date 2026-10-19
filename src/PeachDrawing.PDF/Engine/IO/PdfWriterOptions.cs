// Derived from PDFsharp (MIT) - see LICENSE.md

using System;

namespace PeachDrawing.PDF.Engine.IO;

/// <summary>
/// INTERNAL USE ONLY.
/// </summary>
[Flags]
internal enum PdfWriterOptions
{
    /// <summary>
    /// If only this flag is specified the result is a regular valid PDF stream.
    /// </summary>
    Regular = 0x000000,

    /// <summary>
    /// Omit writing stream data. For debugging purposes only. 
    /// With this option the result is not valid PDF.
    /// </summary>
    OmitStream = 0x000001,

    /// <summary>
    /// Omit inflate filter. For debugging purposes only.
    /// </summary>
    OmitInflation = 0x000002,
}
