// Derived from PDFsharp (MIT) - see LICENSE.md

using System;

namespace PeachPDF.PDF;

/// <summary>
/// Base class of all exceptions in the PDFsharp frame work.
/// </summary>
internal class PdfException : Exception
{
    // The class is not yet used

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfException"/> class.
    /// </summary>
    public PdfException()
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfException"/> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public PdfException(string message)
        : base(message)
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfException"/> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public PdfException(string message, Exception innerException) :
        base(message, innerException)
    { }
}
