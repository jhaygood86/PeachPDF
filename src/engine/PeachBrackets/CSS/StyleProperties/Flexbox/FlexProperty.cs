#nullable disable

using System.Collections.Generic;

namespace PeachBrackets.Css
{
    internal sealed class FlexProperty : ShorthandProperty
    {
        private static readonly IValueConverter StyleConverter = Converters.FlexConverter;

        internal FlexProperty()
            : base(PropertyNames.Flex)
        { }

        internal override IValueConverter Converter => StyleConverter;
    }

    /// <summary>
    /// <c>flex: none</c> (CSS Flexbox 1 §7.1.1) is <c>0 0 auto</c>: the item neither grows nor shrinks. The keyword has no
    /// longhand of its own to land on, so each of the three is handed the value the keyword stands for (the shrink factor
    /// used to be left at its initial value of 1, so the item was shrunk like any other).
    /// </summary>
    internal sealed class FlexNoneConverter : IValueConverter
    {
        public IPropertyValue Convert(IReadOnlyList<Token> value)
        {
            var toks = new List<Token>();
            foreach (var t in value) if (t.Type != TokenType.Whitespace) toks.Add(t);
            return toks.Count == 1 && toks[0].Type == TokenType.Ident && toks[0].Data.Isi(Keywords.None) ? new NoneValue(value) : null;
        }

        public IPropertyValue Construct(Property[] properties) => null;

        private sealed class NoneValue : IPropertyValue
        {
            public NoneValue(IReadOnlyList<Token> tokens) => Original = new TokenValue(tokens);

            public string CssText => Original.Text;

            public TokenValue Original { get; }

            public TokenValue ExtractFor(string name) =>
                name.Is(PropertyNames.FlexGrow) || name.Is(PropertyNames.FlexShrink) ? TokenValue.FromNumber(0)
                : name.Is(PropertyNames.FlexBasis) ? TokenValue.FromString("auto")
                : null;
        }
    }
}
