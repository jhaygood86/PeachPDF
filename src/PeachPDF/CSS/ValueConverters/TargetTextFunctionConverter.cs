#nullable disable

using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    /// <summary>
    /// Converter for css-content-3's <c>target-text(&lt;target&gt;
    /// [, content | before | after | first-letter]?)</c> - pulls another element's text content.
    /// <c>&lt;target&gt;</c> shares <see cref="TargetGrammar"/> with
    /// <see cref="TargetCounterFunctionConverter"/>. The grammar accepts all four modes; only the
    /// default <c>content</c> mode is resolved to anything at runtime for v1 (see
    /// <c>CssContentEngine.ResolveTargetText</c>) - <c>before</c>/<c>after</c>/<c>first-letter</c> parse
    /// but currently resolve to empty, same as an unresolved target.
    /// </summary>
    internal sealed class TargetTextFunctionConverter : IValueConverter
    {
        private static readonly string[] ValidModes = { "content", "before", "after", "first-letter" };

        public IPropertyValue Convert(IEnumerable<Token> value)
        {
            var first = value.OnlyOrDefault();

            if (first is not FunctionToken funcToken ||
                !funcToken.Data.Equals(FunctionNames.TargetText, System.StringComparison.OrdinalIgnoreCase))
                return null;

            var args = funcToken.ArgumentTokens
                .Where(t => t.Type != TokenType.Comma && t.Type != TokenType.Whitespace)
                .ToArray();

            if (args.Length == 0 || !TargetGrammar.IsValidTarget(args[0]))
                return null;

            var mode = "content";
            if (args.Length > 1)
            {
                if (args[1] is not KeywordToken modeToken)
                    return null;

                var kw = modeToken.Data.ToLowerInvariant();
                if (!ValidModes.Contains(kw))
                    return null;

                mode = kw;
            }

            return new TargetTextFunctionValue(mode, value);
        }

        public IPropertyValue Construct(Property[] properties)
        {
            return properties.Guard<TargetTextFunctionValue>();
        }

        private sealed class TargetTextFunctionValue : IPropertyValue
        {
            public TargetTextFunctionValue(string mode, IEnumerable<Token> tokens)
            {
                Mode = mode;
                Original = new TokenValue(tokens);
            }

            // See TargetCounterFunctionConverter's identical CssText comment - Original.Text must be a
            // faithful round-trip (including the <target> argument) since it's what CssBox.Content ends
            // up holding after cascade.
            public string CssText => Original.Text;

            public TokenValue Original { get; }

            public TokenValue ExtractFor(string name) => Original;

            public string Mode { get; }
        }
    }
}
