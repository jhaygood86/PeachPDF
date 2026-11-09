using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Css
{
    /// <summary>The box <c>overflow-clip-margin</c> measures its margin from (css-overflow-4 §3.1).</summary>
    internal enum OverflowClipVisualBox : byte
    {
        PaddingBox = 0,
        ContentBox = 1,
        BorderBox = 2
    }

    /// <summary>
    /// The grammar of <c>overflow-clip-margin</c> (css-overflow-4 §3.1): <c>&lt;visual-box&gt; || &lt;length [0,∞]&gt;</c>, with <c>padding-box</c> and 0 for
    /// whatever is left out. Kept as the text that was written and read back through these same functions by layout.
    /// </summary>
    internal static class OverflowClipMarginGrammar
    {
        private static VisualBoxOrNone BoxOf(string text) =>
            text.Equals("content-box", StringComparison.OrdinalIgnoreCase) ? VisualBoxOrNone.Content
            : text.Equals("padding-box", StringComparison.OrdinalIgnoreCase) ? VisualBoxOrNone.Padding
            : text.Equals("border-box", StringComparison.OrdinalIgnoreCase) ? VisualBoxOrNone.Border
            : VisualBoxOrNone.None;

        private enum VisualBoxOrNone : byte { None, Padding, Content, Border }

        /// <summary>One <c>&lt;visual-box&gt;</c> keyword, one length, or both in either order; each at most once.</summary>
        internal static bool TryParse(IReadOnlyList<Token> tokens)
        {
            var toks = tokens.Where(t => t.Type != TokenType.Whitespace).ToList();
            if (toks.Count is 0 or > 2) return false;

            var boxes = 0;
            var lengths = 0;

            foreach (var token in toks)
            {
                if (token.Type == TokenType.Ident)
                {
                    if (BoxOf(token.Data.ToString()) == VisualBoxOrNone.None) return false;
                    boxes++;
                }
                else if (token.Type == TokenType.Number)
                {
                    // A bare 0 is a legal <length>.
                    if (token.Value != 0) return false;
                    lengths++;
                }
                else if (token.Type == TokenType.Dimension)
                {
                    if (Converters.LengthConverter.Convert([token]) is null || token.Value < 0) return false;
                    lengths++;
                }
                else
                {
                    return false;
                }
            }

            return boxes <= 1 && lengths <= 1;
        }

        /// <summary>
        /// Reads the stored text of <c>overflow-clip-margin</c>: the visual box it measures from (<c>padding-box</c> when none is written) and the margin as
        /// written (null for none, which is 0).
        /// </summary>
        internal static OverflowClipVisualBox Read(string? text, out string? length)
        {
            length = null;
            var visualBox = OverflowClipVisualBox.PaddingBox;
            if (string.IsNullOrWhiteSpace(text)) return visualBox;

            foreach (var part in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                switch (BoxOf(part))
                {
                    case VisualBoxOrNone.Content: visualBox = OverflowClipVisualBox.ContentBox; break;
                    case VisualBoxOrNone.Border: visualBox = OverflowClipVisualBox.BorderBox; break;
                    case VisualBoxOrNone.Padding: visualBox = OverflowClipVisualBox.PaddingBox; break;
                    default: length = part; break;
                }
            }

            return visualBox;
        }
    }
}
