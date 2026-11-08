#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Css
{
    /// <summary>The over or under edge <c>text-box-edge</c> trims to (CSS Inline 3 §5.2).</summary>
    internal enum TextEdgeMetric
    {
        /// <summary>The font's ascent (over) or descent (under).</summary>
        Text,
        Cap,
        Ex,
        Alphabetic,
        Ideographic,
        IdeographicInk
    }

    /// <summary>A parsed <c>text-box-edge</c>.</summary>
    internal readonly record struct TextBoxEdge(bool IsAuto, TextEdgeMetric Over, TextEdgeMetric Under);

    /// <summary>Which line edges <c>text-box-trim</c> trims.</summary>
    [Flags]
    internal enum TextBoxTrim
    {
        None = 0,
        Start = 1,
        End = 2,
        Both = Start | End
    }

    /// <summary>
    /// The grammars of the CSS Rhythmic Sizing and CSS Inline 3 line-box properties (<c>line-height-step</c>, <c>block-step-*</c>,
    /// <c>text-box-trim</c>, <c>text-box-edge</c>), shared by the CSS-OM (to accept or reject at parse time) and layout (to read the
    /// stored text back).
    /// </summary>
    internal static class RhythmGrammar
    {
        private static List<Token> Significant(IReadOnlyList<Token> tokens) =>
            tokens.Where(t => t.Type != TokenType.Whitespace).ToList();

        private static bool IsIdent(Token token, string keyword) =>
            token.Type == TokenType.Ident && token.Data.Isi(keyword);

        /// <summary><c>none | &lt;length [0,∞]&gt;</c>.</summary>
        internal static bool TryParseNoneOrLength(IReadOnlyList<Token> tokens)
        {
            var toks = Significant(tokens);
            if (toks.Count != 1) return false;
            if (IsIdent(toks[0], Keywords.None)) return true;
            // A bare 0 is a legal <length>.
            if (toks[0].Type == TokenType.Number) return toks[0].Value == 0;
            if (toks[0].Type != TokenType.Dimension) return false;

            return Converters.LengthConverter.Convert([toks[0]]) is not null && toks[0].Value >= 0;
        }

        /// <summary>A grammar accepting exactly one of <paramref name="keywords"/>.</summary>
        internal static Func<IReadOnlyList<Token>, bool> OneOf(params string[] keywords) => tokens =>
        {
            var toks = Significant(tokens);
            return toks.Count == 1 && toks[0].Type == TokenType.Ident && keywords.Any(k => toks[0].Data.Isi(k));
        };

        /// <summary><c>none | trim-start | trim-end | trim-both</c> (CSS Inline 3 §5.1).</summary>
        internal static readonly Func<IReadOnlyList<Token>, bool> TrimGrammar = OneOf(Keywords.None, "trim-start", "trim-end", "trim-both");

        /// <summary>Reads the stored <c>text-box-trim</c> text.</summary>
        internal static TextBoxTrim ParseTrim(string text) => (text ?? "").Trim().ToLowerInvariant() switch
        {
            "trim-start" => TextBoxTrim.Start,
            "trim-end" => TextBoxTrim.End,
            "trim-both" => TextBoxTrim.Both,
            _ => TextBoxTrim.None
        };

        private static readonly string[] OverKeywords = ["text", "cap", "ex", "ideographic", "ideographic-ink"];
        private static readonly string[] UnderKeywords = ["text", "alphabetic", "ideographic", "ideographic-ink"];

        /// <summary><c>auto | [ text | cap | ex | ideographic | ideographic-ink ] [ text | alphabetic | ideographic | ideographic-ink ]?</c> (§5.2).</summary>
        internal static bool TryParseEdge(IReadOnlyList<Token> tokens)
        {
            var toks = Significant(tokens);
            if (toks.Count is 0 or > 2 || toks.Any(t => t.Type != TokenType.Ident)) return false;
            if (toks.Count == 1 && IsIdent(toks[0], Keywords.Auto)) return true;

            return OverKeywords.Any(k => toks[0].Data.Isi(k)) && (toks.Count == 1 || UnderKeywords.Any(k => toks[1].Data.Isi(k)));
        }

        /// <summary>Reads the stored <c>text-box-edge</c> text; anything unrecognised is <c>auto</c>.</summary>
        internal static TextBoxEdge ParseEdge(string text)
        {
            var words = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length is 0 or > 2 || words[0].Equals(Keywords.Auto, StringComparison.OrdinalIgnoreCase))
                return new TextBoxEdge(true, TextEdgeMetric.Text, TextEdgeMetric.Text);

            var over = MetricOf(words[0]);
            // One value: both edges use it, except cap and ex, whose under edge is the alphabetic baseline (§5.2).
            var under = words.Length == 2
                ? MetricOf(words[1])
                : over is TextEdgeMetric.Cap or TextEdgeMetric.Ex ? TextEdgeMetric.Alphabetic : over;

            return new TextBoxEdge(false, over, under);
        }

        private static TextEdgeMetric MetricOf(string keyword) => keyword.ToLowerInvariant() switch
        {
            "cap" => TextEdgeMetric.Cap,
            "ex" => TextEdgeMetric.Ex,
            "alphabetic" => TextEdgeMetric.Alphabetic,
            "ideographic" => TextEdgeMetric.Ideographic,
            "ideographic-ink" => TextEdgeMetric.IdeographicInk,
            _ => TextEdgeMetric.Text
        };
    }
}
