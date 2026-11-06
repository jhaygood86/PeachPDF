#nullable disable

using System;
using System.Collections.Generic;

namespace PeachBrackets.Css
{
    /// <summary>
    /// The grammars of the properties of scroll containers (CSS Overflow 3, CSS Overscroll Behavior 1, CSS Scroll Snap 1, CSS Scrollbars 1), kept as the text that was
    /// written and read through these same functions by the render layer, so what the cascade accepts is exactly what is understood.
    /// </summary>
    internal static class ScrollGrammar
    {
        /// <summary><c>scroll-behavior</c>: <c>auto | smooth</c>.</summary>
        internal static bool IsScrollBehavior(string text) => text.Trim().ToLowerInvariant() is "auto" or "smooth";

        /// <summary>One value of <c>overscroll-behavior-x</c> or <c>-y</c>: <c>auto | contain | none</c>.</summary>
        internal static bool IsOverscrollBehavior(string text) => text.Trim().ToLowerInvariant() is "auto" or "contain" or "none";

        /// <summary><c>scroll-snap-stop</c>: <c>normal | always</c>.</summary>
        internal static bool IsSnapStop(string text) => text.Trim().ToLowerInvariant() is "normal" or "always";

        /// <summary><c>scrollbar-width</c>: <c>auto | thin | none</c>.</summary>
        internal static bool IsScrollbarWidth(string text) => text.Trim().ToLowerInvariant() is "auto" or "thin" or "none";

        /// <summary>
        /// <c>scroll-snap-type</c>: <c>none | [ x | y | block | inline | both ] [ mandatory | proximity ]?</c>. The strictness is <c>proximity</c> when left out.
        /// </summary>
        internal static bool TryParseSnapType(string text, out string axis, out bool mandatory)
        {
            axis = "none";
            mandatory = false;

            var parts = text.Trim().ToLowerInvariant().Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is 0 or > 2) return false;

            if (parts[0] == "none") return parts.Length == 1;
            if (parts[0] is not ("x" or "y" or "block" or "inline" or "both")) return false;

            axis = parts[0];
            if (parts.Length == 2)
            {
                if (parts[1] is not ("mandatory" or "proximity")) return false;
                mandatory = parts[1] == "mandatory";
            }

            return true;
        }

        /// <summary><c>scroll-snap-align</c>: <c>[ none | start | end | center ]{1,2}</c>, the block axis first and the inline axis the same when only one is written.</summary>
        internal static bool TryParseSnapAlign(string text, out string block, out string inline)
        {
            block = inline = "none";
            var parts = text.Trim().ToLowerInvariant().Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is 0 or > 2) return false;

            foreach (var part in parts)
            {
                if (part is not ("none" or "start" or "end" or "center")) return false;
            }

            block = parts[0];
            inline = parts.Length == 2 ? parts[1] : parts[0];
            return true;
        }

        /// <summary><c>scrollbar-gutter</c>: <c>auto | stable &amp;&amp; both-edges?</c>.</summary>
        internal static bool IsScrollbarGutter(string text)
        {
            var parts = text.Trim().ToLowerInvariant().Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
            return parts switch
            {
                ["auto"] => true,
                ["stable"] => true,
                ["stable", "both-edges"] or ["both-edges", "stable"] => true,
                _ => false,
            };
        }

        /// <summary><c>scrollbar-color</c>: <c>auto | &lt;color&gt;{2}</c> (the thumb, then the track). The colours are settled where they are used.</summary>
        internal static bool TryParseScrollbarColor(string text, out string thumb, out string track)
        {
            thumb = track = null;
            var trimmed = text.Trim();
            if (trimmed.Equals("auto", StringComparison.OrdinalIgnoreCase)) return true;

            var parts = SplitTopLevel(trimmed);
            if (parts.Count != 2) return false;

            thumb = parts[0];
            track = parts[1];
            return true;
        }

        // The whitespace-separated words of a value, a function's arguments (and any space inside its parentheses) kept with it.
        private static List<string> SplitTopLevel(string text)
        {
            var words = new List<string>();
            var depth = 0;
            var start = -1;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (char.IsWhiteSpace(c) && depth == 0)
                {
                    if (start >= 0) words.Add(text[start..i]);
                    start = -1;
                    continue;
                }

                if (start < 0) start = i;
                if (c == '(') depth++;
                else if (c == ')') depth = Math.Max(0, depth - 1);
            }

            if (start >= 0) words.Add(text[start..]);
            return words;
        }
    }
}
