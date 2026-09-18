#nullable disable

using System.Collections.Generic;

namespace PeachPDF.CSS
{
    /// <summary>
    /// Converter for <c>text-underline-position</c>'s full css-text-decor-4 §2.5 compound grammar
    /// (<c>auto | [ from-font | under ] || [ left | right ]</c>), replacing a plain single-keyword map
    /// converter now that the property accepts up to two keywords together (issue #1146). Shares
    /// <see cref="TextUnderlinePositionGrammar"/> with the generated <c>css-properties.json</c>
    /// validator/setter, so the two never independently re-derive the grammar - the
    /// <see cref="RunningFunctionConverter"/>/<see cref="PositionValueGrammar"/> precedent.
    /// </summary>
    internal sealed class TextUnderlinePositionCompoundConverter : IValueConverter
    {
        public IPropertyValue Convert(IReadOnlyList<Token> value)
        {
            return TextUnderlinePositionGrammar.TryParse(value, out var position, out var side)
                ? new TextUnderlinePositionValue(position, side, value)
                : null;
        }

        public IPropertyValue Construct(Property[] properties)
        {
            return properties.Guard<TextUnderlinePositionValue>();
        }

        private sealed class TextUnderlinePositionValue : IPropertyValue
        {
            private readonly TextUnderlinePosition _position;
            private readonly TextUnderlineSide _side;

            public TextUnderlinePositionValue(TextUnderlinePosition position, TextUnderlineSide side, IEnumerable<Token> tokens)
            {
                _position = position;
                _side = side;
                Original = new TokenValue(tokens);
            }

            public string CssText => TextUnderlinePositionGrammar.CanonicalCssText(_position, _side);

            public TokenValue Original { get; }

            public TokenValue ExtractFor(string name) => Original;
        }
    }
}
