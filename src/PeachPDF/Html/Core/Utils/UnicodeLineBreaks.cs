using PeachDrawing.Text.Unicode;
using System;
using System.Globalization;
using System.Text;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Reads where a line may end inside one box's text, from the Unicode line breaking algorithm (UAX #14) tailored the way
    /// CSS asks for.
    /// </summary>
    internal static class UnicodeLineBreaks
    {
        private const char SoftHyphen = '\u00AD';
        private const char ZeroWidthJoiner = '\u200D';
        private const string RegionalIndicatorA = "\U0001F1E6";

        /// <summary>
        /// Finds the line break opportunities of <paramref name="text"/>: one entry for each UTF-16 index and one for its end
        /// (see <see cref="LineBreaker.FindOpportunities"/>). <paramref name="precedingRegionalIndicators"/> is how many regional
        /// indicators end the text before this box's, which decides whether the first one completes a flag.
        /// </summary>
        internal static LineBreakOpportunity[] Find(string text, PeachPDF.CSS.WordBreak wordBreak, int precedingRegionalIndicators = 0,
            PeachPDF.CSS.LineBreak lineBreak = PeachPDF.CSS.LineBreak.Auto)
        {
            if (text.Length == 0)
            {
                return [LineBreakOpportunity.Mandatory];
            }

            var options = new LineBreakOptions
            {
                WordBreak = wordBreak switch
                {
                    PeachPDF.CSS.WordBreak.BreakAll => WordBreakMode.BreakAll,
                    PeachPDF.CSS.WordBreak.KeepAll => WordBreakMode.KeepAll,
                    _ => WordBreakMode.Normal,
                },
                Strictness = lineBreak switch
                {
                    PeachPDF.CSS.LineBreak.Loose => LineBreakStrictness.Loose,
                    PeachPDF.CSS.LineBreak.Normal => LineBreakStrictness.Normal,
                    PeachPDF.CSS.LineBreak.Strict => LineBreakStrictness.Strict,
                    PeachPDF.CSS.LineBreak.Anywhere => LineBreakStrictness.Anywhere,
                    _ => LineBreakStrictness.Auto,
                },
            };

            // A soft hyphen is a hyphenation candidate the flow decides on when it has to break a word, not a break opportunity of
            // its own, so it is hidden from the algorithm behind a joiner, which never lets a line end after it.
            var analysed = text.Contains(SoftHyphen) ? text.Replace(SoftHyphen, ZeroWidthJoiner) : text;

            // An odd number of regional indicators before this text leaves a flag open, which the first indicator here completes:
            // one more indicator in front makes the algorithm see it, and its two code units are dropped from the answer.
            bool openFlag = precedingRegionalIndicators % 2 != 0;
            if (openFlag)
            {
                analysed = RegionalIndicatorA + analysed;
            }

            var opportunities = LineBreaker.FindOpportunities(analysed, options);
            SuppressBeforeLetters(analysed, opportunities);
            return openFlag ? opportunities[RegionalIndicatorA.Length..] : opportunities;
        }

        /// <summary>Whether a code point is a letter of a script that breaks between characters (ideographs, kana, Hangul), where the algorithm's break after a solidus stands.</summary>
        private static bool IsIdeographicLetter(int codePoint) =>
            codePoint is >= 0x1100 and <= 0x11FF or >= 0x3040 and <= 0x30FF or >= 0x3400 and <= 0x4DBF or >= 0x4E00 and <= 0x9FFF
                or >= 0xAC00 and <= 0xD7AF or >= 0xF900 and <= 0xFAFF or >= 0xFF66 and <= 0xFF9F or >= 0x20000 and <= 0x3FFFF;

        /// <summary>
        /// The deviation CSS Text 3 suggests for interoperability: no break between an exclamation mark, a solidus or a vertical line
        /// and a following letter (not an ideograph, kana or Hangul character), so that <c>!important</c>, <c>23/Jan/Feb</c> and <c>a|b</c> stay whole.
        /// </summary>
        private static void SuppressBeforeLetters(string text, LineBreakOpportunity[] opportunities)
        {
            for (int i = 1; i < text.Length; i++)
            {
                if (opportunities[i] != LineBreakOpportunity.Allowed || text[i - 1] is not ('!' or '/' or '|'))
                {
                    continue;
                }

                Rune.DecodeFromUtf16(text.AsSpan(i), out var rune, out _);
                if (Rune.IsLetter(rune) && !IsIdeographicLetter(rune.Value))
                {
                    opportunities[i] = LineBreakOpportunity.Prohibited;
                }
            }
        }
    }
}
