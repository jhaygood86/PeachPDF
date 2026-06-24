using PeachPDF.Html.Core;

namespace PeachPDF
{
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
