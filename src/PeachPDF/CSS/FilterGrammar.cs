#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    /// <summary>
    /// Shared, layer-agnostic grammar for the CSS <c>filter</c> value (Filter Effects Level 1 §3): the
    /// keyword <c>none</c>, or a <b>space</b>-separated list of <c>&lt;filter-function&gt;</c> values.
    /// Unlike <see cref="BoxShadowGrammar"/>'s comma-separated shadow layers, a filter value has no
    /// separator token at all between functions - each top-level <see cref="Token"/> other than
    /// whitespace is itself one complete <see cref="TokenType.Function"/> token (with its own nested
    /// <see cref="Token.ArgumentTokens"/>), so there is no comma-splitting step here.
    /// <para>
    /// All nine standard filter functions (<c>blur</c>, <c>brightness</c>, <c>contrast</c>,
    /// <c>drop-shadow</c>, <c>grayscale</c>, <c>hue-rotate</c>, <c>invert</c>, <c>opacity</c>,
    /// <c>saturate</c>, <c>sepia</c>) are accepted at the grammar level, so e.g.
    /// <c>filter: grayscale(50%) opacity(0.8)</c> is not rejected wholesale just because one function has
    /// no native PDF rendering path today - see <c>ColorMatrix</c>'s remarks for exactly which functions
    /// that is and why. Which functions actually paint anything is a paint-time decision
    /// (<c>PeachPDF.Html.Core.Paint.FilterEffectResolver</c>), not a parse-time one.
    /// </para>
    /// <para>
    /// Like <see cref="BoxShadowGrammar"/>, each function's arguments are captured as raw authored
    /// component strings, never pre-resolved numbers - Layer A only needs to accept/reject and preserve
    /// text, and <c>drop-shadow()</c>'s lengths specifically need box-relative resolution Layer A can't do.
    /// </para>
    /// </summary>
    internal static class FilterGrammar
    {
        private const string Blur = "blur";
        private const string Brightness = "brightness";
        private const string Contrast = "contrast";
        private const string DropShadow = "drop-shadow";
        private const string Grayscale = "grayscale";
        private const string HueRotate = "hue-rotate";
        private const string Invert = "invert";
        private const string Opacity = "opacity";
        private const string Saturate = "saturate";
        private const string Sepia = "sepia";

        private static readonly HashSet<string> KnownFunctionNames = new(StringComparer.OrdinalIgnoreCase)
        {
            Blur, Brightness, Contrast, DropShadow, Grayscale, HueRotate, Invert, Opacity, Saturate, Sepia,
        };

        /// <summary>
        /// One parsed <c>&lt;filter-function&gt;</c>. <see cref="Name"/> is the lowercased function name.
        /// <see cref="Arguments"/> holds each argument's raw authored text in source order - empty when the
        /// function was authored with no arguments at all (every native single-argument function accepts
        /// omission, defaulting per Filter Effects 1 §3's own per-function defaults), except
        /// <see cref="DropShadow"/>, whose <see cref="Arguments"/> is always exactly four entries:
        /// offset-x, offset-y, blur ("0" if omitted) and color ("" if omitted, meaning "use the element's
        /// own color" - <c>currentColor</c>, per CSS Backgrounds 3 §5's shadow-color default that
        /// <c>drop-shadow()</c> also follows).
        /// </summary>
        internal sealed class FilterFunction
        {
            public string Name { get; init; }
            public IReadOnlyList<string> Arguments { get; init; }
        }

        /// <summary>
        /// Parses a <c>filter</c> value's tokens into an ordered list of <see cref="FilterFunction"/>s, or
        /// null when the value isn't a valid filter list. The literal keyword <c>none</c> returns an
        /// <b>empty list</b> (no filters), distinct from a null (invalid) result - matching
        /// <see cref="BoxShadowGrammar.TryParse"/>'s own none-vs-invalid convention.
        /// </summary>
        internal static List<FilterFunction> TryParse(IReadOnlyList<Token> tokens)
        {
            var significant = tokens.Where(t => t.Type != TokenType.Whitespace).ToList();

            if (significant.Count == 0) return null;

            if (significant is [{ Type: TokenType.Ident } ident] && ident.Data.Isi(Keywords.None))
                return [];

            var functions = new List<FilterFunction>();

            foreach (var token in significant)
            {
                if (token.Type != TokenType.Function) return null;

                var name = token.Data.ToString();
                if (!KnownFunctionNames.Contains(name)) return null;

                name = name.ToLowerInvariant();

                if (!TryParseArguments(name, token.ArgumentTokens, out var arguments)) return null;

                functions.Add(new FilterFunction { Name = name, Arguments = arguments });
            }

            return functions;
        }

        private static bool TryParseArguments(string name, IReadOnlyList<Token> argumentTokens, out List<string> arguments)
        {
            if (name == DropShadow)
                return TryParseDropShadowArguments(argumentTokens, out arguments);

            var significant = argumentTokens.Where(t => t.Type != TokenType.Whitespace).ToList();

            if (significant.Count == 0)
            {
                arguments = [];
                return true;
            }

            if (significant.Count != 1)
            {
                arguments = null;
                return false;
            }

            var token = significant[0];
            var valid = name switch
            {
                HueRotate => IsAngle(token),
                Blur => IsNonNegativeLength(token),
                _ => IsNonNegativeNumberOrPercentage(token), // brightness/contrast/grayscale/invert/opacity/saturate/sepia
            };

            arguments = valid ? [token.ToValue()] : null;
            return valid;
        }

        /// <summary>
        /// <c>drop-shadow(&lt;color&gt;? &amp;&amp; &lt;length&gt;{2,3})</c> (Filter Effects 1 §3.5.4) -
        /// the same trailing-group shape as <c>box-shadow</c> minus <c>inset</c> and <c>spread</c>, so this
        /// reuses <see cref="BoxShadowGrammar.TryClassify"/> rather than a second, independently-derived
        /// classifier (CLAUDE.md's "one grammar per value shape" rule).
        /// </summary>
        private static bool TryParseDropShadowArguments(IReadOnlyList<Token> argumentTokens, out List<string> arguments)
        {
            arguments = null;

            var normalized = BoxShadowGrammar.NormalizeHexColorTokens(
                argumentTokens.Where(t => t.Type != TokenType.Whitespace).ToArray());

            if (!BoxShadowGrammar.TryClassify(normalized, allowInset: false, out _, out var lengths, out var colorTokens))
                return false;

            // offset-x, offset-y, [blur-radius] - no spread, no inset (unlike box-shadow).
            if (lengths.Count is < 2 or > 3) return false;
            if (lengths.Count == 3 && BoxShadowGrammar.LengthValue(lengths[2]) < 0) return false;

            var color = "";
            if (colorTokens.Count > 0)
            {
                if (!BoxShadowGrammar.IsValidColor(colorTokens)) return false;
                color = string.Concat(colorTokens.Select(t => t.ToValue()));
            }

            arguments =
            [
                lengths[0].ToValue(),
                lengths[1].ToValue(),
                lengths.Count == 3 ? lengths[2].ToValue() : "0",
                color,
            ];
            return true;
        }

        /// <summary>Reuses <see cref="Angle"/>'s own parser rather than re-deriving deg/grad/rad/turn unit
        /// recognition here - the unitless-zero exception (CSS Values 4 §6.1) is the one case
        /// <see cref="Angle.TryParse"/> doesn't cover, since it never sees a bare <see cref="TokenType.Number"/> token.</summary>
        private static bool IsAngle(Token token) => token.Type switch
        {
            TokenType.Dimension => Angle.TryParse(token.ToValue(), out _),
            TokenType.Number => token.Value == 0f,
            _ => false,
        };

        private static bool IsNonNegativeLength(Token token) =>
            BoxShadowGrammar.IsLength(token) && BoxShadowGrammar.LengthValue(token) >= 0;

        private static bool IsNonNegativeNumberOrPercentage(Token token) => token switch
        {
            { Type: TokenType.Number, Value: >= 0f } => true,
            { Type: TokenType.Percentage, Value: >= 0f } => true,
            _ => false,
        };
    }
}
