// Derived from PDFsharp (MIT) - see LICENSE.md

using System;

namespace PeachPDF.PDF.Advanced;

/// <summary>
/// Base class for FontTable, ImageTable, FormXObjectTable etc.
/// </summary>
internal class PdfResourceTable
{
    /// <summary>
    /// Base class for document wide resource tables.
    /// </summary>
    public PdfResourceTable(PdfDocument owner)
    {
        if (owner == null)
            throw new ArgumentNullException("owner");
        _owner = owner;
    }

    /// <summary>
    /// Gets the owning document of this resource table.
    /// </summary>
    protected PdfDocument Owner
    {
        get { return _owner; }
    }
    readonly PdfDocument _owner;
}
