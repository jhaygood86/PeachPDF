#nullable enable

using System;

namespace PeachPDF
{
    /// <summary>
    /// Thrown when HTML/CSS parsing, layout, or painting fails while a <see cref="PdfGenerator"/> method is
    /// rendering a document. <see cref="RenderErrorType"/> identifies which pipeline phase raised it.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="renderErrorType">The pipeline phase the error occurred in.</param>
    /// <param name="innerException">Optional: the underlying exception that caused this error, if any.</param>
    public class HtmlRenderException(string message, HtmlRenderErrorType renderErrorType, Exception? innerException = null) : Exception(message, innerException)
    {
        /// <summary>
        /// The pipeline phase this error occurred in.
        /// </summary>
        public HtmlRenderErrorType RenderErrorType { get; } = renderErrorType;

    }
}
