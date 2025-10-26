using PeachPDF.CSS;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using System.Linq;
using System.Text;

namespace PeachPDF.Html.Core.Dom
{
    internal static class CssContentEngine
    {
        public static void ApplyContent(CssBox cssBox)
        {
            if (cssBox.Content is CssConstants.None or CssConstants.Normal)
            {
                return;
            }

            var tokens = CssValueParser.GetCssTokens(cssBox.Content);

            var contentText = new StringBuilder();

            foreach (var token in tokens)
            {
                switch (token)
                {
                    case StringToken stringToken:
                        contentText.Append(stringToken.Data);
                        break;
                    case FunctionToken { Data: CssConstants.Counter } functionToken:
                        {
                            var arguments = functionToken.ArgumentTokens.ToArray();

                            var counterName = (KeywordToken)arguments[0];
                            var counter = CssCounterEngine.GetCounter(cssBox, counterName.Data);

                            var counterValue = counter?.Value ?? 1;

                            if (arguments.Length is 1)
                            {
                                contentText.Append(counterValue);
                            }

                            break;
                        }
                }
            }

            cssBox.Text = contentText.ToString();
        }
    }
}
