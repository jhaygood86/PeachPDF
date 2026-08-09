// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
// 
// - Sun Tsu,
// "The Art of War"

using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Network;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Utility methods for general stuff.
    /// </summary>
    internal static class CommonUtils
    {
        /// <summary>
        /// Table to convert numbers into roman digits
        /// </summary>
        private static readonly string[,] _romanDigitsTable =
        {
            { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" },
            { "", "X", "XX", "XXX", "XL", "L", "LX", "LXX", "LXXX", "XC" },
            { "", "C", "CC", "CCC", "CD", "D", "DC", "DCC", "DCCC", "CM" },
            {
                "", "M", "MM", "MMM", "M(V)", "(V)", "(V)M",
                "(V)MM", "(V)MMM", "M(X)"
            }
        };

        /// <summary>
        /// Table to convert numbers into hebrew digits - CSS Counter Styles Level 3 §6.1's "hebrew"
        /// <c>@counter-style</c>. Row 3 (thousands) is each units-row letter with a geresh (U+05F3)
        /// appended, per the spec's <c>additive-symbols</c> list (e.g. 1000 is "\5D0\5F3", the same
        /// letter as 1 plus a geresh) - added here since the 3-row table previously in this file
        /// silently dropped the thousands digit entirely for any value >= 1000. Capped at 9999 (not
        /// the spec's full 1-10999 range) to match the same 4-row/9999 depth already used for
        /// Armenian/Georgian below, rather than adding a 10000 special case matching neither pattern.
        /// 15 and 16 need a further, non-tabular override - see <see cref="ConvertToHebrewNumber"/>.
        /// </summary>
        private static readonly string[,] _hebrewDigitsTable =
        {
            { "א", "ב", "ג", "ד", "ה", "ו", "ז", "ח", "ט" },
            { "י", "כ", "ל", "מ", "נ", "ס", "ע", "פ", "צ" },
            { "ק", "ר", "ש", "ת", "תק", "תר", "תש", "תת", "תתק", },
            { "א׳", "ב׳", "ג׳", "ד׳", "ה׳", "ו׳", "ז׳", "ח׳", "ט׳" }
        };

        private static readonly string[,] _georgianDigitsTable =
        {
            { "ა", "ბ", "გ", "დ", "ე", "ვ", "ზ", "ჱ", "თ" },
            { "ი", "პ", "ლ", "მ", "ნ", "ჲ", "ო", "პ", "ჟ" },
            { "რ", "ს", "ტ", "ჳ", "ფ", "ქ", "ღ", "ყ", "შ" }
        };

        /// <summary>
        /// Table to convert numbers into (uppercase) armenian digits - CSS Counter Styles Level 3
        /// §6.1's "armenian"/"upper-armenian" <c>@counter-style</c> (the two names are aliases of the
        /// same style). Row 3 (thousands) added for the same reason as <see cref="_hebrewDigitsTable"/>'s
        /// - the pre-existing 3-row table silently dropped the thousands digit for any value >= 1000.
        /// </summary>
        private static readonly string[,] _armenianDigitsTable =
        {
            { "Ա", "Բ", "Գ", "Դ", "Ե", "Զ", "Է", "Ը", "Թ" },
            { "Ժ", "Ի", "Լ", "Խ", "Ծ", "Կ", "Հ", "Ձ", "Ղ" },
            { "Ճ", "Մ", "Յ", "Ն", "Շ", "Ո", "Չ", "Պ", "Ջ" },
            { "Ռ", "Ս", "Վ", "Տ", "Ր", "Ց", "Ւ", "Փ", "Ք" }
        };

        /// <summary>Table to convert numbers into lowercase armenian digits - CSS Counter Styles Level 3
        /// §6.1's "lower-armenian" <c>@counter-style</c>, the lowercase counterpart of
        /// <see cref="_armenianDigitsTable"/>.</summary>
        private static readonly string[,] _lowerArmenianDigitsTable =
        {
            { "ա", "բ", "գ", "դ", "ե", "զ", "է", "ը", "թ" },
            { "ժ", "ի", "լ", "խ", "ծ", "կ", "հ", "ձ", "ղ" },
            { "ճ", "մ", "յ", "ն", "շ", "ո", "չ", "պ", "ջ" },
            { "ռ", "ս", "վ", "տ", "ր", "ց", "ւ", "փ", "ք" }
        };

        /// <summary>Dictionary-order hiragana lettering - CSS Counter Styles Level 3 §6.2's "hiragana"
        /// <c>@counter-style</c>.</summary>
        private static readonly string[] _hiraganaDigitsTable =
        [
            "あ", "ぃ", "ぅ", "ぇ", "ぉ", "か", "き", "く", "け", "こ", "さ", "し", "す", "せ", "そ", "た", "ち", "つ", "て", "と", "な", "に", "ぬ", "ね", "の", "は", "ひ", "ふ", "へ", "ほ", "ま", "み", "む", "め", "も", "ゃ", "ゅ", "ょ", "ら", "り", "る", "れ", "ろ", "ゎ", "ゐ", "ゑ", "を", "ん"
        ];

        /// <summary>
        /// Iroha-order hiragana lettering - CSS Counter Styles Level 3 §6.2's "hiragana-iroha"
        /// <c>@counter-style</c>. This is a genuinely different 47-character ORDER from
        /// <see cref="_hiraganaDigitsTable"/> (i, ro, ha, ni, ho, he, to, ... vs the dictionary a, i, u,
        /// e, o, ka, ki, ...), not a formatting variant of it - previously this style silently reused
        /// the dictionary-order table, which produced dictionary-order output under the "iroha" name.
        /// </summary>
        private static readonly string[] _hiraganaIrohaDigitsTable =
        [
            "い", "ろ", "は", "に", "ほ", "へ", "と", "ち", "り", "ぬ",
            "る", "を", "わ", "か", "よ", "た", "れ", "そ", "つ", "ね",
            "な", "ら", "む", "う", "ゐ", "の", "お", "く", "や", "ま",
            "け", "ふ", "こ", "え", "て", "あ", "さ", "き", "ゆ", "め",
            "み", "し", "ゑ", "ひ", "も", "せ", "す"
        ];

        /// <summary>Dictionary-order katakana lettering - CSS Counter Styles Level 3 §6.2's "katakana"
        /// <c>@counter-style</c>.</summary>
        private static readonly string[] _satakanaDigitsTable =
        [
            "ア", "イ", "ウ", "エ", "オ", "カ", "キ", "ク", "ケ", "コ", "サ", "シ", "ス", "セ", "ソ", "タ", "チ", "ツ", "テ", "ト", "ナ", "ニ", "ヌ", "ネ", "ノ", "ハ", "ヒ", "フ", "ヘ", "ホ", "マ", "ミ", "ム", "メ", "モ", "ヤ", "ユ", "ヨ", "ラ", "リ", "ル", "レ", "ロ", "ワ", "ヰ", "ヱ", "ヲ", "ン"
        ];

        /// <summary>
        /// Iroha-order katakana lettering - CSS Counter Styles Level 3 §6.2's "katakana-iroha"
        /// <c>@counter-style</c>. See <see cref="_hiraganaIrohaDigitsTable"/>'s remarks - same fix, same
        /// reason (this style previously silently reused the dictionary-order table too).
        /// </summary>
        private static readonly string[] _katakanaIrohaDigitsTable =
        [
            "イ", "ロ", "ハ", "ニ", "ホ", "ヘ", "ト", "チ", "リ", "ヌ",
            "ル", "ヲ", "ワ", "カ", "ヨ", "タ", "レ", "ソ", "ツ", "ネ",
            "ナ", "ラ", "ム", "ウ", "ヰ", "ノ", "オ", "ク", "ヤ", "マ",
            "ケ", "フ", "コ", "エ", "テ", "ア", "サ", "キ", "ユ", "メ",
            "ミ", "シ", "ヱ", "ヒ", "モ", "セ", "ス"
        ];

        /// <summary>
        /// Per-script decimal-digit-substitution tables (index 0 = the script's own "0" glyph, ...
        /// index 9 = its "9" glyph) for CSS Counter Styles Level 3 §6.1's "numeric" system styles -
        /// the same base-10 place-value system as western decimal, just with different digit glyphs.
        /// Codepoints are sourced verbatim from the spec's own <c>@counter-style</c> definitions.
        /// </summary>
        private static readonly string[] _arabicIndicDigitsTable =
            ["٠", "١", "٢", "٣", "٤", "٥", "٦", "٧", "٨", "٩"];

        private static readonly string[] _bengaliDigitsTable =
            ["০", "১", "২", "৩", "৪", "৫", "৬", "৭", "৮", "৯"];

        private static readonly string[] _cambodianDigitsTable =
            ["០", "១", "២", "៣", "៤", "៥", "៦", "៧", "៨", "៩"];

        private static readonly string[] _cjkDecimalDigitsTable =
            ["〇", "一", "二", "三", "四", "五", "六", "七", "八", "九"];

        private static readonly string[] _devanagariDigitsTable =
            ["०", "१", "२", "३", "४", "५", "६", "७", "८", "९"];

        private static readonly string[] _gujaratiDigitsTable =
            ["૦", "૧", "૨", "૩", "૪", "૫", "૬", "૭", "૮", "૯"];

        private static readonly string[] _gurmukhiDigitsTable =
            ["੦", "੧", "੨", "੩", "੪", "੫", "੬", "੭", "੮", "੯"];

        private static readonly string[] _kannadaDigitsTable =
            ["೦", "೧", "೨", "೩", "೪", "೫", "೬", "೭", "೮", "೯"];

        private static readonly string[] _laoDigitsTable =
            ["໐", "໑", "໒", "໓", "໔", "໕", "໖", "໗", "໘", "໙"];

        private static readonly string[] _malayalamDigitsTable =
            ["൦", "൧", "൨", "൩", "൪", "൫", "൬", "൭", "൮", "൯"];

        private static readonly string[] _mongolianDigitsTable =
            ["᠐", "᠑", "᠒", "᠓", "᠔", "᠕", "᠖", "᠗", "᠘", "᠙"];

        private static readonly string[] _myanmarDigitsTable =
            ["၀", "၁", "၂", "၃", "၄", "၅", "၆", "၇", "၈", "၉"];

        private static readonly string[] _oriyaDigitsTable =
            ["୦", "୧", "୨", "୩", "୪", "୫", "୬", "୭", "୮", "୯"];

        private static readonly string[] _persianDigitsTable =
            ["۰", "۱", "۲", "۳", "۴", "۵", "۶", "۷", "۸", "۹"];

        private static readonly string[] _tamilDigitsTable =
            ["௦", "௧", "௨", "௩", "௪", "௫", "௬", "௭", "௮", "௯"];

        private static readonly string[] _teluguDigitsTable =
            ["౦", "౧", "౨", "౩", "౪", "౫", "౬", "౭", "౮", "౯"];

        private static readonly string[] _thaiDigitsTable =
            ["๐", "๑", "๒", "๓", "๔", "๕", "๖", "๗", "๘", "๙"];

        private static readonly string[] _tibetanDigitsTable =
            ["༠", "༡", "༢", "༣", "༤", "༥", "༦", "༧", "༨", "༩"];

        private static readonly FrozenDictionary<string, string[]> _positionalDigitTables =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { Keywords.ArabicIndic, _arabicIndicDigitsTable },
                { Keywords.Bengali, _bengaliDigitsTable },
                { Keywords.Cambodian, _cambodianDigitsTable },
                { Keywords.Khmer, _cambodianDigitsTable },
                { Keywords.CjkDecimal, _cjkDecimalDigitsTable },
                { Keywords.Devanagari, _devanagariDigitsTable },
                { Keywords.Gujarati, _gujaratiDigitsTable },
                { Keywords.Gurmukhi, _gurmukhiDigitsTable },
                { Keywords.Kannada, _kannadaDigitsTable },
                { Keywords.Lao, _laoDigitsTable },
                { Keywords.Malayalam, _malayalamDigitsTable },
                { Keywords.Mongolian, _mongolianDigitsTable },
                { Keywords.Myanmar, _myanmarDigitsTable },
                { Keywords.Oriya, _oriyaDigitsTable },
                { Keywords.Persian, _persianDigitsTable },
                { Keywords.Tamil, _tamilDigitsTable },
                { Keywords.Telugu, _teluguDigitsTable },
                { Keywords.Thai, _thaiDigitsTable },
                { Keywords.Tibetan, _tibetanDigitsTable }
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Fixed (finite, non-repeating) symbol lists for CSS Counter Styles Level 3 §6.4's
        /// "cjk-earthly-branch"/"cjk-heavenly-stem" <c>@counter-style</c>s - a value outside 1..N falls
        /// back to decimal, same as the spec's default <c>fallback: decimal</c> for any counter style
        /// that doesn't declare its own.
        /// </summary>
        private static readonly string[] _cjkEarthlyBranchSymbols =
        [
            "子", "丑", "寅", "卯", "辰", "巳", "午", "未", "申", "酉",
            "戌", "亥"
        ];

        private static readonly string[] _cjkHeavenlyStemSymbols =
        [
            "甲", "乙", "丙", "丁", "戊", "己", "庚", "辛", "壬", "癸"
        ];

        /// <summary>Ethiopic numeral tens/units glyphs - CSS Counter Styles Level 3 §7.2's
        /// "ethiopic-numeric" algorithm (<see cref="ConvertToEthiopicNumber"/>). Index 0 = the "10"/"1"
        /// glyph, ... index 8 = the "90"/"9" glyph.</summary>
        private static readonly string[] _ethiopicTensGlyphs =
            ["፲", "፳", "፴", "፵", "፶", "፷", "፸", "፹", "፺"];

        private static readonly string[] _ethiopicUnitsGlyphs =
            ["፩", "፪", "፫", "፬", "፭", "፮", "፯", "፰", "፱"];

        /// <summary>
        /// Check if the given codepoint is of Asian range. Takes a <see cref="Rune"/> (a whole Unicode
        /// scalar value) rather than a <c>char</c>: the range 0x4E00-0xFA2D otherwise wrongly includes the
        /// UTF-16 surrogate range (0xD800-0xDFFF), which would classify each half of an astral character
        /// (e.g. an emoji) as its own Asian character and split the surrogate pair into two invalid words.
        /// </summary>
        /// <param name="rune">the codepoint to check</param>
        /// <returns>true - Asian char, false - otherwise</returns>
        public static bool IsAsianCharacter(Rune rune)
        {
            return rune.Value >= 0x4e00 && rune.Value <= 0xFA2D;
        }

        /// <summary>
        /// Check if the given char is a digit character (0-9) and (0-9, a-f for HEX)
        /// </summary>
        /// <param name="ch">the character to check</param>
        /// <param name="hex">optional: is hex digit check</param>
        /// <returns>true - is digit, false - not a digit</returns>
        public static bool IsDigit(char ch, bool hex = false)
        {
            return ch is >= '0' and <= '9' || (hex && ch is >= 'a' and <= 'f' or >= 'A' and <= 'F');
        }

        /// <summary>
        /// Returns true if the provided span represents an integer
        /// </summary>
        /// <param name="value">A span of characters</param>
        /// <returns>true if an integer, false otherwise</returns>
        public static bool IsInteger(ReadOnlySpan<char> value)
        {
            foreach (var charValue in value)
            {
                if (!char.IsNumber(charValue) && charValue is not '-')
                {
                    return false;
                }
            }

            return value.Length > 0;
        }

        /// <summary>
        /// Convert the given char to digit.
        /// </summary>
        /// <param name="ch">the character to check</param>
        /// <param name="hex">optional: is hex digit check</param>
        /// <returns>true - is digit, false - not a digit</returns>
        public static int ToDigit(char ch, bool hex = false)
        {
            if (ch is >= '0' and <= '9')
                return ch - '0';

            if (!hex) return 0;

            return ch switch
            {
                >= 'a' and <= 'f' => ch - 'a' + 10,
                >= 'A' and <= 'F' => ch - 'A' + 10,
                _ => 0
            };
        }

        /// <summary>
        /// Get size that is max of <paramref name="size"/> and <paramref name="other"/> for width and height separately.
        /// </summary>
        public static RSize Max(RSize size, RSize other)
        {
            return new RSize(Math.Max(size.Width, other.Width), Math.Max(size.Height, other.Height));
        }

        /// <summary>
        /// Resolves <paramref name="src"/> (a <c>src</c>/<c>href</c>/<c>url()</c> reference) to an absolute
        /// URI against the document's base URI, the single place this resolution rule lives for images and
        /// stylesheets alike. Precedence: an explicit <paramref name="overrideBaseUri"/> (e.g. the location
        /// of an importing stylesheet, against which its own relative references resolve) if given, else a
        /// <c>&lt;base href&gt;</c> element, else the adapter's base URI (which defaults to the current
        /// working directory as a <c>file:</c> URI, so relative references load from disk by default).
        /// Returns <c>null</c> when the reference cannot be resolved to any URI.
        /// </summary>
        /// <param name="htmlContainer">the container whose document base and adapter to resolve against</param>
        /// <param name="src">the (possibly relative) reference to resolve</param>
        /// <param name="overrideBaseUri">a base URI that takes precedence over the document's own base, if any</param>
        public static RUri? ResolveAgainstDocumentBase(HtmlContainerInt htmlContainer, string src, RUri? overrideBaseUri = null)
        {
            RUri? baseUri;

            if (overrideBaseUri is not null)
            {
                baseUri = overrideBaseUri;
            }
            else
            {
                var baseElement = DomUtils.GetBoxByTagName(htmlContainer.Root, "base");
                var baseHref = baseElement?.HtmlTag?.TryGetAttribute("href", "");
                baseUri = string.IsNullOrWhiteSpace(baseHref) ? htmlContainer.Adapter.BaseUri : new RUri(baseHref);
            }

            try
            {
                // Deliberately not pre-filtered with Uri.IsWellFormedUriString: it applies stricter
                // generic-syntax rules than Uri's own parser and rejects some URIs Uri/RUri parse
                // correctly (e.g. certain percent-encoded `data:` URIs). new Uri(base, src) resolves an
                // absolute src as-is, so this handles absolute and relative references uniformly.
                return baseUri is null ? new RUri(src, UriKind.RelativeOrAbsolute) : new RUri(baseUri, src);
            }
            catch (UriFormatException)
            {
                return null;
            }
        }

        /// <summary>
        /// Get the first value in the given dictionary.
        /// </summary>
        /// <typeparam name="TKey">the type of dictionary key</typeparam>
        /// <typeparam name="TValue">the type of dictionary value</typeparam>
        /// <param name="dic">the dictionary</param>
        /// <param name="defaultValue">optional: the default value to return of no elements found in dictionary </param>
        /// <returns>first element or default value</returns>
        public static TValue? GetFirstValueOrDefault<TKey, TValue>(IDictionary<TKey, TValue>? dic, TValue? defaultValue = default)
        {
            if (dic == null) return defaultValue;

            foreach (var value in dic)
                return value.Value;

            return defaultValue;
        }

        /// <summary>
        /// Get substring separated by whitespace starting from the given idex.
        /// </summary>
        /// <param name="str">the string to get substring in</param>
        /// <param name="idx">the index to start substring search from</param>
        /// <param name="length">return the length of the found string</param>
        /// <returns>the index of the substring, -1 if no valid sub-string found</returns>
        public static int GetNextSubString(string str, int idx, out int length)
        {
            while (idx < str.Length && char.IsWhiteSpace(str[idx]))
                idx++;
            if (idx < str.Length)
            {
                var endIdx = idx + 1;
                while (endIdx < str.Length && !char.IsWhiteSpace(str[endIdx]))
                    endIdx++;
                length = endIdx - idx;
                return idx;
            }
            length = 0;
            return -1;
        }

        /// <summary>
        /// Compare that the substring of <paramref name="str"/> is equal to <paramref name="str2"/>
        /// Assume given substring is not empty and all indexes are valid!<br/>
        /// </summary>
        /// <returns>true - equals, false - not equals</returns>
        public static bool SubStringEquals(string str, int idx, int length, string str2)
        {
            if (length == str2.Length && idx + length <= str.Length)
            {
                for (int i = 0; i < length; i++)
                {
                    if (char.ToLowerInvariant(str[idx + i]) != char.ToLowerInvariant(str2[i]))
                        return false;
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Style-name -> converter lookup for every "alphabetic"-family (CSS Counter Styles Level 3
        /// §6.2) and additive-family (§3.7's "additive" system - armenian/georgian/hebrew/roman) style
        /// this codebase implements via a hand-authored table/algorithm, keyed the same way
        /// <see cref="Dom.CssCounterEngine"/>'s own recognized-style check is - a single source of truth
        /// for "which styles does this method handle" instead of two independently-maintained lists.
        /// </summary>
        private static readonly FrozenDictionary<string, Func<int, string>> _alphabeticConverters =
            new Dictionary<string, Func<int, string>>(StringComparer.OrdinalIgnoreCase)
            {
                { Keywords.LowerGreek, ConvertToGreekNumber },
                { Keywords.LowerRoman, n => ConvertToRomanNumbers(n, true) },
                { Keywords.UpperRoman, n => ConvertToRomanNumbers(n, false) },
                { Keywords.Armenian, n => ConvertToSpecificNumbers(n, _armenianDigitsTable) },
                { Keywords.UpperArmenian, n => ConvertToSpecificNumbers(n, _armenianDigitsTable) },
                { Keywords.LowerArmenian, n => ConvertToSpecificNumbers(n, _lowerArmenianDigitsTable) },
                { Keywords.Georgian, n => ConvertToSpecificNumbers(n, _georgianDigitsTable) },
                { Keywords.Hebrew, ConvertToHebrewNumber },
                { Keywords.Hiragana, n => ConvertToSpecificNumbers2(n, _hiraganaDigitsTable) },
                { Keywords.HiraganaIroha, n => ConvertToSpecificNumbers2(n, _hiraganaIrohaDigitsTable) },
                { Keywords.Katakana, n => ConvertToSpecificNumbers2(n, _satakanaDigitsTable) },
                { Keywords.KatakanaIroha, n => ConvertToSpecificNumbers2(n, _katakanaIrohaDigitsTable) },
                { Keywords.LowerAlpha, n => ConvertToEnglishNumber(n, true) },
                { Keywords.LowerLatin, n => ConvertToEnglishNumber(n, true) },
                { Keywords.UpperAlpha, n => ConvertToEnglishNumber(n, false) },
                { Keywords.UpperLatin, n => ConvertToEnglishNumber(n, false) }
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Whether <paramref name="style"/> is one of the alphabetic/additive counter styles
        /// <see cref="ConvertToAlphaNumber"/> genuinely handles - the single source of truth
        /// <see cref="Dom.CssCounterEngine.FormatCounterValue"/> checks before calling it, so a known
        /// style is never wrongly demoted to decimal and an unknown one never wrongly promoted to alpha.
        /// </summary>
        public static bool IsAlphabeticCounterStyle(string style) => _alphabeticConverters.ContainsKey(style);

        /// <summary>
        /// Convert number to alphanumeric system by the requested style (UpperAlpha, LowerRoman, Hebrew, etc.).
        /// </summary>
        /// <param name="number">the number to convert</param>
        /// <param name="style">the css style to convert by</param>
        /// <returns>converted string</returns>
        public static string ConvertToAlphaNumber(int number, string style = Keywords.UpperAlpha)
        {
            if (number == 0)
                return string.Empty;

            return _alphabeticConverters.TryGetValue(style, out var convert)
                ? convert(number)
                : ConvertToEnglishNumber(number, false);
        }

        /// <summary>
        /// Hebrew's additive algorithm (<see cref="ConvertToSpecificNumbers"/> over
        /// <see cref="_hebrewDigitsTable"/>) with the CSS Counter Styles Level 3 §6.1 override for a
        /// tens-and-units value of 15 or 16: the naive per-digit-group combination would produce
        /// יה/יו, which closely resembles the Tetragrammaton, so the spec mandates טו/טז instead. 17-19
        /// don't need an override - their naive combination (יז/יח/יט) already matches the spec's table.
        /// </summary>
        private static string ConvertToHebrewNumber(int number)
        {
            if (number <= 0) return string.Empty;

            var tensAndUnits = number % 100;
            if (tensAndUnits is 15 or 16)
            {
                var hundredsAndUp = ConvertToSpecificNumbers(number - tensAndUnits, _hebrewDigitsTable);
                return hundredsAndUp + (tensAndUnits == 15 ? "טו" : "טז");
            }

            return ConvertToSpecificNumbers(number, _hebrewDigitsTable);
        }

        /// <summary>
        /// Converts a number into a per-script positional (place-value) numeral by substituting each
        /// base-10 digit for the script's own glyph, per CSS Counter Styles Level 3 §6.1's "numeric"
        /// system (arabic-indic, devanagari, thai, cjk-decimal, ...) - the same algorithm as western
        /// decimal, just with different digit glyphs. Returns null when <paramref name="style"/> isn't
        /// one of the known "numeric"-system styles, so the caller can fall back to plain decimal.
        /// </summary>
        public static string? ConvertToPositionalNumber(int number, string style)
        {
            return _positionalDigitTables.TryGetValue(style, out var digits)
                ? ConvertToPositionalDigits(number, digits)
                : null;
        }

        private static string ConvertToPositionalDigits(int number, string[] digits)
        {
            if (number == 0) return digits[0];

            var negative = number < 0;
            var magnitude = negative ? -(long)number : number;
            var sb = string.Empty;

            while (magnitude > 0)
            {
                sb = digits[(int)(magnitude % 10)] + sb;
                magnitude /= 10;
            }

            return negative ? "-" + sb : sb;
        }

        /// <summary>
        /// Looks up a fixed (non-repeating) counter-style symbol for CSS Counter Styles Level 3 §6.4's
        /// "cjk-earthly-branch"/"cjk-heavenly-stem". Returns null when <paramref name="style"/> isn't
        /// one of those two names, or when <paramref name="number"/> falls outside the style's finite
        /// 1..N range, so the caller can fall back to decimal either way - this codebase doesn't model
        /// a per-style <c>fallback</c> descriptor, and decimal is the spec's own default for one.
        /// </summary>
        public static string? ConvertToFixedCjkSymbol(int number, string style)
        {
            string[] symbols;
            if (style.Equals(Keywords.CjkEarthlyBranch, StringComparison.OrdinalIgnoreCase))
            {
                symbols = _cjkEarthlyBranchSymbols;
            }
            else if (style.Equals(Keywords.CjkHeavenlyStem, StringComparison.OrdinalIgnoreCase))
            {
                symbols = _cjkHeavenlyStemSymbols;
            }
            else
            {
                return null;
            }

            return number is >= 1 && number <= symbols.Length ? symbols[number - 1] : null;
        }

        /// <summary>
        /// Converts a positive integer into ethiopic numerals per CSS Counter Styles Level 3 §7.2's
        /// documented algorithm: split into 2-digit groups starting from the least significant, then
        /// for each group (processed most-significant-first): omit its digits (not its separator) when
        /// the group is zero, is the most-significant group and equals 1, or has an odd index and
        /// equals 1; substitute the remaining tens/units glyphs; then append a hundred separator (፻) to
        /// every odd-indexed group that wasn't originally zero, and a ten-thousand separator (፼) to
        /// every even-indexed group except group 0. Verified against the spec's own worked examples for
        /// values an <see langword="int"/> can hold (100 -> ፻; 78010092 -> ፸፰፻፩፼፺፪, the spec's
        /// smaller worked example) - see <c>CommonUtilsTests.ConvertToEthiopicNumber_MatchesSpecAlgorithm</c>.
        /// The spec's other worked example, 780100000092, exceeds <see cref="int.MaxValue"/> and so isn't
        /// reachable through this method's <see langword="int"/> parameter.
        /// </summary>
        public static string ConvertToEthiopicNumber(int number)
        {
            if (number == 1) return _ethiopicUnitsGlyphs[0];
            if (number <= 0) return string.Empty;

            var groupValues = new List<int>();
            var remaining = number;
            while (remaining > 0)
            {
                groupValues.Add(remaining % 100);
                remaining /= 100;
            }

            var mostSignificantIndex = groupValues.Count - 1;
            var sb = new StringBuilder();

            for (var i = mostSignificantIndex; i >= 0; i--)
            {
                var value = groupValues[i];

                var omitDigits = value == 0
                    || (i == mostSignificantIndex && value == 1)
                    || (i % 2 == 1 && value == 1);

                if (!omitDigits)
                {
                    var tens = value / 10;
                    var units = value % 10;
                    if (tens > 0) sb.Append(_ethiopicTensGlyphs[tens - 1]);
                    if (units > 0) sb.Append(_ethiopicUnitsGlyphs[units - 1]);
                }

                if (i % 2 == 1 && value != 0)
                {
                    sb.Append('፻'); // ፻ hundred
                }
                else if (i % 2 == 0 && i != 0)
                {
                    sb.Append('፼'); // ፼ ten-thousand
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Convert the given integer into alphabetic numeric format (D, AU, etc.)
        /// </summary>
        /// <param name="number">the number to convert</param>
        /// <param name="lowercase">is to use lowercase</param>
        /// <returns>the roman number string</returns>
        private static string ConvertToEnglishNumber(int number, bool lowercase)
        {
            var sb = string.Empty;
            int alphStart = lowercase ? 97 : 65;
            while (number > 0)
            {
                var n = number % 26 - 1;
                if (n >= 0)
                {
                    sb = (char)(alphStart + n) + sb;
                    number /= 26;
                }
                else
                {
                    sb = (char)(alphStart + 25) + sb;
                    number = (number - 1) / 26;
                }
            }

            return sb;
        }

        /// <summary>
        /// Convert the given integer into alphabetic numeric format (alpha, AU, etc.)
        /// </summary>
        /// <param name="number">the number to convert</param>
        /// <returns>the roman number string</returns>
        private static string ConvertToGreekNumber(int number)
        {
            var sb = string.Empty;
            while (number > 0)
            {
                var n = number % 24 - 1;
                if (n > 16)
                    n++;
                if (n >= 0)
                {
                    sb = (char)(945 + n) + sb;
                    number /= 24;
                }
                else
                {
                    sb = (char)(945 + 24) + sb;
                    number = (number - 1) / 25;
                }
            }

            return sb;
        }

        /// <summary>
        /// Convert the given integer into roman numeric format (II, VI, IX, etc.)
        /// </summary>
        /// <param name="number">the number to convert</param>
        /// <param name="lowercase">if to use lowercase letters for roman digits</param>
        /// <returns>the roman number string</returns>
        private static string ConvertToRomanNumbers(int number, bool lowercase)
        {
            var sb = string.Empty;
            for (int i = 1000, j = 3; i > 0; i /= 10, j--)
            {
                int digit = number / i;
                sb += string.Format(_romanDigitsTable[j, digit]);
                number -= digit * i;
            }
            return lowercase ? sb.ToLower() : sb;
        }

        /// <summary>
        /// Convert the given integer into given alphabet numeric system.
        /// </summary>
        /// <param name="number">the number to convert</param>
        /// <param name="alphabet">the alphabet system to use</param>
        /// <returns>the number string</returns>
        private static string ConvertToSpecificNumbers(int number, string[,] alphabet)
        {
            int level = 0;
            var sb = string.Empty;
            while (number > 0 && level < alphabet.GetLength(0))
            {
                var n = number % 10;
                if (n > 0)
                    sb = alphabet[level, number % 10 - 1].ToString(CultureInfo.InvariantCulture) + sb;
                number /= 10;
                level++;
            }
            return sb;
        }

        /// <summary>
        /// Convert the given integer into given alphabet numeric system. Driven by
        /// <paramref name="alphabet"/>'s own length (a bijective base-(length+1) numbering) rather than
        /// a literal 48/49, so this also serves alphabets of a different length correctly - e.g. the 47
        /// character hiragana-iroha/katakana-iroha orderings alongside the pre-existing 48-character
        /// dictionary-order hiragana/katakana tables. A hard-coded 49 here previously threw
        /// IndexOutOfRangeException on any counter value >= 48 for a 47-length alphabet.
        /// </summary>
        /// <param name="number">the number to convert</param>
        /// <param name="alphabet">the alphabet system to use</param>
        /// <returns>the number string</returns>
        private static string ConvertToSpecificNumbers2(int number, string[] alphabet)
        {
            var @base = alphabet.Length + 1;

            for (int i = 20; i > 0; i--)
            {
                if (number > alphabet.Length * i + 1)
                    number++;
            }

            var sb = string.Empty;
            while (number > 0)
            {
                sb = alphabet[Math.Max(0, number % @base - 1)].ToString(CultureInfo.InvariantCulture) + sb;
                number /= @base;
            }
            return sb;
        }
    }
}