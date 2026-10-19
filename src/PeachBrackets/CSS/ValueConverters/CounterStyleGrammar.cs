#nullable enable

using System;
using System.Collections.Generic;

namespace PeachPDF.CSS
{
    /// <summary>The <c>system</c> descriptor's algorithm (CSS Counter Styles Level 3 §2.1).</summary>
    internal enum CounterSystem
    {
        Cyclic,
        Numeric,
        Alphabetic,
        Symbolic,
        Fixed,
        Additive,
        Extends,
    }

    /// <summary>
    /// The one grammar for the symbol-bearing parts of CSS Counter Styles Level 3, shared by the CSS-OM
    /// converter that validates <c>list-style-type: symbols(...)</c> and by the Layer-B code that turns an
    /// <c>@counter-style</c> rule or a <c>symbols()</c> value into a counter style - so the two cannot
    /// drift apart.
    /// </summary>
    internal static class CounterStyleGrammar
    {
        /// <summary>
        /// Parses a whitespace-separated list of <c>&lt;symbol&gt;</c> (<c>&lt;string&gt;</c> or
        /// <c>&lt;custom-ident&gt;</c>) into their text; <c>null</c> when empty or holding anything else
        /// (an <c>&lt;image&gt;</c> symbol is not supported and invalidates the list).
        /// </summary>
        internal static List<string>? ParseSymbolList(IEnumerable<Token> tokens)
        {
            var symbols = new List<string>();

            foreach (var token in tokens)
            {
                switch (token.Type)
                {
                    case TokenType.Whitespace:
                        continue;
                    case TokenType.String:
                    case TokenType.Ident:
                        symbols.Add(token.Data.ToString());
                        break;
                    default:
                        return null;
                }
            }

            return symbols.Count > 0 ? symbols : null;
        }

        /// <summary>Maps a <c>system</c> keyword to its algorithm (excluding <c>extends</c>/<c>fixed &lt;integer&gt;</c> forms).</summary>
        internal static bool TryParseSystemKeyword(string keyword, out CounterSystem system)
        {
            switch (keyword.ToLowerInvariant())
            {
                case "cyclic": system = CounterSystem.Cyclic; return true;
                case "numeric": system = CounterSystem.Numeric; return true;
                case "alphabetic": system = CounterSystem.Alphabetic; return true;
                case "symbolic": system = CounterSystem.Symbolic; return true;
                case "fixed": system = CounterSystem.Fixed; return true;
                case "additive": system = CounterSystem.Additive; return true;
                default: system = default; return false;
            }
        }

        /// <summary>
        /// Parses a <c>symbols( &lt;symbols-type&gt;? [ &lt;string&gt; | &lt;custom-ident&gt; ]+ )</c> function
        /// (§3.9). <c>symbols-type</c> defaults to <c>symbolic</c>; <c>additive</c> is not allowed, and
        /// <c>numeric</c>/<c>alphabetic</c> need at least two symbols.
        /// </summary>
        internal static bool TryParseSymbolsFunction(Token function, out CounterSystem system, out List<string> symbols)
        {
            system = CounterSystem.Symbolic;
            symbols = [];

            if (function.Type != TokenType.Function || !function.Data.Isi(FunctionNames.Symbols)) return false;

            var arguments = new List<Token>();
            foreach (var token in function.ArgumentTokens)
            {
                if (token.Type != TokenType.Whitespace) arguments.Add(token);
            }

            var first = 0;
            if (arguments.Count > 0 && arguments[0].Type == TokenType.Ident
                && TryParseSystemKeyword(arguments[0].Data.ToString(), out var parsedSystem)
                && parsedSystem != CounterSystem.Additive)
            {
                // An ident that names a system is the type, not a symbol - "symbols(cyclic ...)" can never
                // mean a cyclic symbol called "cyclic", exactly as the spec's grammar reads.
                system = parsedSystem;
                first = 1;
            }

            var parsed = ParseSymbolList(arguments.GetRange(first, arguments.Count - first));
            if (parsed is null) return false;

            if (system is CounterSystem.Numeric or CounterSystem.Alphabetic && parsed.Count < 2) return false;

            symbols = parsed;
            return true;
        }
    }
}
