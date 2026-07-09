using PeachPDF.Html.Core;

namespace PeachPDF
{
    /// <summary>
    /// An opaque, pre-parsed stylesheet produced by <see cref="PdfGenerator.ParseStyleSheet"/>. Pass an instance
    /// to a <c>GeneratePdf</c>/<c>AddPdfPages</c> overload's <c>cssData</c> parameter to reuse the same parsed CSS
    /// across multiple renders instead of re-parsing identical stylesheet text on every call.
    /// </summary>
    public class PeachPdfCssContent
    {
        private readonly CssData _cssData;

        internal PeachPdfCssContent(CssData cssData)
        {
            _cssData = cssData;
        }

        internal CssData CssData => _cssData;
    }
}
