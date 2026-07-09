using PeachPDF.PdfSharpCore.Pdf;
using System.IO;

namespace PeachPDF;

/// <summary>
/// A PDF document produced by <see cref="PdfGenerator"/>. Instances are created by calling one of the
/// <c>PdfGenerator.GeneratePdf</c> overloads and are written out with <see cref="Save"/>.
/// </summary>
public class PeachPdfDocument
{
    private readonly PdfDocument _document;

    internal PeachPdfDocument(PdfDocument document)
    {
        _document = document;
    }

    internal PdfDocument PdfDocument => _document;

    /// <summary>
    /// The number of pages currently in the document.
    /// </summary>
    public int PageCount => _document.PageCount;

    internal PdfPages Pages => _document.Pages;

    /// <summary>
    /// Writes the completed PDF to the given stream.
    /// </summary>
    /// <param name="stream">The destination stream. The caller owns the stream and is responsible for disposing it.</param>
    public void Save(Stream stream)
    {
        _document.Save(stream);
    }
}
