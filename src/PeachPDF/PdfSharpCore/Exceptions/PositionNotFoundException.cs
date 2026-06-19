using PeachPDF.PdfSharpCore.Pdf;

namespace PeachPDF.PdfSharpCore.Exceptions
{
    public class PositionNotFoundException : System.Exception
    {
        public PositionNotFoundException(PdfObjectID id) : base($"Object with ID {id} resolved with negative position ") { }
    }
}
