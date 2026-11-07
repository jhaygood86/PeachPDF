#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeachBrackets.Css
{
    /// <summary>
    /// CSS Borders 4 <c>&lt;corner-shape-value&gt;</c>: <c>round | scoop | bevel | notch | square | squircle |
    /// superellipse(&lt;number&gt; | infinity | -infinity)</c>. The grammar lives in <see cref="CornerShapeGrammar"/>,
    /// shared with the paint-side resolver so the two cannot disagree about what a value means.
    /// </summary>
    internal sealed class CornerShapeValueConverter : IValueConverter
    {
        public IPropertyValue Convert(IReadOnlyList<Token> value)
        {
            var text = CornerShapeGrammar.Normalize(value);
            return text is null ? null : new CornerShapeValue(text, value);
        }

        public IPropertyValue Construct(Property[] properties) => properties.Guard<CornerShapeValue>();

        private sealed class CornerShapeValue : IPropertyValue
        {
            public CornerShapeValue(string text, IReadOnlyList<Token> tokens)
            {
                CssText = text;
                Original = new TokenValue(tokens);
            }

            public string CssText { get; }

            public TokenValue Original { get; }

            public TokenValue ExtractFor(string name) => Original;
        }
    }

    /// <summary>The <c>&lt;corner-shape-value&gt;</c> grammar and its mapping onto the superellipse parameter K.</summary>
    internal static class CornerShapeGrammar
    {
        /// <summary>The keywords, each with the K it is defined to equal (css-borders-4 §3.7).</summary>
        private static readonly (string Keyword, double K)[] Keywords =
        {
            ("round", 1d),
            ("scoop", -1d),
            ("bevel", 0d),
            ("notch", double.NegativeInfinity),
            ("square", double.PositiveInfinity),
            ("squircle", 2d),
        };

        /// <summary>The initial value, <c>round</c>.</summary>
        public const string Initial = "round";

        /// <summary>The canonical text for the tokens (keyword lower-cased, <c>superellipse(K)</c> normalized), or null when invalid.</summary>
        public static string Normalize(IReadOnlyList<Token> tokens)
        {
            var only = tokens.OnlyOrDefault();
            if (only is { Type: TokenType.Ident } ident)
            {
                var text = ident.Data.ToString();
                foreach (var (keyword, _) in Keywords)
                    if (keyword.Equals(text, StringComparison.OrdinalIgnoreCase)) return keyword;
                return null;
            }

            if (only is { Type: TokenType.Function } function
                && function.Data.Equals("superellipse", StringComparison.OrdinalIgnoreCase))
            {
                var args = function.ArgumentTokens;
                Token? single = null;
                foreach (var arg in args)
                {
                    if (arg.Type == TokenType.Whitespace) continue;
                    if (single is not null) return null;
                    single = arg;
                }

                if (single is not { } token) return null;
                if (token.Type == TokenType.Number)
                    return "superellipse(" + ((double)token.Value).ToString("R", CultureInfo.InvariantCulture) + ")";
                if (token.Type == TokenType.Ident)
                {
                    var word = token.Data.ToString();
                    if (word.Equals("infinity", StringComparison.OrdinalIgnoreCase)) return "superellipse(infinity)";
                    if (word.Equals("-infinity", StringComparison.OrdinalIgnoreCase)) return "superellipse(-infinity)";
                }
            }

            return null;
        }

        /// <summary>
        /// The superellipse parameter K for a (canonical or authored) <c>&lt;corner-shape-value&gt;</c> string;
        /// <c>1</c> (round) for anything unparseable.
        /// </summary>
        public static double ParseK(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 1d;
            var text = value.Trim();

            foreach (var (keyword, k) in Keywords)
                if (keyword.Equals(text, StringComparison.OrdinalIgnoreCase)) return k;

            if (text.StartsWith("superellipse(", StringComparison.OrdinalIgnoreCase) && text.EndsWith(')'))
            {
                var arg = text.Substring(13, text.Length - 14).Trim();
                if (arg.Equals("infinity", StringComparison.OrdinalIgnoreCase)) return double.PositiveInfinity;
                if (arg.Equals("-infinity", StringComparison.OrdinalIgnoreCase)) return double.NegativeInfinity;
                if (double.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    && !double.IsNaN(number)) return number;
            }

            return 1d;
        }
    }
}
