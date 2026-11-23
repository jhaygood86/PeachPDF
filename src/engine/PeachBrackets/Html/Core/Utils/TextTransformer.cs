using System;
using System.Collections.Generic;
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
                    return MapCase(text, upper: true);
                }
                case TextTransform.Lowercase:
                {
                    return MapCase(text, upper: false);
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
                case TextTransform.FullSizeKana:
                {
                    var chars = text.ToCharArray();
                    for (var i = 0; i < chars.Length; i++)
                        chars[i] = ToFullSizeKana(chars[i]);
                    return new string(chars);
                }
                default:
                    return text;
            }
        }

        /// <summary>
        /// Applies <paramref name="transform"/> to <paramref name="text"/> as <see cref="Apply"/> does, except that <c>uppercase</c>,
        /// <c>lowercase</c> and <c>capitalize</c> use Unicode's full case mappings for <paramref name="language"/> (the German sharp s
        /// becomes <c>SS</c>, or <c>Ss</c> at the start of a word; a Greek final sigma is chosen from its context; Turkish dotted and dotless
        /// i), and <c>full-width</c> first collapses the white space the box collapses, as css-text-3 section 4.1 does before it applies
        /// the transform, so each run that survives becomes one ideographic space. Both can change the text's length;
        /// <paramref name="sourceIndices"/> then says, for each UTF-16 index of the result and for its end, the index of the character of
        /// <paramref name="text"/> it came from, and is <see langword="null"/> when the length did not change.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <param name="transform">The transform.</param>
        /// <param name="language">The BCP 47 language of the text, or <see langword="null"/> when it has none.</param>
        /// <param name="collapseSpaces">Whether runs of spaces and tabs collapse (<c>white-space-collapse</c> is not a preserving value).</param>
        /// <param name="collapseBreaks">Whether segment breaks collapse with them.</param>
        /// <param name="sourceIndices">Where each character of the result came from, or <see langword="null"/>.</param>
        public static string ApplyFull(string text, TextTransform transform, string? language, bool collapseSpaces, bool collapseBreaks,
            out int[]? sourceIndices)
        {
            sourceIndices = null;
            if (string.IsNullOrEmpty(text))
                return text;

            switch (transform)
            {
                case TextTransform.Uppercase:
                    return PeachDrawing.Text.Unicode.CaseMapper.ToUpper(text, language, out sourceIndices);
                case TextTransform.Lowercase:
                    return PeachDrawing.Text.Unicode.CaseMapper.ToLower(text, language, out sourceIndices);
                case TextTransform.Capitalize:
                {
                    var atWordStart = true;
                    return ApplyCapitalizeFull(text, language, ref atWordStart, out sourceIndices);
                }
                case TextTransform.FullWidth when collapseSpaces:
                    return ApplyFullWidthCollapsing(text, collapseBreaks, out sourceIndices);
                default:
                    return Apply(text, transform);
            }
        }

        /// <summary>
        /// <c>capitalize</c> with Unicode's full title case mappings: the first letter of each word is mapped to title case, which can be
        /// longer than the letter (<c>ß</c> becomes <c>Ss</c>) and is not always its uppercase (<c>ǆ</c> becomes <c>ǅ</c>).
        /// </summary>
        public static string ApplyCapitalizeFull(string text, string? language, ref bool atWordStart, out int[]? sourceIndices)
        {
            var result = new StringBuilder(text.Length);
            List<int>? sources = null;

            for (var i = 0; i < text.Length;)
            {
                Rune.DecodeFromUtf16(text.AsSpan(i), out var rune, out var length);
                var piece = text.Substring(i, length);

                if (Rune.IsWhiteSpace(rune))
                {
                    atWordStart = true;
                }
                else if (atWordStart && Rune.IsLetter(rune))
                {
                    piece = PeachDrawing.Text.Unicode.CaseMapper.ToTitle(piece, language);
                    atWordStart = false;
                }

                AppendMapped(result, ref sources, piece, i, length);
                i += length;
            }

            sourceIndices = FinishMap(sources, text.Length);
            return result.ToString();
        }

        private static string ApplyFullWidthCollapsing(string text, bool collapseBreaks, out int[]? sourceIndices)
        {
            var result = new StringBuilder(text.Length);
            List<int>? sources = null;

            for (var i = 0; i < text.Length;)
            {
                if (IsCollapsible(text[i], collapseBreaks))
                {
                    var runStart = i;
                    while (i < text.Length && IsCollapsible(text[i], collapseBreaks)) i++;
                    AppendMapped(result, ref sources, "\u3000", runStart, i - runStart);
                }
                else
                {
                    result.Append(ToFullWidth(text[i]));
                    if (sources is not null) sources.Add(i);
                    i++;
                }
            }

            sourceIndices = FinishMap(sources, text.Length);
            return result.ToString();
        }

        private static bool IsCollapsible(char c, bool collapseBreaks) => c is ' ' or '\t' or '\r' or '\f' || (collapseBreaks && c == '\n');

        /// <summary>Appends <paramref name="piece"/>, the mapping of the <paramref name="sourceLength"/> characters at <paramref name="sourceIndex"/>, keeping the source map once a length has changed.</summary>
        private static void AppendMapped(StringBuilder result, ref List<int>? sources, string piece, int sourceIndex, int sourceLength)
        {
            if (sources is null && piece.Length != sourceLength)
            {
                sources = new List<int>(result.Length + piece.Length + 16);
                for (var before = 0; before < result.Length; before++) sources.Add(before);
            }

            result.Append(piece);
            if (sources is not null)
                for (var c = 0; c < piece.Length; c++) sources.Add(sourceIndex);
        }

        private static int[]? FinishMap(List<int>? sources, int sourceLength)
        {
            if (sources is null) return null;
            sources.Add(sourceLength);
            return [.. sources];
        }

        /// <summary>
        /// Maps <paramref name="text"/> to upper or lower case one code point at a time, so a letter outside the Basic Multilingual Plane
        /// (Deseret, Osage, Adlam ...) maps as a whole rather than as two lone surrogates. The mapping is Unicode's simple case mapping, so
        /// the text keeps its length; the dotless i, which .NET's invariant mapping leaves alone, becomes <c>I</c>.
        /// </summary>
        private static string MapCase(string text, bool upper)
        {
            var result = new StringBuilder(text.Length);
            foreach (var rune in text.EnumerateRunes())
            {
                var mapped = upper
                    ? rune.Value == 0x0131 ? new Rune('I') : Rune.ToUpperInvariant(rune)
                    : Rune.ToLowerInvariant(rune);
                result.Append(mapped.ToString());
            }

            return result.ToString();
        }

        /// <summary>
        /// <c>full-size-kana</c> (CSS Text 3 section 2.1): a small kana becomes the full-size kana it is a small form of (hiragana, katakana, the katakana
        /// phonetic extensions and the half-width katakana). Every other character is returned unchanged.
        /// </summary>
        public static char ToFullSizeKana(char c) => c switch
        {
            'ぁ' => 'あ', 'ぃ' => 'い', 'ぅ' => 'う', 'ぇ' => 'え', 'ぉ' => 'お', 'っ' => 'つ', 'ゃ' => 'や', 'ゅ' => 'ゆ', 'ょ' => 'よ', 'ゎ' => 'わ',
            'ゕ' => 'か', 'ゖ' => 'け',
            'ァ' => 'ア', 'ィ' => 'イ', 'ゥ' => 'ウ', 'ェ' => 'エ', 'ォ' => 'オ', 'ッ' => 'ツ', 'ャ' => 'ヤ', 'ュ' => 'ユ', 'ョ' => 'ヨ', 'ヮ' => 'ワ',
            'ヵ' => 'カ', 'ヶ' => 'ケ',
            'ㇰ' => 'ク', 'ㇱ' => 'シ', 'ㇲ' => 'ス', 'ㇳ' => 'ト', 'ㇴ' => 'ヌ', 'ㇵ' => 'ハ', 'ㇶ' => 'ヒ', 'ㇷ' => 'フ', 'ㇸ' => 'ヘ', 'ㇹ' => 'ホ',
            'ㇺ' => 'ム', 'ㇻ' => 'ラ', 'ㇼ' => 'リ', 'ㇽ' => 'ル', 'ㇾ' => 'レ', 'ㇿ' => 'ロ',
            'ｧ' => 'ｱ', 'ｨ' => 'ｲ', 'ｩ' => 'ｳ', 'ｪ' => 'ｴ', 'ｫ' => 'ｵ', 'ｯ' => 'ﾂ', 'ｬ' => 'ﾔ', 'ｭ' => 'ﾕ', 'ｮ' => 'ﾖ',
            _ => c,
        };

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
