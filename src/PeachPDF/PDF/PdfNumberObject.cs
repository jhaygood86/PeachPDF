// Derived from PDFsharp (MIT) - see LICENSE.md

namespace PeachPDF.PDF;

/// <summary>
/// Base class for indirect number values (not yet used, maybe superfluous).
/// </summary>
internal abstract class PdfNumberObject : PdfObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfNumberObject"/> class.
    /// </summary>
    protected PdfNumberObject()
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfNumberObject"/> class.
    /// </summary>
    /// <param name="document">The document.</param>
    protected PdfNumberObject(PdfDocument document)
        : base(document)
    { }
}
