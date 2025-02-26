using System.Linq;
using System.Text;
using PeachPDF.CSS;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;

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
                if (token is StringToken stringToken)
                {
                    contentText.Append(stringToken.Data);
                }

                if (token is FunctionToken functionToken)
                {
                    if (functionToken.Data is CssConstants.Counter)
                    {
                        var arguments = functionToken.ArgumentTokens.ToArray();

                        var counterName = (KeywordToken)arguments[0];
                        var counter = CssCounterEngine.GetCounter(cssBox, counterName.Data);

                        var counterValue = counter?.Value ?? 1;

                        if (arguments.Length is 1)
                        {
                            contentText.Append(counterValue);
                        }
                    }
                }
            }

            cssBox.Text = contentText.ToString();
        }
    }
}
