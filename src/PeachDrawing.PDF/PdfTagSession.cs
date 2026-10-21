#nullable enable

using PeachDrawing.PDF.Import;

namespace PeachDrawing.PDF
{
    /// <summary>
    /// The tagging of one drawing of a page of an opened PDF onto a document being tagged: it ties the structure elements recorded for that drawing
    /// together, so the page's links can be tied to them afterwards. Made by <see cref="PdfPageSource.CreateTagSession"/>.
    /// </summary>
    public sealed class PdfTagSession
    {
        internal PdfTagSession(PdfTagBinding binding)
        {
            Binding = binding;
        }

        internal PdfTagBinding Binding { get; }
    }
}
