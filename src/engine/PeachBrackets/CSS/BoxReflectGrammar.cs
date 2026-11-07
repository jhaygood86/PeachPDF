#nullable enable

using System;
using System.Linq;

namespace PeachBrackets.Css
{
    /// <summary>The side of a box <c>-webkit-box-reflect</c> places its reflection on.</summary>
    internal enum BoxReflectDirection
    {
        Above,
        Below,
        Left,
        Right
    }

    /// <summary>
    /// The shared grammar for <c>-webkit-box-reflect</c>: <c>none | [ above | below | left | right ]? &lt;length&gt;? &lt;image&gt;?</c>.
    /// The trailing image (a gradient in practice) is the mask the reflection is drawn through; it is returned as authored text for the
    /// image parser. Used by the CSS-OM converter (to accept or reject) and by the painter.
    /// </summary>
    internal static class BoxReflectGrammar
    {
        /// <summary>Parses <paramref name="value"/>; <paramref name="none"/> is true for the literal <c>none</c> (which paints nothing).</summary>
        internal static bool TryParse(string? value, out bool none, out BoxReflectDirection direction, out string? offset, out string? image)
        {
            none = false;
            direction = BoxReflectDirection.Below;
            offset = null;
            image = null;

            if (string.IsNullOrWhiteSpace(value)) return false;

            var parts = global::PeachBrackets.Html.Parse.CssValueParser.SplitTopLevelWhitespace(value).ToList();
            if (parts.Count == 0) return false;

            if (parts.Count == 1 && parts[0].Equals(Keywords.None, StringComparison.OrdinalIgnoreCase))
            {
                none = true;
                return true;
            }

            var i = 0;
            if (TryDirection(parts[i], out var parsed))
            {
                direction = parsed;
                i++;
            }

            if (i < parts.Count && IsLength(parts[i]))
            {
                offset = parts[i];
                i++;
            }

            if (i < parts.Count)
            {
                image = string.Join(" ", parts.Skip(i));
                // An image is a function (gradient, image-set, ...) or url(...); a stray keyword is not.
                if (!image.Contains('(')) return false;
            }

            return true;
        }

        private static bool TryDirection(string word, out BoxReflectDirection direction)
        {
            direction = BoxReflectDirection.Below;
            if (word.Equals("above", StringComparison.OrdinalIgnoreCase)) direction = BoxReflectDirection.Above;
            else if (word.Equals("below", StringComparison.OrdinalIgnoreCase)) direction = BoxReflectDirection.Below;
            else if (word.Equals("left", StringComparison.OrdinalIgnoreCase)) direction = BoxReflectDirection.Left;
            else if (word.Equals("right", StringComparison.OrdinalIgnoreCase)) direction = BoxReflectDirection.Right;
            else return false;
            return true;
        }

        private static bool IsLength(string word) =>
            word.StartsWith("calc(", StringComparison.OrdinalIgnoreCase)
            || (word.Length > 0 && !word.Contains('(') && (char.IsDigit(word[0]) || word[0] is '.' or '-' or '+'));
    }
}
