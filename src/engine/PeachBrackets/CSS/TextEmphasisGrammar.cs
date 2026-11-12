#nullable disable

using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Css
{
    /// <summary>What a decoration line is kept off, from <c>text-decoration-skip</c> (CSS Text Decoration 4 section 2.4).</summary>
    [System.Flags]
    internal enum DecorationSkips
    {
        None = 0,
        Objects = 1,
        Spaces = 2,
        LeadingSpaces = 4,
        TrailingSpaces = 8,
        Edges = 16,
        BoxDecoration = 32,
        Ink = 64
    }

    /// <summary>The shape of a <c>text-emphasis-style</c> mark.</summary>
    internal enum TextEmphasisShape
    {
        None,
        Dot,
        Circle,
        DoubleCircle,
        Triangle,
        Sesame,
        Custom
    }

    /// <summary>A parsed <c>text-emphasis-style</c>: a keyword shape and fill, or a one-character string.</summary>
    internal readonly record struct TextEmphasisMark(TextEmphasisShape Shape, bool Filled, string Custom)
    {
        /// <summary>The character drawn for the mark, or null for <c>none</c>.</summary>
        internal string Glyph()
        {
            return Shape switch
            {
                TextEmphasisShape.None => null,
                TextEmphasisShape.Custom => Custom,
                TextEmphasisShape.Dot => Filled ? "•" : "◦",
                TextEmphasisShape.Circle => Filled ? "●" : "○",
                TextEmphasisShape.DoubleCircle => Filled ? "◉" : "◎",
                TextEmphasisShape.Triangle => Filled ? "▲" : "△",
                _ => Filled ? "﹅" : "﹆"
            };
        }
    }

    /// <summary>
    /// The shared grammars of the CSS Text Decoration 4 properties added with <c>text-emphasis</c>, used by both
    /// the CSS-OM (to accept or reject at parse time) and the painter (to read the stored text back).
    /// </summary>
    internal static class TextEmphasisGrammar
    {
        private static List<Token> Significant(IReadOnlyList<Token> tokens) =>
            [.. tokens.Where(t => t.Type != TokenType.Whitespace)];

        private static bool IsIdent(Token token, string keyword) =>
            token.Type == TokenType.Ident && token.Data.Isi(keyword);

        /// <summary><c>none | [ [ filled | open ] || [ dot | circle | double-circle | triangle | sesame ] ] | &lt;string&gt;</c> (§4.1).</summary>
        internal static bool TryParseStyle(IReadOnlyList<Token> tokens, out TextEmphasisMark mark)
        {
            mark = new TextEmphasisMark(TextEmphasisShape.None, true, null);
            var toks = Significant(tokens);
            if (toks.Count == 0) return false;

            if (toks.Count == 1 && IsIdent(toks[0], Keywords.None)) return true;

            if (toks.Count == 1 && toks[0].Type == TokenType.String)
            {
                var text = toks[0].Data.ToString();
                if (string.IsNullOrEmpty(text)) return false;
                mark = new TextEmphasisMark(TextEmphasisShape.Custom, true, text);
                return true;
            }

            bool? filled = null;
            var shape = TextEmphasisShape.None;
            foreach (var token in toks)
            {
                if (token.Type != TokenType.Ident) return false;
                if (token.Data.Isi("filled") || token.Data.Isi("open"))
                {
                    if (filled is not null) return false;
                    filled = token.Data.Isi("filled");
                    continue;
                }

                var parsed = token.Data.Isi("dot") ? TextEmphasisShape.Dot
                    : token.Data.Isi("circle") ? TextEmphasisShape.Circle
                    : token.Data.Isi("double-circle") ? TextEmphasisShape.DoubleCircle
                    : token.Data.Isi("triangle") ? TextEmphasisShape.Triangle
                    : token.Data.Isi("sesame") ? TextEmphasisShape.Sesame
                    : TextEmphasisShape.None;
                if (parsed == TextEmphasisShape.None || shape != TextEmphasisShape.None) return false;
                shape = parsed;
            }

            // A fill with no shape names the default shape (§4.1: dot in horizontal text).
            if (shape == TextEmphasisShape.None) shape = TextEmphasisShape.Dot;
            mark = new TextEmphasisMark(shape, filled ?? true, null);
            return true;
        }

        internal static bool TryParseStyle(string value, out TextEmphasisMark mark)
        {
            using var pooled = global::PeachBrackets.Html.Parse.CssValueParser.GetCssTokensPooled(value);
            List<Token> tokens = pooled;
            return TryParseStyle(tokens, out mark);
        }

        /// <summary><c>[ over | under ] &amp;&amp; [ right | left ]?</c> (§4.3). Defaults are over and right.</summary>
        internal static bool TryParsePosition(IReadOnlyList<Token> tokens, out bool over, out bool right)
        {
            over = true;
            right = true;
            var toks = Significant(tokens);
            if (toks.Count is < 1 or > 2) return false;

            bool? side = null;
            bool? line = null;
            foreach (var token in toks)
            {
                if (token.Type != TokenType.Ident) return false;
                if (token.Data.Isi("over") || token.Data.Isi("under"))
                {
                    if (line is not null) return false;
                    line = token.Data.Isi("over");
                }
                else if (token.Data.Isi("right") || token.Data.Isi("left"))
                {
                    if (side is not null) return false;
                    side = token.Data.Isi("right");
                }
                else return false;
            }

            // `over`/`under` is required: a side alone is not a value.
            if (line is null) return false;
            over = line.Value;
            right = side ?? true;
            return true;
        }

        internal static bool TryParsePosition(string value, out bool over, out bool right)
        {
            using var pooled = global::PeachBrackets.Html.Parse.CssValueParser.GetCssTokensPooled(value);
            List<Token> tokens = pooled;
            return TryParsePosition(tokens, out over, out right);
        }

        /// <summary><c>none | auto | [ objects || [ spaces | [ leading-spaces || trailing-spaces ] ] || edges || box-decoration ]</c> (§2.4, with the level 4 <c>ink</c>).</summary>
        internal static bool TryParseSkip(IReadOnlyList<Token> tokens)
        {
            var toks = Significant(tokens);
            if (toks.Count == 0) return false;
            if (toks.Count == 1 && (IsIdent(toks[0], Keywords.None) || IsIdent(toks[0], Keywords.Auto))) return true;

            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var token in toks)
            {
                if (token.Type != TokenType.Ident) return false;
                var name = token.Data.ToString();
                if (!(name.Isi("objects") || name.Isi("spaces") || name.Isi("leading-spaces") || name.Isi("trailing-spaces")
                      || name.Isi("edges") || name.Isi("box-decoration") || name.Isi("ink")))
                    return false;
                if (!seen.Add(name)) return false;
            }

            // `spaces` is an alternative to the two partial-space keywords, not a companion.
            return !(seen.Contains("spaces") && (seen.Contains("leading-spaces") || seen.Contains("trailing-spaces")));
        }

        /// <summary>
        /// Reads the stored <c>text-decoration-skip</c> text: <c>none</c> skips nothing, <c>auto</c> (and anything unrecognised) is the
        /// initial <c>objects</c>.
        /// </summary>
        internal static DecorationSkips ParseSkip(string text)
        {
            var words = (text ?? "").Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0 || (words.Length == 1 && words[0].Equals(Keywords.Auto, System.StringComparison.OrdinalIgnoreCase)))
                return DecorationSkips.Objects;

            var skips = DecorationSkips.None;
            foreach (var word in words)
            {
                skips |= word.ToLowerInvariant() switch
                {
                    "objects" => DecorationSkips.Objects,
                    "spaces" => DecorationSkips.Spaces,
                    "leading-spaces" => DecorationSkips.LeadingSpaces,
                    "trailing-spaces" => DecorationSkips.TrailingSpaces,
                    "edges" => DecorationSkips.Edges,
                    "box-decoration" => DecorationSkips.BoxDecoration,
                    "ink" => DecorationSkips.Ink,
                    _ => DecorationSkips.None
                };
            }

            return skips;
        }

        /// <summary><c>auto | &lt;length&gt;{1,2}</c> (§2.7): how far each end of the line is pulled in.</summary>
        internal static bool TryParseInset(IReadOnlyList<Token> tokens)
        {
            var toks = Significant(tokens);
            if (toks.Count == 0 || toks.Count > 2) return false;
            if (toks.Count == 1 && IsIdent(toks[0], Keywords.Auto)) return true;

            foreach (var token in toks)
            {
                if (token.Type is not (TokenType.Dimension or TokenType.Number)) return false;
                if (Converters.LengthConverter.Convert([token]) is null) return false;
            }

            return true;
        }

        /// <summary><c>none | all | digits &lt;integer [2,4]&gt;?</c> (css-writing-modes-4 §9.1).</summary>
        internal static bool TryParseCombineUpright(IReadOnlyList<Token> tokens)
        {
            var toks = Significant(tokens);
            if (toks.Count == 0) return false;
            if (toks.Count == 1) return IsIdent(toks[0], Keywords.None) || IsIdent(toks[0], Keywords.All) || IsIdent(toks[0], "digits");
            if (!IsIdent(toks[0], "digits") || toks.Count > 2) return false;
            return toks[1].Type == TokenType.Number && int.TryParse(toks[1].Data, out var digits) && digits is >= 2 and <= 4;
        }
    }
}
