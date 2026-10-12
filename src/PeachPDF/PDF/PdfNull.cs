// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachPDF.PDF.IO;

namespace PeachPDF.PDF;

/// <summary>
/// Represents a indirect reference that is not in the cross reference table.
/// </summary>
internal sealed class PdfNull : PdfItem
{
    // Reference: 3.2.8  Null Object / Page 63

    PdfNull()
    { }

    /// <summary>
    /// Returns a <see cref="T:System.String"></see> that represents the current <see cref="T:System.Object"></see>.
    /// </summary>
    /// <returns>
    /// A <see cref="T:System.String"></see> that represents the current <see cref="T:System.Object"></see>.
    /// </returns>
    public override string ToString()
    {
        return "null";
    }

    internal override void WriteObject(PdfWriter writer)
    {
        // Implementet because it must be overridden.
        writer.WriteRaw(" null ");
    }

    /// <summary>
    /// The only instance of this class.
    /// </summary>
    public static readonly PdfNull Value = new PdfNull();
}
