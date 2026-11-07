#nullable disable

using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Css
{
    /// <summary>Validates an <c>initial-letter</c> value through the shared <see cref="InitialLetterGrammar"/>, keeping the authored text.</summary>
    internal sealed class InitialLetterValueConverter : IValueConverter
    {
        public IPropertyValue Convert(IReadOnlyList<Token> value)
        {
            var tokens = value.ToArray();
            var text = new TokenValue(tokens).Text;
            return InitialLetterGrammar.TryParse(text, out _, out _) ? new InitialLetterValue(tokens) : null;
        }

        public IPropertyValue Construct(Property[] properties) => properties.Guard<InitialLetterValue>();

        private sealed class InitialLetterValue : IPropertyValue
        {
            public InitialLetterValue(IEnumerable<Token> tokens) => Original = new TokenValue(tokens);

            public string CssText => Original.Text;

            public TokenValue Original { get; }

            public TokenValue ExtractFor(string name) => Original;
        }
    }
}
