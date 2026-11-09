using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Css
{
    /// <summary>The kinds of containment of CSS Containment 2 §3, as a set.</summary>
    [Flags]
    internal enum Containment : byte
    {
        None = 0,

        /// <summary>Size containment (§3.1): the box's intrinsic sizes are those of an empty box.</summary>
        Size = 1,

        /// <summary>Layout containment (§3.2).</summary>
        Layout = 2,

        /// <summary>Style containment (§3.3).</summary>
        Style = 4,

        /// <summary>Paint containment (§3.4).</summary>
        Paint = 8,

        /// <summary>Inline-size containment (css-contain-3): size containment in the inline axis alone.</summary>
        InlineSize = 16,

        /// <summary><c>contain: content</c>.</summary>
        Content = Layout | Paint | Style,

        /// <summary><c>contain: strict</c>.</summary>
        Strict = Size | Layout | Paint | Style
    }

    /// <summary>
    /// The grammars of CSS Containment 2 §2 (<c>contain</c>) and §4 (<c>content-visibility</c>), kept as the text that was written and read through these same
    /// functions by the render layer, so what the cascade accepts is exactly what is understood.
    /// </summary>
    internal static class ContainGrammar
    {
        private static readonly char[] Whitespace = [' ', '\t', '\r', '\n', '\f'];

        /// <summary>
        /// <c>contain</c>: <c>none | strict | content | [ [ size | inline-size ] || layout || style || paint ]</c>. <c>none</c>, <c>strict</c> and <c>content</c>
        /// stand alone; the keywords of the last form may come in any order, each at most once, and <c>size</c> and <c>inline-size</c> not together.
        /// Anything else makes the whole declaration invalid.
        /// </summary>
        internal static bool TryParseContain(string text, out Containment containment)
        {
            containment = Containment.None;
            var rest = text.AsSpan().Trim();

            if (rest.IsEmpty) return false;

            // The stand-alone keywords take the whole value.
            if (rest.Equals("none", StringComparison.OrdinalIgnoreCase)) return true;

            if (rest.Equals("strict", StringComparison.OrdinalIgnoreCase))
            {
                containment = Containment.Strict;
                return true;
            }

            if (rest.Equals("content", StringComparison.OrdinalIgnoreCase))
            {
                containment = Containment.Content;
                return true;
            }

            while (!rest.IsEmpty)
            {
                var end = rest.IndexOfAny(Whitespace);
                var token = end < 0 ? rest : rest[..end];
                var kind = KindOf(token);

                // Not one of the four, or one that was already there (the stand-alone keywords were taken above, so here they are invalid).
                if (kind == Containment.None || (containment & kind) != 0
                    || ((containment | kind) & (Containment.Size | Containment.InlineSize)) == (Containment.Size | Containment.InlineSize))
                {
                    containment = Containment.None;
                    return false;
                }

                containment |= kind;
                rest = end < 0 ? default : rest[end..].TrimStart();
            }

            return true;
        }

        private static Containment KindOf(ReadOnlySpan<char> token) =>
            token.Equals("size", StringComparison.OrdinalIgnoreCase) ? Containment.Size
            : token.Equals("inline-size", StringComparison.OrdinalIgnoreCase) ? Containment.InlineSize
            : token.Equals("layout", StringComparison.OrdinalIgnoreCase) ? Containment.Layout
            : token.Equals("style", StringComparison.OrdinalIgnoreCase) ? Containment.Style
            : token.Equals("paint", StringComparison.OrdinalIgnoreCase) ? Containment.Paint
            : Containment.None;

        internal static bool IsContain(string text) => TryParseContain(text, out _);

        /// <summary><c>content-visibility</c>: <c>visible | auto | hidden</c>.</summary>
        internal static bool IsContentVisibility(string text)
        {
            var value = text.AsSpan().Trim();

            return value.Equals("visible", StringComparison.OrdinalIgnoreCase)
                || value.Equals("auto", StringComparison.OrdinalIgnoreCase)
                || value.Equals("hidden", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// <c>auto? [ none | &lt;length [0,∞]&gt; ]</c> (css-sizing-4 §5.1, <c>contain-intrinsic-width</c> and its siblings), over the tokens of one value.
        /// </summary>
        internal static bool TryParseIntrinsic(IReadOnlyList<Token> tokens)
        {
            var toks = tokens.Where(t => t.Type != TokenType.Whitespace).ToList();

            if (toks.Count > 0 && toks[0].Type == TokenType.Ident && toks[0].Data.Isi(Keywords.Auto)) toks.RemoveAt(0);
            if (toks.Count != 1) return false;

            if (toks[0].Type == TokenType.Ident) return toks[0].Data.Isi(Keywords.None);

            // A bare 0 is a legal <length>.
            if (toks[0].Type == TokenType.Number) return toks[0].Value == 0;
            if (toks[0].Type != TokenType.Dimension) return false;

            return Converters.LengthConverter.Convert([toks[0]]) is not null && toks[0].Value >= 0;
        }

        /// <summary>
        /// Reads the stored text of a <c>contain-intrinsic-*</c> value: whether it has <c>auto</c> (the last remembered size, which a document with no
        /// earlier rendering has none of, so the length that follows is used) and the length, or null for <c>none</c> or text that is not one.
        /// </summary>
        internal static string? ParseIntrinsic(string? text, out bool auto)
        {
            auto = false;
            if (string.IsNullOrWhiteSpace(text)) return null;

            var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var index = 0;

            if (parts.Length > 1 && parts[0].Equals(Keywords.Auto, StringComparison.OrdinalIgnoreCase))
            {
                auto = true;
                index = 1;
            }

            return parts[index].Equals(Keywords.None, StringComparison.OrdinalIgnoreCase) ? null : parts[index];
        }

        /// <summary>
        /// The containment a box asks for: its <c>contain</c>, plus what <c>content-visibility</c> turns on. <c>auto</c> turns on layout, style and paint
        /// containment; <c>hidden</c> turns on size containment as well (§4: it always skips its contents).
        /// </summary>
        internal static Containment Of(string? contain, string? contentVisibility)
        {
            var result = Containment.None;

            if (!string.IsNullOrEmpty(contain) && !contain.Equals("none", StringComparison.OrdinalIgnoreCase) && TryParseContain(contain, out var parsed))
            {
                result = parsed;
            }

            if (!string.IsNullOrEmpty(contentVisibility))
            {
                if (contentVisibility.Equals("auto", StringComparison.OrdinalIgnoreCase)) result |= Containment.Content;
                else if (contentVisibility.Equals("hidden", StringComparison.OrdinalIgnoreCase)) result |= Containment.Strict;
            }

            return result;
        }
    }
}
