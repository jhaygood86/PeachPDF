#nullable disable

using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    /// <summary>
    /// Validates a <c>hyphenate-limit-chars</c> value through the shared <see cref="HyphenateLimitCharsGrammar"/>.
    /// The authored text is preserved so Layer B (<see cref="Html.Core.Dom.CssLayoutEngine"/>) re-parses the
    /// same value during layout — the <see cref="AspectRatioValueConverter"/> precedent.
    /// </summary>
    internal sealed class HyphenateLimitCharsValueConverter : IValueConverter
    {
        public IPropertyValue Convert(IEnumerable<Token> value)
        {
            var tokens = value.ToArray();
            return HyphenateLimitCharsGrammar.TryParse(tokens, out _, out _, out _) ? new HyphenateLimitCharsValue(tokens) : null;
        }

        public IPropertyValue Construct(Property[] properties)
        {
            return properties.Guard<HyphenateLimitCharsValue>();
        }

        private sealed class HyphenateLimitCharsValue : IPropertyValue
        {
            public HyphenateLimitCharsValue(IEnumerable<Token> tokens)
            {
                Original = new TokenValue(tokens);
            }

            public string CssText => Original.Text;

            public TokenValue Original { get; }

            public TokenValue ExtractFor(string name) => Original;
        }
    }
}
