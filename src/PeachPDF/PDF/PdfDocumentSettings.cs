// Derived from PDFsharp (MIT) - see LICENSE.md

namespace PeachPDF.PDF;

/// <summary>
/// Holds PDF specific information of the document.
/// </summary>
internal sealed class PdfDocumentSettings
{
    internal PdfDocumentSettings(PdfDocument document)
    { }

    /// <summary>
    /// Gets or sets the default trim margins.
    /// </summary>
    public TrimMargins TrimMargins
    {
        get
        {
            if (_trimMargins == null)
                _trimMargins = new TrimMargins();
            return _trimMargins;
        }
        set
        {
            if (_trimMargins == null)
                _trimMargins = new TrimMargins();
            if (value != null)
            {
                _trimMargins.Left = value.Left;
                _trimMargins.Right = value.Right;
                _trimMargins.Top = value.Top;
                _trimMargins.Bottom = value.Bottom;
            }
            else
                _trimMargins.All = 0;
        }
    }
    TrimMargins _trimMargins = new TrimMargins();
}
