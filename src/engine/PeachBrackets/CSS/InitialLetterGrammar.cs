#nullable enable

using System;
using System.Globalization;
using System.Linq;

namespace PeachBrackets.Css
{
    /// <summary>
    /// The shared grammar for <c>initial-letter</c> (CSS Inline 3 §7.4):
    /// <c>normal | &lt;number [1,∞]&gt; &lt;integer [1,∞]&gt; | &lt;number [1,∞]&gt; &amp;&amp; [ drop | raise ]?</c>.
    /// Used by the CSS-OM converter (to accept or reject) and by the first-letter sizing (to read the size and sink).
    /// </summary>
    internal static class InitialLetterGrammar
    {
        /// <summary>
        /// Parses <paramref name="value"/>. <paramref name="size"/> is the number of lines the letter's height spans and
        /// <paramref name="sink"/> the line its baseline sits on; both are 0 for <c>normal</c>.
        /// </summary>
        internal static bool TryParse(string? value, out double size, out int sink)
        {
            size = 0;
            sink = 0;

            if (string.IsNullOrWhiteSpace(value)) return false;

            var parts = global::PeachBrackets.Html.Parse.CssValueParser.SplitTopLevelWhitespace(value).ToList();

            if (parts.Count == 1 && parts[0].Equals(Keywords.Normal, StringComparison.OrdinalIgnoreCase)) return true;
            if (parts.Count is < 1 or > 2) return false;

            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedSize) || parsedSize < 1) return false;

            var parsedSink = (int)Math.Floor(parsedSize);

            if (parts.Count == 2)
            {
                if (parts[1].Equals("drop", StringComparison.OrdinalIgnoreCase)) { }
                else if (parts[1].Equals("raise", StringComparison.OrdinalIgnoreCase)) parsedSink = 1;
                else if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out parsedSink) || parsedSink < 1) return false;
            }

            size = parsedSize;
            sink = parsedSink;
            return true;
        }
    }
}
