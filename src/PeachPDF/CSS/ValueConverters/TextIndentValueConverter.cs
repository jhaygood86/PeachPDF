#nullable disable

using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    /// <summary>
    /// Validates a <c>text-indent</c> value through the shared <see cref="TextIndentGrammar"/> -
    /// accepting a <c>&lt;length-percentage&gt;</c> optionally combined with <c>hanging</c>/
    /// <c>each-line</c>, in any order, and rejecting everything else so an invalid value is dropped at
    /// parse time. The authored text is preserved so Layer B (<see cref="Html.Core.Dom.CssLayoutEngine"/>)
    /// re-parses the same value during layout.
    /// </summary>
    internal sealed class TextIndentValueConverter : IValueConverter
    {
        public IPropertyValue Convert(IEnumerable<Token> value)
        {
            var tokens = value.ToArray();
            return TextIndentGrammar.TryParse(tokens, out _, out _, out _) ? new TextIndentValue(tokens) : null;
        }

        public IPropertyValue Construct(Property[] properties)
        {
            return properties.Guard<TextIndentValue>();
        }

        private sealed class TextIndentValue : IPropertyValue
        {
            public TextIndentValue(IEnumerable<Token> tokens)
            {
                Original = new TokenValue(tokens);
            }

            public string CssText => Original.Text;

            public TokenValue Original { get; }

            public TokenValue ExtractFor(string name) => Original;
        }
    }
}
