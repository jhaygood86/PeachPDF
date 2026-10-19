#nullable disable

using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    /// <summary>
    /// CSS Fonts 4 §3.5 <c>font-synthesis</c>: <c>none | [ weight || style || small-caps || position ]</c>. A longhand
    /// the value names is <c>auto</c>; every one it does not is <c>none</c> (so <c>font-synthesis: weight</c> still
    /// forbids synthetic italic) - which is why this needs its own value type instead of
    /// <see cref="ShorthandProperty.Export"/>'s "omitted longhand resets to its initial value" rule, which would
    /// reset an omitted one to <c>auto</c>.
    /// </summary>
    internal sealed class FontSynthesisProperty : ShorthandProperty
    {
        private static readonly IValueConverter StyleConverter = new FontSynthesisShorthandConverter().OrGlobalValue();

        internal FontSynthesisProperty()
            : base(PropertyNames.FontSynthesis, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;

        private sealed class FontSynthesisShorthandConverter : IValueConverter
        {
            private static readonly string[] Names =
            [
                PropertyNames.FontSynthesisWeight,
                PropertyNames.FontSynthesisStyle,
                PropertyNames.FontSynthesisSmallCaps,
                PropertyNames.FontSynthesisPosition,
            ];

            private static readonly string[] Keys = ["weight", "style", "small-caps", "position"];

            public IPropertyValue Convert(IReadOnlyList<Token> value)
            {
                var toks = value.Where(t => t.Type != TokenType.Whitespace).ToArray();
                if (toks.Length is 0 or > 4) return null;

                var enabled = new bool[4];

                if (toks.Length == 1 && toks[0] is { Type: TokenType.Ident } only && only.Data.Isi(Keywords.None))
                    return new SynthesisValue(Keywords.None, enabled);

                foreach (var token in toks)
                {
                    if (token.Type != TokenType.Ident) return null;

                    var index = -1;
                    for (var i = 0; i < Keys.Length; i++)
                    {
                        if (token.Data.Isi(Keys[i])) index = i;
                    }

                    if (index < 0 || enabled[index]) return null;
                    enabled[index] = true;
                }

                var text = string.Join(" ", Keys.Where((_, i) => enabled[i]));
                return new SynthesisValue(text, enabled);
            }

            public IPropertyValue Construct(Property[] properties)
            {
                var enabled = new bool[4];
                for (var i = 0; i < Names.Length; i++)
                {
                    var property = properties.FirstOrDefault(p => p.Name.Is(Names[i]));
                    if (property is null || !property.HasValue) return null;
                    enabled[i] = property.IsInitial || !property.Value.Is(Keywords.None);
                }

                var text = enabled.Any(e => e) ? string.Join(" ", Keys.Where((_, i) => enabled[i])) : Keywords.None;
                return new SynthesisValue(text, enabled);
            }

            private sealed class SynthesisValue : IPropertyValue
            {
                private readonly bool[] _enabled;

                public SynthesisValue(string cssText, bool[] enabled)
                {
                    CssText = cssText;
                    _enabled = enabled;
                    Original = TokenValue.FromString(cssText);
                }

                public string CssText { get; }

                public TokenValue Original { get; }

                public TokenValue ExtractFor(string name)
                {
                    for (var i = 0; i < Names.Length; i++)
                    {
                        if (name.Is(Names[i])) return TokenValue.FromString(_enabled[i] ? Keywords.Auto : Keywords.None);
                    }

                    return null;
                }
            }
        }
    }
}
