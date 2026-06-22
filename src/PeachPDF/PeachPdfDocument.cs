using PeachPDF.PdfSharpCore.Pdf;
using System.IO;

namespace PeachPDF;

public class PeachPdfDocument
{
    private readonly PdfDocument _document;

    internal PeachPdfDocument(PdfDocument document)
    {
        _document = document;
    }

    internal PdfDocument PdfDocument => _document;

    public void Save(Stream stream)
    {
        _document.Save(stream);
    }
}
