#nullable disable

using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    /// <summary>
    /// css-text-3 §6.1's real shorthand over <c>text-align-all</c> (<see cref="TextAlignAllProperty"/>,
    /// §6.2) and <c>text-align-last</c> (<see cref="TextAlignLastProperty"/>, §6.3). A plain value sets
    /// <c>text-align-all</c> and - per <see cref="ShorthandProperty.Export"/>'s existing "an omitted
    /// longhand resets to its initial value" rule - leaves <c>text-align-last</c> untagged so it resets
    /// to <c>auto</c>; <c>justify-all</c> sets both to <c>justify</c>; <c>match-parent</c> sets both to
    /// <c>match-parent</c>.
    /// </summary>
    internal sealed class TextAlignProperty : ShorthandProperty
    {
        private static readonly IValueConverter StyleConverter = new TextAlignShorthandConverter();

        internal TextAlignProperty()
            : base(PropertyNames.TextAlign, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;

        private sealed class TextAlignShorthandConverter : IValueConverter
        {
            private static readonly TokenValue JustifyTokens = TokenValue.FromString(Keywords.Justify);

            private static readonly IValueConverter TextAlignAllOnly =
                Converters.HorizontalAlignmentConverter.For(PropertyNames.TextAlignAll);

            public IPropertyValue Convert(IReadOnlyList<Token> value)
            {
                if (value.Is(Keywords.JustifyAll)) return new BothLonghandsValue(Keywords.JustifyAll, JustifyTokens);
                if (value.Is(Keywords.MatchParent)) return new BothLonghandsValue(Keywords.MatchParent, new TokenValue(value));

                return TextAlignAllOnly.Convert(value);
            }

            public IPropertyValue Construct(Property[] properties)
            {
                var all = properties.FirstOrDefault(p => p.Name.Is(PropertyNames.TextAlignAll));
                var last = properties.FirstOrDefault(p => p.Name.Is(PropertyNames.TextAlignLast));
                if (all is null || last is null || !all.HasValue) return null;

                if (all.Value.Is(Keywords.MatchParent) && last.Value.Is(Keywords.MatchParent))
                    return new IdentityValue(Keywords.MatchParent);

                if (all.Value.Is(Keywords.Justify) && last.Value.Is(Keywords.Justify))
                    return new IdentityValue(Keywords.JustifyAll);

                // A longhand ShorthandProperty.Export reset via TrySetValue(null) ends up with a
                // DeclaredValue that literally serializes as "initial" (Property.Value's own fallback
                // for the CSS-wide keyword, not text-align-last's real default text) - not "auto" - so
                // both have to be treated as "at its initial state" here.
                return last.IsInitial || last.Value.Is(Keywords.Auto) ? new IdentityValue(all.Value) : null;
            }

            /// <summary>The value <c>justify-all</c>/<c>match-parent</c> produce - the same raw tokens tagged for both longhand names.</summary>
            private sealed class BothLonghandsValue : IPropertyValue
            {
                private readonly TokenValue _tokens;

                public BothLonghandsValue(string cssText, TokenValue tokens)
                {
                    CssText = cssText;
                    _tokens = tokens;
                }

                public string CssText { get; }

                public TokenValue Original => _tokens;

                public TokenValue ExtractFor(string name) =>
                    name.Is(PropertyNames.TextAlignAll) || name.Is(PropertyNames.TextAlignLast) ? _tokens : null;
            }

            /// <summary>Serializes back to a single keyword - used only by <see cref="Construct"/>, the reverse (Stringify) direction.</summary>
            private sealed class IdentityValue : IPropertyValue
            {
                public IdentityValue(string cssText) => CssText = cssText;

                public string CssText { get; }

                public TokenValue Original => TokenValue.FromString(CssText);

                public TokenValue ExtractFor(string name) => Original;
            }
        }
    }
}
