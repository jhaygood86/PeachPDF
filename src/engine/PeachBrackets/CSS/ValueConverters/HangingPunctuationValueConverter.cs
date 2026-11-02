#nullable disable

using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Css
{
    /// <summary>
    /// Validates a <c>hanging-punctuation</c> value through the shared <see cref="HangingPunctuationGrammar"/>.
    /// The authored text is preserved so Layer B re-parses the
    /// same value during layout — the <see cref="AspectRatioValueConverter"/> precedent.
    /// </summary>
    internal sealed class HangingPunctuationValueConverter : IValueConverter
    {
        public IPropertyValue Convert(IReadOnlyList<Token> value)
        {
            var tokens = value.ToArray();
            return HangingPunctuationGrammar.TryParse(tokens, out _) ? new HangingPunctuationValue(tokens) : null;
        }

        public IPropertyValue Construct(Property[] properties)
        {
            return properties.Guard<HangingPunctuationValue>();
        }

        private sealed class HangingPunctuationValue : IPropertyValue
        {
            public HangingPunctuationValue(IEnumerable<Token> tokens)
            {
                Original = new TokenValue(tokens);
            }

            public string CssText => Original.Text;

            public TokenValue Original { get; }

            public TokenValue ExtractFor(string name) => Original;
        }
    }
}
