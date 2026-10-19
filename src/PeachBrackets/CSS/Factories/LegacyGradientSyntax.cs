#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeachPDF.CSS
{
    /// <summary>
    /// Rewrites the pre-standard vendor gradient functions (<c>-webkit-linear-gradient()</c>,
    /// <c>-moz-radial-gradient()</c>, and their <c>repeating-</c> forms; <c>-o-</c> and <c>-ms-</c> spellings too) into
    /// the standard <c>linear-gradient()</c>/<c>radial-gradient()</c> token stream, so the CSS-OM converters and the
    /// box-layer gradient parser only ever see standard syntax.
    /// </summary>
    /// <remarks>
    /// Not a pure rename - the prefixed grammar differs from the standard one:
    /// <list type="bullet">
    /// <item>Linear: an angle is measured counter-clockwise from the east axis (standard: clockwise from north), so
    /// <c>θ_std = 90° − θ_legacy</c>; a side/corner keyword names where the line <em>starts</em> (<c>top</c> = the
    /// standard <c>to bottom</c>), so each keyword is flipped to its opposite; with neither, both default to
    /// top-to-bottom.</item>
    /// <item>Radial: the center position comes <em>first</em> (standard: after <c>at</c>), the size keywords are
    /// <c>cover</c>/<c>contain</c> (standard <c>farthest-corner</c>/<c>closest-side</c>).</item>
    /// </list>
    /// Applied in <see cref="ValueBuilder"/>, the one place every declaration value passes through, so it covers
    /// <c>background</c>, <c>background-image</c>, <c>border-image-source</c>, <c>list-style-image</c>, <c>content</c>
    /// and <c>@supports</c> conditions alike. A function whose first group already uses the standard
    /// <c>to &lt;side&gt;</c> form passes through unchanged. <c>-webkit-gradient()</c> (a different function with a
    /// different grammar) is not handled.
    /// </remarks>
    internal static class LegacyGradientSyntax
    {
        private static readonly string[] Prefixes = ["-webkit-", "-moz-", "-o-", "-ms-"];

        private static readonly string[] StandardNames =
        [
            FunctionNames.LinearGradient, FunctionNames.RepeatingLinearGradient,
            FunctionNames.RadialGradient, FunctionNames.RepeatingRadialGradient,
        ];

        /// <summary>Returns <paramref name="token"/> rewritten to standard syntax if it is a legacy gradient function; otherwise unchanged.</summary>
        internal static Token Rewrite(Token token)
        {
            if (token.Type != TokenType.Function || token.Arguments is null ||
                !TryGetStandardName(token.Data, out var standardName, out var isLinear))
                return token;

            var groups = SplitGroups(token.ArgumentTokens);
            if (groups.Count > 0) groups = isLinear ? RewriteLinear(groups) : RewriteRadial(groups);

            var rewritten = Token.NewFunction(standardName.AsMemory(), token.Position);
            for (var i = 0; i < groups.Count; i++)
            {
                if (i > 0)
                {
                    rewritten.AddArgumentToken(Token.Comma);
                    rewritten.AddArgumentToken(Token.Whitespace);
                }

                foreach (var t in groups[i]) rewritten.AddArgumentToken(t);
            }

            rewritten.AddArgumentToken(new Token(TokenType.RoundBracketClose, ")".AsMemory(), token.Position));
            return rewritten;
        }

        private static bool TryGetStandardName(ReadOnlySpan<char> name, out string standardName, out bool isLinear)
        {
            standardName = string.Empty;
            isLinear = false;

            if (name.Length < 2 || name[0] != '-') return false;

            foreach (var prefix in Prefixes)
            {
                if (!name.StartsWith(prefix.AsSpan(), StringComparison.OrdinalIgnoreCase)) continue;

                var rest = name[prefix.Length..];
                foreach (var candidate in StandardNames)
                {
                    if (!rest.Equals(candidate.AsSpan(), StringComparison.OrdinalIgnoreCase)) continue;

                    standardName = candidate;
                    isLinear = candidate.Contains("linear", StringComparison.Ordinal);
                    return true;
                }

                return false;
            }

            return false;
        }

        // Splits on top-level commas (nested functions are self-contained tokens, so a flat split is top-level),
        // trimming surrounding whitespace from each group.
        private static List<List<Token>> SplitGroups(IReadOnlyList<Token> tokens)
        {
            var groups = new List<List<Token>> { new() };

            foreach (var t in tokens)
            {
                if (t.Type == TokenType.Comma) groups.Add([]);
                else groups[^1].Add(t);
            }

            foreach (var g in groups)
            {
                while (g.Count > 0 && g[0].Type == TokenType.Whitespace) g.RemoveAt(0);
                while (g.Count > 0 && g[^1].Type == TokenType.Whitespace) g.RemoveAt(g.Count - 1);
            }

            return groups;
        }

        private static List<List<Token>> RewriteLinear(List<List<Token>> groups)
        {
            var first = groups[0];

            if (TryGetAngleDegrees(first, out var legacyDegrees))
            {
                var standard = (90 - legacyDegrees) % 360;
                if (standard < 0) standard += 360;

                groups[0] =
                [
                    Token.NewUnit(TokenType.Dimension,
                        standard.ToString("0.####", CultureInfo.InvariantCulture).AsMemory(), "deg", first[0].Position),
                ];
            }
            else if (TryFlipSideKeywords(first, out var flipped))
            {
                groups[0] = flipped;
            }

            // Otherwise no direction group was written (or it is already standard `to ...`): the legacy default,
            // `top`, is the standard default, `to bottom`, so the groups pass through untouched.
            return groups;
        }

        private static bool TryGetAngleDegrees(List<Token> group, out double degrees)
        {
            degrees = 0;
            if (group.Count != 1) return false;

            var t = group[0];
            if (t.Type == TokenType.Number) return t.Value == 0;
            if (t.Type != TokenType.Dimension) return false;

            var unit = t.Unit;
            double value = t.Value;
            if (unit.Isi("deg")) degrees = value;
            else if (unit.Isi("grad")) degrees = value * 0.9;
            else if (unit.Isi("rad")) degrees = value * 180 / Math.PI;
            else if (unit.Isi("turn")) degrees = value * 360;
            else return false;

            return true;
        }

        // `top`/`left`/`bottom`/`right` (one, or a vertical+horizontal pair) -> the standard `to <opposite...>`.
        private static bool TryGetSideKeyword(Token t, out string opposite, out bool vertical)
        {
            opposite = string.Empty;
            vertical = false;
            if (t.Type != TokenType.Ident) return false;

            if (t.Data.Isi("top")) { opposite = "bottom"; vertical = true; }
            else if (t.Data.Isi("bottom")) { opposite = "top"; vertical = true; }
            else if (t.Data.Isi("left")) opposite = "right";
            else if (t.Data.Isi("right")) opposite = "left";
            else return false;

            return true;
        }

        private static bool TryFlipSideKeywords(List<Token> group, out List<Token> flipped)
        {
            flipped = [];
            string? vert = null, horiz = null;

            foreach (var t in group)
            {
                if (t.Type == TokenType.Whitespace) continue;
                if (!TryGetSideKeyword(t, out var opposite, out var vertical)) return false;

                if (vertical)
                {
                    if (vert != null) return false;
                    vert = opposite;
                }
                else
                {
                    if (horiz != null) return false;
                    horiz = opposite;
                }
            }

            if (vert == null && horiz == null) return false;

            var pos = group[0].Position;
            flipped.Add(Ident("to", pos));
            if (vert != null) { flipped.Add(Token.Whitespace); flipped.Add(Ident(vert, pos)); }
            if (horiz != null) { flipped.Add(Token.Whitespace); flipped.Add(Ident(horiz, pos)); }
            return true;
        }

        private static Token Ident(string text, TextPosition pos) => Token.NewKeyword(TokenType.Ident, text.AsMemory(), pos);

        private static bool IsShapeOrSizeKeyword(ReadOnlySpan<char> s) =>
            s.Isi("circle") || s.Isi("ellipse") || s.Isi("cover") || s.Isi("contain") ||
            s.Isi("closest-side") || s.Isi("closest-corner") || s.Isi("farthest-side") || s.Isi("farthest-corner");

        private static bool IsPositionKeyword(ReadOnlySpan<char> s) =>
            s.Isi("left") || s.Isi("right") || s.Isi("top") || s.Isi("bottom") || s.Isi("center");

        // A header group has no color, so it can only hold position/shape/size keywords and numeric lengths -
        // which is what tells it apart from a color stop (every stop carries a color).
        private static bool IsHeaderGroup(List<Token> group)
        {
            if (group.Count == 0) return false;

            foreach (var t in group)
            {
                switch (t.Type)
                {
                    case TokenType.Whitespace:
                    case TokenType.Dimension:
                    case TokenType.Percentage:
                    case TokenType.Number:
                        break;
                    case TokenType.Ident when IsShapeOrSizeKeyword(t.Data) || IsPositionKeyword(t.Data):
                        break;
                    default:
                        return false;
                }
            }

            return true;
        }

        private static bool HasShapeOrSize(List<Token> group)
        {
            foreach (var t in group)
            {
                if (t.Type == TokenType.Ident && IsShapeOrSizeKeyword(t.Data)) return true;
            }

            return false;
        }

        private static List<List<Token>> RewriteRadial(List<List<Token>> groups)
        {
            // Legacy header: [<position>,]? [<shape> || <size>,]? then the color stops.
            var headerCount = 0;
            while (headerCount < 2 && headerCount < groups.Count && IsHeaderGroup(groups[headerCount])) headerCount++;

            if (headerCount == 0) return groups;

            List<Token>? position = null, shapeSize = null;
            if (headerCount == 2)
            {
                position = groups[0];
                shapeSize = groups[1];
            }
            else if (HasShapeOrSize(groups[0])) shapeSize = groups[0];
            else position = groups[0];

            var header = new List<Token>();
            if (shapeSize != null)
            {
                foreach (var t in shapeSize)
                {
                    if (t.Type == TokenType.Ident && t.Data.Isi("cover")) header.Add(Ident("farthest-corner", t.Position));
                    else if (t.Type == TokenType.Ident && t.Data.Isi("contain")) header.Add(Ident("closest-side", t.Position));
                    else header.Add(t);
                }
            }

            if (position != null)
            {
                if (header.Count > 0) header.Add(Token.Whitespace);
                header.Add(Ident("at", position[0].Position));
                header.Add(Token.Whitespace);
                header.AddRange(position);
            }

            var result = new List<List<Token>> { header };
            for (var i = headerCount; i < groups.Count; i++) result.Add(groups[i]);
            return result;
        }
    }
}
