using PeachBrackets.Css;
using System;
using System.Collections.Generic;
using System.Text;

namespace PeachBrackets.Html.Utils
{
    /// <summary>
    /// The ten longhand East Asian counter styles of CSS Counter Styles Level 3 section 7.1: <c>cjk-ideographic</c> and the Chinese,
    /// Japanese and Korean formal and informal styles.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each style is a digit row (1-9, and 0), a unit row (1000, 100, 10) and, beyond 9999, myriad group units (10000, 10^8). The spec
    /// states the 1-9999 core of every style as one additive-symbols table - the product of those rows - and the Japanese styles stop
    /// there (outside -9999..9999 they fall back to <c>cjk-decimal</c>). The Chinese styles and the Korean ones go on in groups of four
    /// digits, which is what browsers do (Chromium writes 10000 as <c>一万</c> and <c>일만</c>, and the Japanese styles as <c>一〇〇〇〇</c>).
    /// </para>
    /// <para>
    /// Chinese numerals are not purely additive: a <c>零</c> stands for a run of zero digits between two non-zero ones, and between
    /// groups when the lower group is below 1000 or a group was skipped, and never trails (<c>1100</c> is <c>一千一百</c>, <c>1001</c> is
    /// <c>一千零一</c>). An informal style leaves out the <c>一</c> before a <c>十</c> that opens a group (<c>十</c>, <c>十一</c>,
    /// <c>一万零十</c>) but keeps it inside a larger one (<c>一百一十</c>, <c>一千零一十</c>). The Korean and Japanese styles write no <c>零</c>
    /// (<c>101</c> is <c>百一</c>), and an informal one leaves out every <c>一</c> before a unit.
    /// </para>
    /// </remarks>
    internal static class EastAsianCounterStyles
    {
        private enum Family { Chinese, Japanese, Korean }

        private sealed record Style(
            Family Family, bool Informal, string[] Digits, string Thousand, string Hundred, string Ten, string[] GroupUnits, string Negative, string Suffix);

        private static readonly string[] SimplifiedInformalDigits = ["零", "一", "二", "三", "四", "五", "六", "七", "八", "九"];
        private static readonly string[] SimplifiedFormalDigits = ["零", "壹", "贰", "叁", "肆", "伍", "陆", "柒", "捌", "玖"];
        private static readonly string[] TraditionalFormalDigits = ["零", "壹", "貳", "參", "肆", "伍", "陸", "柒", "捌", "玖"];
        private static readonly string[] JapaneseInformalDigits = ["〇", "一", "二", "三", "四", "五", "六", "七", "八", "九"];
        private static readonly string[] JapaneseFormalDigits = ["零", "壱", "弐", "参", "四", "伍", "六", "七", "八", "九"];
        private static readonly string[] HangulDigits = ["영", "일", "이", "삼", "사", "오", "육", "칠", "팔", "구"];
        private static readonly string[] HanjaInformalDigits = ["零", "一", "二", "三", "四", "五", "六", "七", "八", "九"];
        private static readonly string[] HanjaFormalDigits = ["零", "壹", "貳", "參", "四", "五", "六", "七", "八", "九"];

        private static readonly string[] SimplifiedGroups = ["", "万", "亿"];
        private static readonly string[] TraditionalGroups = ["", "萬", "億"];
        private static readonly string[] HangulGroups = ["", "만", "억"];

        private static readonly Dictionary<string, Style> Styles = new(StringComparer.OrdinalIgnoreCase)
        {
            [Keywords.SimpChineseInformal] = new(Family.Chinese, true, SimplifiedInformalDigits, "千", "百", "十", SimplifiedGroups, "负", "、"),
            [Keywords.SimpChineseFormal] = new(Family.Chinese, false, SimplifiedFormalDigits, "仟", "佰", "拾", SimplifiedGroups, "负", "、"),
            [Keywords.TradChineseInformal] = new(Family.Chinese, true, SimplifiedInformalDigits, "千", "百", "十", TraditionalGroups, "負", "、"),
            [Keywords.CjkIdeographic] = new(Family.Chinese, true, SimplifiedInformalDigits, "千", "百", "十", TraditionalGroups, "負", "、"),
            [Keywords.TradChineseFormal] = new(Family.Chinese, false, TraditionalFormalDigits, "仟", "佰", "拾", TraditionalGroups, "負", "、"),
            [Keywords.JapaneseInformal] = new(Family.Japanese, true, JapaneseInformalDigits, "千", "百", "十", [], "マイナス", "、"),
            [Keywords.JapaneseFormal] = new(Family.Japanese, false, JapaneseFormalDigits, "阡", "百", "拾", [], "マイナス", "、"),
            [Keywords.KoreanHangulFormal] = new(Family.Korean, false, HangulDigits, "천", "백", "십", HangulGroups, "마이너스 ", ", "),
            [Keywords.KoreanHanjaInformal] = new(Family.Korean, true, HanjaInformalDigits, "千", "百", "十", TraditionalGroups, "마이너스 ", ", "),
            [Keywords.KoreanHanjaFormal] = new(Family.Korean, false, HanjaFormalDigits, "仟", "百", "拾", TraditionalGroups, "마이너스 ", ", "),
        };

        /// <summary>Whether <paramref name="style"/> names one of the ten styles.</summary>
        internal static bool IsEastAsianCounterStyle(string style) => Styles.ContainsKey(style);

        /// <summary>The suffix the style puts after a list marker (section 7.1: 、 for Chinese and Japanese, a comma and space for Korean), or null for another style.</summary>
        internal static string? SuffixOf(string style) => Styles.TryGetValue(style, out var s) ? s.Suffix : null;

        /// <summary>
        /// The counter <paramref name="number"/> written in <paramref name="style"/>, or null when the style is not one of the ten or the
        /// number is outside the style's range (the caller then uses its <c>cjk-decimal</c> fallback).
        /// </summary>
        internal static string? Format(int number, string style)
        {
            if (!Styles.TryGetValue(style, out var s)) return null;
            if (s.Family == Family.Japanese && number is < -9999 or > 9999) return null;

            var sb = new StringBuilder();
            if (number < 0) sb.Append(s.Negative);

            var magnitude = Math.Abs((long)number);
            if (magnitude == 0) return sb.Append(s.Digits[0]).ToString();

            // Groups of four digits, least significant first.
            var groups = new List<int>(3);
            for (var rest = magnitude; rest > 0; rest /= 10000) groups.Add((int)(rest % 10000));

            var written = false;
            var zeroPending = false;

            for (var i = groups.Count - 1; i >= 0; i--)
            {
                var group = groups[i];
                if (group == 0)
                {
                    zeroPending |= written;
                    continue;
                }

                if (s.Family == Family.Chinese && written && (zeroPending || group < 1000)) sb.Append(s.Digits[0]);
                zeroPending = false;

                AppendGroup(sb, group, s, i > 0);
                if (i > 0) sb.Append(s.GroupUnits[i]);
                written = true;
            }

            return sb.ToString();
        }

        /// <summary>Writes one group of four digits (1-9999): each non-zero digit with its unit, and for Chinese a <c>零</c> for zeros between two of them.</summary>
        private static void AppendGroup(StringBuilder sb, int group, Style s, bool beforeGroupUnit)
        {
            var units = new[] { s.Thousand, s.Hundred, s.Ten };
            var started = false;
            var zeroPending = false;

            for (var position = 3; position >= 0; position--)
            {
                var digit = group / (int)Math.Pow(10, position) % 10;
                if (digit == 0)
                {
                    zeroPending |= started;
                    continue;
                }

                if (zeroPending && s.Family == Family.Chinese) sb.Append(s.Digits[0]);
                zeroPending = false;
                started = true;

                if (position == 0)
                {
                    // A lone 1 in front of a myriad unit is left out by Korean informal (萬, not 一萬).
                    if (!(s.Family == Family.Korean && s.Informal && beforeGroupUnit && group == 1)) sb.Append(s.Digits[digit]);
                    continue;
                }

                var leavesOutOne = digit == 1 && s.Informal && s.Family switch
                {
                    Family.Chinese => position == 1 && group is >= 10 and <= 19,
                    _ => true,
                };

                if (!leavesOutOne) sb.Append(s.Digits[digit]);
                sb.Append(units[3 - position]);
            }
        }
    }
}
