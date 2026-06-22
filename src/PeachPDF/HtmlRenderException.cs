#nullable enable

using System;

namespace PeachPDF
{
    public class HtmlRenderException(string message, HtmlRenderErrorType renderErrorType, Exception? innerException = null) : Exception(message, innerException)
    {
        public HtmlRenderErrorType RenderErrorType { get; } = renderErrorType;

    }
}
