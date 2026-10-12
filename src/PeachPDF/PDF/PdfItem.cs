// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachPDF.PDF.IO;
using System;

namespace PeachPDF.PDF;

/// <summary>
/// The base class of all PDF objects and simple PDF types.
/// </summary>
internal abstract class PdfItem
{
    // All simple types (i.e. derived from PdfItem but not from PdfObject) must be immutable.

/// <summary>
    /// When overridden in a derived class, appends a raw string representation of this object
    /// to the specified PdfWriter.
    /// </summary>
    internal abstract void WriteObject(PdfWriter writer);
}
