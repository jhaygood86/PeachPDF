using PeachPDF.PdfSharpCore.Pdf;

namespace PeachPDF.PdfSharpCore.Exceptions
{
    internal class PositionNotFoundException : System.Exception
    {
        public PositionNotFoundException(PdfObjectID id) : base($"Object with ID {id} resolved with negative position ") { }
    }
}
