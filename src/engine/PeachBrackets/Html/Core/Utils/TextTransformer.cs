using System.Text;
using PeachBrackets.Css;

namespace PeachBrackets.Html.Utils
{
    /// <summary>
    /// Applies CSS Text Module Level 3's <c>text-transform</c> to a string - extracted from
    /// <see cref="Dom.CssBox"/>'s own (private, HTML-only) implementation so SVG text
    /// (<see cref="Svg.SvgTreeBuilder"/>) can apply the exact same grammar, per this repo's "one parser
    /// per grammar" convention (see CLAUDE.md). <see cref="ApplyCapitalize"/> is split out from
    /// <see cref="Apply"/> because SVG's whitespace-collapsing model (<c>SvgTreeBuilder.TextWhitespaceState</c>)
    /// already threads word-boundary state across an entire <c>&lt;text&gt;</c> subtree's worth of
    /// fragments/tspans as one shared object - <c>capitalize</c> needs that same cross-fragment
    /// word-start tracking (a word split across a <c>&lt;tspan&gt;</c> boundary must still capitalize
    /// correctly), which a single whole-string call has no way to carry between calls.
    /// </summary>
    internal static class TextTransformer
    {
        /// <summary>Applies <paramref name="transform"/> to the whole of <paramref name="text"/> as one
        /// independent unit (HTML's own per-box usage - each box's text is transformed on its own,
        /// with no cross-box word-boundary tracking for <c>capitalize</c>).</summary>
        public static string Apply(string text, TextTransform transform)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            switch (transform)
            {
                case TextTransform.Uppercase:
                {
                    var chars = text.ToCharArray();
                    for (var i = 0; i < chars.Length; i++)
                        chars[i] = char.ToUpperInvariant(chars[i]);
                    return new string(chars);
                }
                case TextTransform.Lowercase:
                {
                    var chars = text.ToCharArray();
                    for (var i = 0; i < chars.Length; i++)
                        chars[i] = char.ToLowerInvariant(chars[i]);
                    return new string(chars);
                }
                case TextTransform.Capitalize:
                {
                    var atWordStart = true;
                    return ApplyCapitalize(text, ref atWordStart);
                }
                case TextTransform.FullWidth:
                {
                    var chars = text.ToCharArray();
                    for (var i = 0; i < chars.Length; i++)
                        chars[i] = ToFullWidth(chars[i]);
                    return new string(chars);
                }
                default:
                    return text;
            }
        }

        /// <summary>Applies <c>capitalize</c> to <paramref name="text"/>, threading word-start state in
        /// from (and back out to) <paramref name="atWordStart"/> - lets a caller apply this across
        /// several fragments in document order (e.g. text split across <c>&lt;tspan&gt;</c> boundaries)
        /// as if it were one continuous string.</summary>
        public static string ApplyCapitalize(string text, ref bool atWordStart)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var chars = text.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (char.IsWhiteSpace(chars[i]))
                {
                    atWordStart = true;
                }
                else if (atWordStart && char.IsLetter(chars[i]))
                {
                    chars[i] = char.ToUpperInvariant(chars[i]);
                    atWordStart = false;
                }
            }
            return new string(chars);
        }

        /// <summary>
        /// Maps a single character to its fullwidth form per Unicode's &lt;wide&gt; and &lt;narrow&gt;
        /// decomposition mappings (used by CSS Text Module Level 3's <c>text-transform: full-width</c>).
        /// ASCII 0x21-0x7E map to U+FF01-FF5E (offset by U+FEE0), space maps to the ideographic space
        /// U+3000, a handful of Latin-1 currency/symbol characters map to their own fullwidth forms in
        /// the U+FFE0-FFE6 range, and the halfwidth katakana (U+FF61-FF9F), halfwidth Hangul jamo
        /// (U+FFA0-FFDC) and halfwidth symbol forms (U+FFE8-FFEE) map to their normal-width characters.
        /// The mapping is one code point to one code point, so the text keeps its length: a halfwidth
        /// voiced-sound mark (U+FF9E/FF9F) becomes the combining mark U+3099/309A rather than composing
        /// into the preceding kana, as in Chromium. Characters with no fullwidth form are returned unchanged.
        /// </summary>
        public static char ToFullWidth(char c)
        {
            switch (c)
            {
                case ' ':
                    return '　';
                case >= '!' and <= '~':
                    return (char)(c + 0xfee0);
                case '¢':
                    return '￠';
                case '£':
                    return '￡';
                case '¬':
                    return '￢';
                case '¯':
                    return '￣';
                case '¦':
                    return '￤';
                case '¥':
                    return '￥';
                case '₩':
                    return '￦';
                case '⦅':
                    return '｟';
                case '⦆':
                    return '｠';
                case >= '｡' and <= 'ﾟ':
                    return HalfwidthKatakana[c - '｡'];
                case 'ﾠ':
                    return 'ㅤ';
                case >= 'ﾡ' and <= 'ﾾ':
                    return (char)(c - 0xFFA1 + 0x3131);
                case >= 'ￂ' and <= 'ￇ':
                    return (char)(c - 0xFFC2 + 0x314F);
                case >= 'ￊ' and <= 'ￏ':
                    return (char)(c - 0xFFCA + 0x3155);
                case >= 'ￒ' and <= 'ￗ':
                    return (char)(c - 0xFFD2 + 0x315B);
                case >= 'ￚ' and <= 'ￜ':
                    return (char)(c - 0xFFDA + 0x3161);
                case '￨':
                    return '│';
                case >= '￩' and <= '￬':
                    return (char)(c - 0xFFE9 + 0x2190);
                case '￭':
                    return '■';
                case '￮':
                    return '○';
                default:
                    return c;
            }
        }

        // U+FF61..U+FF9F: halfwidth CJK punctuation, katakana and the voiced/semi-voiced sound marks, to their normal-width forms.
        private static readonly char[] HalfwidthKatakana =
        [
            '。', '「', '」', '、', '・', 'ヲ',
            'ァ', 'ィ', 'ゥ', 'ェ', 'ォ', 'ャ', 'ュ', 'ョ', 'ッ', 'ー',
            'ア', 'イ', 'ウ', 'エ', 'オ',
            'カ', 'キ', 'ク', 'ケ', 'コ',
            'サ', 'シ', 'ス', 'セ', 'ソ',
            'タ', 'チ', 'ツ', 'テ', 'ト',
            'ナ', 'ニ', 'ヌ', 'ネ', 'ノ',
            'ハ', 'ヒ', 'フ', 'ヘ', 'ホ',
            'マ', 'ミ', 'ム', 'メ', 'モ',
            'ヤ', 'ユ', 'ヨ',
            'ラ', 'リ', 'ル', 'レ', 'ロ',
            'ワ', 'ン', '゙', '゚',
        ];
    }
}
