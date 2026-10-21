#nullable enable

namespace PeachDrawing.PDF
{
    /// <summary>Which of a page's annotations <see cref="PdfPageSource.Paint(int, PeachDrawing.Core.Canvas, PdfAnnotationPaint, PdfTagRecorder)"/> draws, over its content.</summary>
    public enum PdfAnnotationPaint
    {
        /// <summary>None: only the page's content, which is what a page is without any markup.</summary>
        None,

        /// <summary>
        /// The appearance of every visible annotation except form fields (text markup, stamps, notes, drawings...). Links and popups have no
        /// appearance. Use it when the form fields are carried separately, as <see cref="PdfPageSource.GetFormFields"/> returns them.
        /// </summary>
        ExceptFormFields,

        /// <summary>Every visible annotation, form fields included, as a viewer shows the page.</summary>
        All,
    }
}
