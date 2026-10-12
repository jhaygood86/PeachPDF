// Derived from PDFsharp (MIT) - see LICENSE.md

using System;

namespace PeachPDF.PDF;

/// <summary>
/// Identifies the state of the document
/// </summary>
[Flags]
internal enum DocumentState
{
    /// <summary>
    /// The document was created from scratch.
    /// </summary>
    Created = 0x0001,

    /// <summary>
    /// The document was created by opening an existing PDF file.
    /// </summary>
    Imported = 0x0002,

    /// <summary>
    /// The document is disposed.
    /// </summary>
    Disposed = 0x8000,
}
