#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Css
{
    /// <summary>
    /// Accepts a value when a shared grammar (<see cref="TextEmphasisGrammar"/>) accepts its tokens, and keeps
    /// the authored text so the painter re-parses the same value - the <see cref="HangingPunctuationValueConverter"/> precedent.
    /// </summary>
    internal sealed class TokenGrammarValueConverter : IValueConverter
    {
        private readonly Func<IReadOnlyList<Token>, bool> _accepts;

        internal TokenGrammarValueConverter(Func<IReadOnlyList<Token>, bool> accepts)
        {
            _accepts = accepts;
        }

        public IPropertyValue Convert(IReadOnlyList<Token> value)
        {
            var tokens = value.ToArray();
            return _accepts(tokens) ? new GrammarValue(tokens) : null;
        }

        public IPropertyValue Construct(Property[] properties) => properties.Guard<GrammarValue>();

        private sealed class GrammarValue : IPropertyValue
        {
            public GrammarValue(IEnumerable<Token> tokens)
            {
                Original = new TokenValue(tokens);
            }

            public string CssText => Original.Text;

            public TokenValue Original { get; }

            public TokenValue ExtractFor(string name) => Original;
        }
    }
}
