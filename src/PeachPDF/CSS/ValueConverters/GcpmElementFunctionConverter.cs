#nullable disable

using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    /// <summary>
    /// Converter for css-gcpm-3's <c>element(&lt;custom-ident&gt; [, first | start | last | first-except]?)</c>
    /// <c>content</c> value - selects which occupant of a <c>position: running(name)</c> name (see
    /// <c>RunningFunctionConverter</c>) a page margin box displays. Named distinctly from CSS Images 4's
    /// unrelated <c>element(#id)</c> (<see cref="ElementImageConverter"/>, an <c>&lt;image&gt;</c> value
    /// snapshotting an element by hash-ident, never a live re-layout) to keep the two modules apart in
    /// code as well as name - the two are also structurally distinguishable by argument shape (a bare
    /// identifier here, a <c>#id</c> hash there), which is what actually keeps this converter from ever
    /// misparsing the Images-4 form.
    /// </summary>
    internal sealed class GcpmElementFunctionConverter : IValueConverter
    {
        public IPropertyValue Convert(IEnumerable<Token> value)
        {
            var first = value.OnlyOrDefault();

            if (first is not FunctionToken funcToken || !funcToken.Data.Isi(FunctionNames.Element))
                return null;

            var args = funcToken.ArgumentTokens
                .Where(t => t.Type != TokenType.Comma && t.Type != TokenType.Whitespace)
                .ToArray();

            // KeywordToken is also how a '#'-prefixed hash lexes (Type == TokenType.Hash) - the Images-4
            // element(#id) form - so the type check on top of the C# type pattern is what actually
            // disambiguates the two, not merely the different class each converter happens to check for.
            if (args.Length == 0 || args[0] is not KeywordToken { Type: TokenType.Ident } nameToken)
                return null;

            var name = nameToken.Data;
            var keyword = "first";

            if (args.Length > 1)
            {
                if (args[1] is not KeywordToken { Type: TokenType.Ident } keywordToken)
                    return null;

                var kw = keywordToken.Data.ToLowerInvariant();
                if (!GcpmSelectionKeywords.Values.Contains(kw))
                    return null;

                keyword = kw;
            }

            return new GcpmElementFunctionValue(name, keyword, value);
        }

        public IPropertyValue Construct(Property[] properties)
        {
            return properties.Guard<GcpmElementFunctionValue>();
        }

        private sealed class GcpmElementFunctionValue : IPropertyValue
        {
            private readonly string _name;
            private readonly string _keyword;

            public GcpmElementFunctionValue(string name, string keyword, IEnumerable<Token> tokens)
            {
                _name = name;
                _keyword = keyword;
                Original = new TokenValue(tokens);
            }

            public string CssText => _keyword == "first"
                ? $"element({_name})"
                : $"element({_name}, {_keyword})";

            public TokenValue Original { get; }

            public TokenValue ExtractFor(string name) => Original;

            public string Name => _name;
            public string Keyword => _keyword;
        }
    }
}
