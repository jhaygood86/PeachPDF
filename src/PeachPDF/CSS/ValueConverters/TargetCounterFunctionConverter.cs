#nullable disable

using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    /// <summary>
    /// Converter for css-content-3's <c>target-counter(&lt;target&gt;, &lt;counter-name&gt;
    /// [, &lt;counter-style&gt;]?)</c> - resolves a counter's value at another element, not the
    /// current one. <c>&lt;target&gt;</c> follows the same <c>&lt;string&gt; | url(#id) | attr(name)</c>
    /// house convention <c>-peachpdf-bookmark-target</c> already established (see
    /// <c>BookmarkOutlineBuilder.ResolveTargetValue</c>/<c>ResolveAttrTarget</c>) rather than the
    /// stricter formal grammar (<c>&lt;string&gt; | url()</c> only) - every real UA also accepts
    /// <c>attr(href)</c> in practice for anchor-based authoring, which is the common case for a
    /// hand-authored table of contents.
    /// </summary>
    internal sealed class TargetCounterFunctionConverter : IValueConverter
    {
        public IPropertyValue Convert(IEnumerable<Token> value)
        {
            var first = value.OnlyOrDefault();

            if (first is not FunctionToken funcToken ||
                !funcToken.Data.Equals(FunctionNames.TargetCounter, System.StringComparison.OrdinalIgnoreCase))
                return null;

            var args = funcToken.ArgumentTokens
                .Where(t => t.Type != TokenType.Comma && t.Type != TokenType.Whitespace)
                .ToArray();

            if (args.Length < 2 || !TargetGrammar.IsValidTarget(args[0]))
                return null;

            if (args[1] is not KeywordToken counterNameToken)
                return null;

            var style = Keywords.Decimal;
            if (args.Length > 2)
            {
                if (args[2] is not KeywordToken styleToken)
                    return null;

                style = styleToken.Data;
            }

            return new TargetCounterFunctionValue(counterNameToken.Data, style, value);
        }

        public IPropertyValue Construct(Property[] properties)
        {
            return properties.Guard<TargetCounterFunctionValue>();
        }

        private sealed class TargetCounterFunctionValue : IPropertyValue
        {
            public TargetCounterFunctionValue(string counterName, string style, IEnumerable<Token> tokens)
            {
                CounterName = counterName;
                Style = style;
                Original = new TokenValue(tokens);
            }

            // Original.Text (TokenValue.ToCss()) re-serializes the whole matched token span verbatim,
            // including the <target> argument (a string/url()/attr() this class doesn't otherwise keep a
            // parsed form of) - this is what CssBox.Content ends up holding after cascade (see
            // CssPropertyRegistry's cssom passthrough), so it must be a faithful round-trip, not a
            // canonicalized reconstruction that would silently drop the target argument CssContentEngine
            // needs to re-tokenize at resolution time.
            public string CssText => Original.Text;

            public TokenValue Original { get; }

            public TokenValue ExtractFor(string name) => Original;

            public string CounterName { get; }
            public string Style { get; }
        }
    }
}
