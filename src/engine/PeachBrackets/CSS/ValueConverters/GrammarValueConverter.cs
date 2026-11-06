#nullable disable

using System;
using System.Collections.Generic;

namespace PeachBrackets.Css
{
    /// <summary>
    /// A converter that accepts the tokens whose text a grammar function accepts, and keeps them as written, for a property that is stored as its own text and read by
    /// the render layer through that same grammar (<see cref="TimelineGrammar"/>).
    /// </summary>
    internal sealed class GrammarValueConverter : IValueConverter
    {
        private readonly Func<string, bool> _accepts;

        internal GrammarValueConverter(Func<string, bool> accepts) => _accepts = accepts;

        public IPropertyValue Convert(IReadOnlyList<Token> value)
        {
            var original = new TokenValue(value);
            return _accepts(original.ToText().Trim()) ? new TextValue(original) : null;
        }

        public IPropertyValue Construct(Property[] properties) => properties.Guard<TextValue>();

        private sealed class TextValue : IPropertyValue
        {
            public TextValue(TokenValue original) => Original = original;

            public string CssText => Original.ToText();

            public TokenValue Original { get; }

            public TokenValue ExtractFor(string name) => Original;
        }
    }
}
