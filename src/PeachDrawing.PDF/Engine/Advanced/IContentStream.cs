// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachDrawing.Core;
using PeachDrawing.PDF.Engine.Drawing;

namespace PeachDrawing.PDF.Engine.Advanced;

internal interface IContentStream
{
    PdfResources Resources { get; }

    string GetFontName(TypefaceFont font, out PdfFont pdfFont);

    string GetImageName(PdfRasterImage image, double width, double height);

    string GetFormName(PdfForm form);
}
