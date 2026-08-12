#nullable disable

using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    /// <summary>
    /// Converter for css-content-3's <c>leader(&lt;leader-type&gt;?)</c>, where
    /// <c>&lt;leader-type&gt; = dotted | solid | space | &lt;string&gt;</c> - fills the remaining space
    /// on the line with a repeating pattern (the classic "Chapter One .......... 12" table-of-contents
    /// idiom). The argument is optional and defaults to <c>dotted</c> - <c>leader()</c> alone is the
    /// shortest spec-legal way to request a dotted leader. Layout of the fill itself happens post-flow,
    /// per line, in <c>CssLayoutEngine.ApplyLeaderFill</c> - this converter only validates/captures
    /// which pattern was requested.
    /// </summary>
    internal sealed class LeaderFunctionConverter : IValueConverter
    {
        public IPropertyValue Convert(IEnumerable<Token> value)
        {
            var first = value.OnlyOrDefault();

            if (first is not FunctionToken funcToken ||
                !funcToken.Data.Equals(FunctionNames.Leader, System.StringComparison.OrdinalIgnoreCase))
                return null;

            var args = funcToken.ArgumentTokens
                .Where(t => t.Type != TokenType.Comma && t.Type != TokenType.Whitespace)
                .ToArray();

            if (args.Length == 0) return new LeaderFunctionValue(Keywords.Dotted, null, value);
            if (args.Length != 1) return null;

            switch (args[0])
            {
                case KeywordToken keywordToken:
                    var kw = keywordToken.Data.ToLowerInvariant();
                    return kw is Keywords.Dotted or Keywords.Solid or Keywords.Space
                        ? new LeaderFunctionValue(kw, null, value)
                        : null;
                case StringToken stringToken:
                    return new LeaderFunctionValue(null, stringToken.Data, value);
                default:
                    return null;
            }
        }

        public IPropertyValue Construct(Property[] properties)
        {
            return properties.Guard<LeaderFunctionValue>();
        }

        private sealed class LeaderFunctionValue : IPropertyValue
        {
            public LeaderFunctionValue(string keyword, string customPattern, IEnumerable<Token> tokens)
            {
                Keyword = keyword;
                CustomPattern = customPattern;
                Original = new TokenValue(tokens);
            }

            // Original.Text (TokenValue.ToCss()) re-serializes the whole matched leader(...) call
            // verbatim - this is what CssBox.Content ends up holding after cascade, so it must include
            // the function wrapper, not just the bare keyword/string argument.
            public string CssText => Original.Text;

            public TokenValue Original { get; }

            public TokenValue ExtractFor(string name) => Original;

            /// <summary>One of <see cref="Keywords.Dotted"/>/<see cref="Keywords.Solid"/>/<see cref="Keywords.Space"/>, or null when <see cref="CustomPattern"/> is set.</summary>
            public string Keyword { get; }

            /// <summary>The literal repeating pattern for <c>leader("...")</c>, or null when <see cref="Keyword"/> is set.</summary>
            public string CustomPattern { get; }
        }
    }
}
