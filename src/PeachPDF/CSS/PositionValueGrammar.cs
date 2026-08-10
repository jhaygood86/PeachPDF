#nullable disable

using System.Collections.Generic;
using System.Linq;
using PeachPDF.Html.Core.Parse;

namespace PeachPDF.CSS
{
    /// <summary>
    /// Grammar for the <c>position</c> property's value: a plain keyword (static/relative/absolute/
    /// fixed/sticky) or css-gcpm-3's <c>running(&lt;custom-ident&gt;)</c> function. Shared by the CSS-OM
    /// <c>PositionProperty</c> converter (<see cref="RunningFunctionConverter"/>) and the generated
    /// <c>position</c> validator/setter in css-properties.json, so the two never independently re-derive
    /// this grammar.
    /// </summary>
    internal static class PositionValueGrammar
    {
        /// <summary>
        /// Token-level match for <c>running(&lt;custom-ident&gt;)</c>, shared by <see cref="TryParse"/>
        /// (which tokenizes a raw string first) and <see cref="RunningFunctionConverter"/> (which already
        /// has tokens from the CSS-OM parser).
        /// </summary>
        internal static bool TryParseRunningTokens(IEnumerable<Token> tokens, out string name)
        {
            name = null;

            if (tokens.OnlyOrDefault() is not FunctionToken fn || !fn.Data.Isi(FunctionNames.Running))
                return false;

            var args = fn.ArgumentTokens
                .Where(t => t.Type != TokenType.Whitespace && t.Type != TokenType.Comma)
                .ToArray();

            // KeywordToken is also how a '#'-prefixed hash lexes (Type == TokenType.Hash - see
            // KeywordToken.ToValue()'s own "#" + Data case), so the type check must be on top of the
            // C# type pattern - otherwise running(#foo) would wrongly accept "foo" as the custom-ident.
            if (args is not [KeywordToken { Type: TokenType.Ident } nameToken])
                return false;

            name = nameToken.Data;
            return true;
        }

        /// <summary>
        /// Resolves a raw authored <c>position</c> value to its <see cref="PositionMode"/> and (for
        /// <c>running()</c>) the running-element name - the shape css-properties.json's <c>position</c>
        /// entry needs for its customValidator/customSetter, since those only see the raw string.
        /// </summary>
        internal static bool TryParse(string value, out PositionMode mode, out string runningName)
        {
            if (Map.PositionModes.TryGetValue(value.Trim(), out mode))
            {
                runningName = null;
                return true;
            }

            if (TryParseRunningTokens(CssValueParser.GetCssTokens(value), out runningName))
            {
                mode = PositionMode.Running;
                return true;
            }

            mode = default;
            runningName = null;
            return false;
        }

        /// <summary>
        /// The canonical <c>CssProperty&lt;PositionMode&gt;.ToString()</c> text for a successfully-parsed
        /// <paramref name="value"/> - a plain keyword is trimmed/lowercased (matching
        /// <see cref="CssProperty{T}.FromCssText"/>'s own canonicalization, issue #598), while
        /// <paramref name="runningName"/> (case-sensitive, per &lt;custom-ident&gt;) is re-serialized
        /// through the same canonical <c>running(name)</c> shape <see cref="RunningFunctionConverter"/>
        /// already produces at the CSS-OM layer, rather than keeping whatever whitespace/casing the
        /// function name itself was authored with.
        /// </summary>
        internal static string CanonicalCssText(string value, string runningName) =>
            runningName is not null ? $"running({runningName})" : value.Trim().ToLowerInvariant();
    }
}
