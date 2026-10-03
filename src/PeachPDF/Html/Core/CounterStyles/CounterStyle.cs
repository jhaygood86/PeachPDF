#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using PeachPDF.CSS;
using PeachPDF.Html.Core.Parse;

namespace PeachPDF.Html.Core.CounterStyles
{
    /// <summary>
    /// An author-defined counter style (CSS Counter Styles Level 3): either an <c>@counter-style</c> rule or
    /// an inline <c>symbols()</c> value. Descriptors the author did not give are <c>null</c> so an
    /// <c>extends</c> style can tell "unspecified, inherit it" from "specified".
    /// </summary>
    internal sealed class CounterStyle
    {
        public CounterSystem System { get; init; } = CounterSystem.Symbolic;

        /// <summary>The style named by <c>extends</c> (only for <see cref="CounterSystem.Extends"/>).</summary>
        public string? ExtendsName { get; init; }

        public IReadOnlyList<string> Symbols { get; init; } = [];

        public IReadOnlyList<(int Weight, string Symbol)> AdditiveSymbols { get; init; } = [];

        /// <summary>The first symbol's counter value for <c>fixed</c> (default 1).</summary>
        public int FixedFirst { get; init; } = 1;

        public (string Prefix, string Suffix)? Negative { get; init; }

        /// <summary>The <c>range</c> descriptor's bounds, or null for <c>auto</c>.</summary>
        public IReadOnlyList<(long Lower, long Upper)>? Range { get; init; }

        public (int Length, string Symbol)? Pad { get; init; }

        public string? Fallback { get; init; }

        public string? Prefix { get; init; }

        public string? Suffix { get; init; }

        /// <summary>Builds the style a <c>symbols()</c> function value describes, or null when it is invalid.</summary>
        public static CounterStyle? FromSymbolsFunction(Token function)
        {
            return CounterStyleGrammar.TryParseSymbolsFunction(function, out var system, out var symbols)
                ? new CounterStyle { System = system, Symbols = symbols }
                : null;
        }

        /// <summary>
        /// Builds the style an <c>@counter-style</c> rule describes, or null when the rule is invalid (an
        /// unusable <c>system</c>, or a symbol list the system cannot work with) and so must be ignored (§3).
        /// An individually invalid optional descriptor is dropped back to unspecified instead.
        /// </summary>
        public static CounterStyle? FromRule(ICounterStyleRule rule)
        {
            var system = CounterSystem.Symbolic;
            string? extendsName = null;
            var fixedFirst = 1;

            var systemTokens = Tokenize(rule.GetDescriptor("system"));
            if (systemTokens.Count > 0)
            {
                if (systemTokens[0].Type != TokenType.Ident) return null;
                var keyword = systemTokens[0].Data.ToString();

                if (keyword.Equals("extends", StringComparison.OrdinalIgnoreCase))
                {
                    if (systemTokens.Count != 2 || systemTokens[1].Type != TokenType.Ident) return null;
                    system = CounterSystem.Extends;
                    extendsName = systemTokens[1].Data.ToString();
                }
                else if (CounterStyleGrammar.TryParseSystemKeyword(keyword, out var parsed))
                {
                    system = parsed;

                    if (parsed == CounterSystem.Fixed && systemTokens.Count == 2)
                    {
                        if (systemTokens[1] is not { Type: TokenType.Number, IsInteger: true } first) return null;
                        fixedFirst = first.IntegerValue;
                    }
                    else if (systemTokens.Count != 1)
                    {
                        return null;
                    }
                }
                else
                {
                    return null;
                }
            }

            var symbols = CounterStyleGrammar.ParseSymbolList(Tokenize(rule.GetDescriptor("symbols"))) ?? [];
            var additive = ParseAdditiveSymbols(Tokenize(rule.GetDescriptor("additive-symbols")));

            // §2.1: each system's minimum symbol count; a rule that cannot meet it is invalid.
            var usable = system switch
            {
                CounterSystem.Cyclic or CounterSystem.Symbolic or CounterSystem.Fixed => symbols.Count >= 1,
                CounterSystem.Numeric or CounterSystem.Alphabetic => symbols.Count >= 2,
                CounterSystem.Additive => additive.Count >= 1,
                _ => true,
            };

            if (!usable) return null;

            return new CounterStyle
            {
                System = system,
                ExtendsName = extendsName,
                Symbols = symbols,
                AdditiveSymbols = additive,
                FixedFirst = fixedFirst,
                Negative = ParseNegative(Tokenize(rule.GetDescriptor("negative"))),
                Range = ParseRange(Tokenize(rule.GetDescriptor("range"))),
                Pad = ParsePad(Tokenize(rule.GetDescriptor("pad"))),
                Fallback = ParseIdent(Tokenize(rule.GetDescriptor("fallback"))),
                Prefix = ParseSingleSymbol(Tokenize(rule.GetDescriptor("prefix"))),
                Suffix = ParseSingleSymbol(Tokenize(rule.GetDescriptor("suffix"))),
            };
        }

        private static List<Token> Tokenize(string text)
        {
            using var pooled = CssValueParser.GetCssTokensPooled(text);
            List<Token> tokens = pooled;

            // The pooled list must not outlive its block, so copy out (Token is a value type).
            return [.. tokens];
        }

        private static string? ParseIdent(List<Token> tokens) =>
            tokens is [{ Type: TokenType.Ident } ident] ? ident.Data.ToString() : null;

        private static string? ParseSingleSymbol(List<Token> tokens) =>
            CounterStyleGrammar.ParseSymbolList(tokens) is [var symbol] ? symbol : null;

        private static (string, string)? ParseNegative(List<Token> tokens) =>
            CounterStyleGrammar.ParseSymbolList(tokens) switch
            {
                [var prefix] => (prefix, string.Empty),
                [var prefix, var suffix] => (prefix, suffix),
                _ => null,
            };

        private static (int, string)? ParsePad(List<Token> tokens)
        {
            if (tokens.Count != 2) return null;

            var number = tokens.FirstOrDefault(t => t.Type == TokenType.Number);
            var symbol = tokens.FirstOrDefault(t => t.Type is TokenType.String or TokenType.Ident);

            if (number.Type != TokenType.Number || !number.IsInteger || number.IntegerValue < 0) return null;
            if (symbol.Type is not (TokenType.String or TokenType.Ident)) return null;

            return (number.IntegerValue, symbol.Data.ToString());
        }

        private static List<(long, long)>? ParseRange(List<Token> tokens)
        {
            if (tokens.Count == 0 || tokens is [{ Type: TokenType.Ident } auto] && auto.Data.Isi("auto")) return null;

            var ranges = new List<(long, long)>();
            var bounds = new List<long>();

            foreach (var token in tokens)
            {
                switch (token)
                {
                    case { Type: TokenType.Comma }:
                        if (bounds.Count != 2 || bounds[0] > bounds[1]) return null;
                        ranges.Add((bounds[0], bounds[1]));
                        bounds.Clear();
                        break;
                    case { Type: TokenType.Number, IsInteger: true }:
                        bounds.Add(token.IntegerValue);
                        break;
                    case { Type: TokenType.Ident } when token.Data.Isi("infinite"):
                        // The first bound infinite is -infinity, the second +infinity.
                        bounds.Add(bounds.Count == 0 ? long.MinValue : long.MaxValue);
                        break;
                    default:
                        return null;
                }
            }

            if (bounds.Count != 2 || bounds[0] > bounds[1]) return null;
            ranges.Add((bounds[0], bounds[1]));
            return ranges;
        }

        private static List<(int, string)> ParseAdditiveSymbols(List<Token> tokens)
        {
            var result = new List<(int, string)>();
            if (tokens.Count == 0) return result;

            var tuple = new List<Token>();
            tokens.Add(Token.Comma);

            foreach (var token in tokens)
            {
                if (token.Type != TokenType.Comma)
                {
                    tuple.Add(token);
                    continue;
                }

                var number = tuple.FirstOrDefault(t => t.Type == TokenType.Number);
                var symbol = tuple.FirstOrDefault(t => t.Type is TokenType.String or TokenType.Ident);
                tuple.Clear();

                if (number.Type != TokenType.Number || !number.IsInteger || number.IntegerValue < 0 ||
                    symbol.Type is not (TokenType.String or TokenType.Ident))
                {
                    return [];
                }

                // §2.7: tuples must be in strictly descending weight order, or the descriptor is invalid.
                if (result.Count > 0 && number.IntegerValue >= result[^1].Item1) return [];

                result.Add((number.IntegerValue, symbol.Data.ToString()));
            }

            return result;
        }
    }
}
