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

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text;

namespace PeachBrackets.Html.Utils
{
    internal static class HtmlUtils
    {
        #region Fields and Consts

        /// <summary>
        /// List of html tags that don't have content
        /// </summary>
        private static readonly FrozenSet<string> _list = new HashSet<string>
        {
            "area", "base", "basefont", "br", "col", "embed",
            "frame", "hr", "img", "input", "isindex",
            "link", "meta", "param", "source", "track", "wbr"
        }.ToFrozenSet();

        /// <summary>
        /// the html encode\decode pairs
        /// </summary>
        private static readonly KeyValuePair<string, string>[] _encodeDecode =
        [
            new("&lt;", "<"),
            new("&gt;", ">"),
            new("&quot;", "\""),
            new("&amp;", "&")
        ];

        /// <summary>
        /// the html decode only pairs
        /// </summary>
        private static readonly FrozenDictionary<string, char> _decodeOnly;

        #endregion


        /// <summary>
        /// Init.
        /// </summary>
        static HtmlUtils()
        {
            var decodeOnlyBuilder = new Dictionary<string, char>(StringComparer.OrdinalIgnoreCase)
            {
                // "nbsp" below decodes to the real U+00A0 non-breaking space, not a plain ASCII space -
                // callers that need to tell it apart from ordinary collapsible whitespace (line-wrapping,
                // whitespace collapsing) must use IsCollapsibleWhitespace/IsNullOrCollapsibleWhitespace
                // below rather than char.IsWhiteSpace, which is also true for U+00A0.

                ["nbsp"] = ' ',
                ["rdquo"] = '"',
                ["lsquo"] = '\'',
                ["apos"] = '\'',

                // ISO 8859-1 Symbols
                ["iexcl"] = Convert.ToChar(161),
                ["cent"] = Convert.ToChar(162),
                ["pound"] = Convert.ToChar(163),
                ["curren"] = Convert.ToChar(164),
                ["yen"] = Convert.ToChar(165),
                ["brvbar"] = Convert.ToChar(166),
                ["sect"] = Convert.ToChar(167),
                ["uml"] = Convert.ToChar(168),
                ["copy"] = Convert.ToChar(169),
                ["ordf"] = Convert.ToChar(170),
                ["laquo"] = Convert.ToChar(171),
                ["not"] = Convert.ToChar(172),
                ["shy"] = Convert.ToChar(173),
                ["reg"] = Convert.ToChar(174),
                ["macr"] = Convert.ToChar(175),
                ["deg"] = Convert.ToChar(176),
                ["plusmn"] = Convert.ToChar(177),
                ["sup2"] = Convert.ToChar(178),
                ["sup3"] = Convert.ToChar(179),
                ["acute"] = Convert.ToChar(180),
                ["micro"] = Convert.ToChar(181),
                ["para"] = Convert.ToChar(182),
                ["middot"] = Convert.ToChar(183),
                ["cedil"] = Convert.ToChar(184),
                ["sup1"] = Convert.ToChar(185),
                ["ordm"] = Convert.ToChar(186),
                ["raquo"] = Convert.ToChar(187),
                ["frac14"] = Convert.ToChar(188),
                ["frac12"] = Convert.ToChar(189),
                ["frac34"] = Convert.ToChar(190),
                ["iquest"] = Convert.ToChar(191),
                ["times"] = Convert.ToChar(215),
                ["divide"] = Convert.ToChar(247),

                // ISO 8859-1 Characters
                ["Agrave"] = Convert.ToChar(192),
                ["Aacute"] = Convert.ToChar(193),
                ["Acirc"] = Convert.ToChar(194),
                ["Atilde"] = Convert.ToChar(195),
                ["Auml"] = Convert.ToChar(196),
                ["Aring"] = Convert.ToChar(197),
                ["AElig"] = Convert.ToChar(198),
                ["Ccedil"] = Convert.ToChar(199),
                ["Egrave"] = Convert.ToChar(200),
                ["Eacute"] = Convert.ToChar(201),
                ["Ecirc"] = Convert.ToChar(202),
                ["Euml"] = Convert.ToChar(203),
                ["Igrave"] = Convert.ToChar(204),
                ["Iacute"] = Convert.ToChar(205),
                ["Icirc"] = Convert.ToChar(206),
                ["Iuml"] = Convert.ToChar(207),
                ["ETH"] = Convert.ToChar(208),
                ["Ntilde"] = Convert.ToChar(209),
                ["Ograve"] = Convert.ToChar(210),
                ["Oacute"] = Convert.ToChar(211),
                ["Ocirc"] = Convert.ToChar(212),
                ["Otilde"] = Convert.ToChar(213),
                ["Ouml"] = Convert.ToChar(214),
                ["Oslash"] = Convert.ToChar(216),
                ["Ugrave"] = Convert.ToChar(217),
                ["Uacute"] = Convert.ToChar(218),
                ["Ucirc"] = Convert.ToChar(219),
                ["Uuml"] = Convert.ToChar(220),
                ["Yacute"] = Convert.ToChar(221),
                ["THORN"] = Convert.ToChar(222),
                ["szlig"] = Convert.ToChar(223),
                ["agrave"] = Convert.ToChar(224),
                ["aacute"] = Convert.ToChar(225),
                ["acirc"] = Convert.ToChar(226),
                ["atilde"] = Convert.ToChar(227),
                ["auml"] = Convert.ToChar(228),
                ["aring"] = Convert.ToChar(229),
                ["aelig"] = Convert.ToChar(230),
                ["ccedil"] = Convert.ToChar(231),
                ["egrave"] = Convert.ToChar(232),
                ["eacute"] = Convert.ToChar(233),
                ["ecirc"] = Convert.ToChar(234),
                ["euml"] = Convert.ToChar(235),
                ["igrave"] = Convert.ToChar(236),
                ["iacute"] = Convert.ToChar(237),
                ["icirc"] = Convert.ToChar(238),
                ["iuml"] = Convert.ToChar(239),
                ["eth"] = Convert.ToChar(240),
                ["ntilde"] = Convert.ToChar(241),
                ["ograve"] = Convert.ToChar(242),
                ["oacute"] = Convert.ToChar(243),
                ["ocirc"] = Convert.ToChar(244),
                ["otilde"] = Convert.ToChar(245),
                ["ouml"] = Convert.ToChar(246),
                ["oslash"] = Convert.ToChar(248),
                ["ugrave"] = Convert.ToChar(249),
                ["uacute"] = Convert.ToChar(250),
                ["ucirc"] = Convert.ToChar(251),
                ["uuml"] = Convert.ToChar(252),
                ["yacute"] = Convert.ToChar(253),
                ["thorn"] = Convert.ToChar(254),
                ["yuml"] = Convert.ToChar(255),

                // Math Symbols Supported by HTML
                ["forall"] = Convert.ToChar(8704),
                ["part"] = Convert.ToChar(8706),
                ["exist"] = Convert.ToChar(8707),
                ["empty"] = Convert.ToChar(8709),
                ["nabla"] = Convert.ToChar(8711),
                ["isin"] = Convert.ToChar(8712),
                ["notin"] = Convert.ToChar(8713),
                ["ni"] = Convert.ToChar(8715),
                ["prod"] = Convert.ToChar(8719),
                ["sum"] = Convert.ToChar(8721),
                ["minus"] = Convert.ToChar(8722),
                ["lowast"] = Convert.ToChar(8727),
                ["radic"] = Convert.ToChar(8730),
                ["prop"] = Convert.ToChar(8733),
                ["infin"] = Convert.ToChar(8734),
                ["ang"] = Convert.ToChar(8736),
                ["and"] = Convert.ToChar(8743),
                ["or"] = Convert.ToChar(8744),
                ["cap"] = Convert.ToChar(8745),
                ["cup"] = Convert.ToChar(8746),
                ["int"] = Convert.ToChar(8747),
                ["there4"] = Convert.ToChar(8756),
                ["sim"] = Convert.ToChar(8764),
                ["cong"] = Convert.ToChar(8773),
                ["asymp"] = Convert.ToChar(8776),
                ["ne"] = Convert.ToChar(8800),
                ["equiv"] = Convert.ToChar(8801),
                ["le"] = Convert.ToChar(8804),
                ["ge"] = Convert.ToChar(8805),
                ["sub"] = Convert.ToChar(8834),
                ["sup"] = Convert.ToChar(8835),
                ["nsub"] = Convert.ToChar(8836),
                ["sube"] = Convert.ToChar(8838),
                ["supe"] = Convert.ToChar(8839),
                ["oplus"] = Convert.ToChar(8853),
                ["otimes"] = Convert.ToChar(8855),
                ["perp"] = Convert.ToChar(8869),
                ["sdot"] = Convert.ToChar(8901),

                // Greek Letters Supported by HTML
                ["Alpha"] = Convert.ToChar(913),
                ["Beta"] = Convert.ToChar(914),
                ["Gamma"] = Convert.ToChar(915),
                ["Delta"] = Convert.ToChar(916),
                ["Epsilon"] = Convert.ToChar(917),
                ["Zeta"] = Convert.ToChar(918),
                ["Eta"] = Convert.ToChar(919),
                ["Theta"] = Convert.ToChar(920),
                ["Iota"] = Convert.ToChar(921),
                ["Kappa"] = Convert.ToChar(922),
                ["Lambda"] = Convert.ToChar(923),
                ["Mu"] = Convert.ToChar(924),
                ["Nu"] = Convert.ToChar(925),
                ["Xi"] = Convert.ToChar(926),
                ["Omicron"] = Convert.ToChar(927),
                ["Pi"] = Convert.ToChar(928),
                ["Rho"] = Convert.ToChar(929),
                ["Sigma"] = Convert.ToChar(931),
                ["Tau"] = Convert.ToChar(932),
                ["Upsilon"] = Convert.ToChar(933),
                ["Phi"] = Convert.ToChar(934),
                ["Chi"] = Convert.ToChar(935),
                ["Psi"] = Convert.ToChar(936),
                ["Omega"] = Convert.ToChar(937),
                ["alpha"] = Convert.ToChar(945),
                ["beta"] = Convert.ToChar(946),
                ["gamma"] = Convert.ToChar(947),
                ["delta"] = Convert.ToChar(948),
                ["epsilon"] = Convert.ToChar(949),
                ["zeta"] = Convert.ToChar(950),
                ["eta"] = Convert.ToChar(951),
                ["theta"] = Convert.ToChar(952),
                ["iota"] = Convert.ToChar(953),
                ["kappa"] = Convert.ToChar(954),
                ["lambda"] = Convert.ToChar(955),
                ["mu"] = Convert.ToChar(956),
                ["nu"] = Convert.ToChar(957),
                ["xi"] = Convert.ToChar(958),
                ["omicron"] = Convert.ToChar(959),
                ["pi"] = Convert.ToChar(960),
                ["rho"] = Convert.ToChar(961),
                ["sigmaf"] = Convert.ToChar(962),
                ["sigma"] = Convert.ToChar(963),
                ["tau"] = Convert.ToChar(964),
                ["upsilon"] = Convert.ToChar(965),
                ["phi"] = Convert.ToChar(966),
                ["chi"] = Convert.ToChar(967),
                ["psi"] = Convert.ToChar(968),
                ["omega"] = Convert.ToChar(969),
                ["thetasym"] = Convert.ToChar(977),
                ["upsih"] = Convert.ToChar(978),
                ["piv"] = Convert.ToChar(982),

                // Other Entities Supported by HTML
                ["OElig"] = Convert.ToChar(338),
                ["oelig"] = Convert.ToChar(339),
                ["Scaron"] = Convert.ToChar(352),
                ["scaron"] = Convert.ToChar(353),
                ["Yuml"] = Convert.ToChar(376),
                ["fnof"] = Convert.ToChar(402),
                ["circ"] = Convert.ToChar(710),
                ["tilde"] = Convert.ToChar(732),
                ["ndash"] = Convert.ToChar(8211),
                ["mdash"] = Convert.ToChar(8212),
                ["lsquo"] = Convert.ToChar(8216),
                ["rsquo"] = Convert.ToChar(8217),
                ["sbquo"] = Convert.ToChar(8218),
                ["ldquo"] = Convert.ToChar(8220),
                ["rdquo"] = Convert.ToChar(8221),
                ["bdquo"] = Convert.ToChar(8222),
                ["dagger"] = Convert.ToChar(8224),
                ["Dagger"] = Convert.ToChar(8225),
                ["bull"] = Convert.ToChar(8226),
                ["hellip"] = Convert.ToChar(8230),
                ["permil"] = Convert.ToChar(8240),
                ["prime"] = Convert.ToChar(8242),
                ["Prime"] = Convert.ToChar(8243),
                ["lsaquo"] = Convert.ToChar(8249),
                ["rsaquo"] = Convert.ToChar(8250),
                ["oline"] = Convert.ToChar(8254),
                ["euro"] = Convert.ToChar(8364),
                ["trade"] = Convert.ToChar(153),
                ["larr"] = Convert.ToChar(8592),
                ["uarr"] = Convert.ToChar(8593),
                ["rarr"] = Convert.ToChar(8594),
                ["darr"] = Convert.ToChar(8595),
                ["harr"] = Convert.ToChar(8596),
                ["crarr"] = Convert.ToChar(8629),
                ["lceil"] = Convert.ToChar(8968),
                ["rceil"] = Convert.ToChar(8969),
                ["lfloor"] = Convert.ToChar(8970),
                ["rfloor"] = Convert.ToChar(8971),
                ["loz"] = Convert.ToChar(9674),
                ["spades"] = Convert.ToChar(9824),
                ["clubs"] = Convert.ToChar(9827),
                ["hearts"] = Convert.ToChar(9829),
                ["diams"] = Convert.ToChar(9830)
            };

            _decodeOnly = decodeOnlyBuilder.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }

        public static string FixNewLines(string text)
        {
            text = text.Replace("\r\n", "\n");
            text = text.Replace("\r", "\n");
            return text;
        }

        /// <summary>
        /// Is the given html tag is single tag or can have content.
        /// </summary>
        /// <param name="tagName">the tag to check (must be lower case)</param>
        /// <returns>true - is single tag, false - otherwise</returns>
        public static bool IsSingleTag(string tagName)
        {
            return _list.Contains(tagName);
        }

        /// <summary>
        /// Is the given character CSS-collapsible/breakable whitespace. Unlike <see cref="char.IsWhiteSpace(char)"/>,
        /// this excludes U+00A0 (non-breaking space) - per CSS, nbsp is significant, non-collapsible content and
        /// never a line-break opportunity, even though Unicode classifies it as whitespace for general text
        /// processing purposes.
        /// </summary>
        public static bool IsCollapsibleWhitespace(char c)
        {
            return char.IsWhiteSpace(c) && c != ' ';
        }

        /// <summary>
        /// Is the given string null, empty, or made up entirely of CSS-collapsible whitespace (see
        /// <see cref="IsCollapsibleWhitespace"/>). A string containing only non-breaking spaces returns false.
        /// </summary>
        public static bool IsNullOrCollapsibleWhitespace(string? str)
        {
            if (string.IsNullOrEmpty(str)) return true;

            foreach (var c in str)
            {
                if (!IsCollapsibleWhitespace(c)) return false;
            }

            return true;
        }

        /// <summary>
        /// Decode html encoded string to regular string.<br/>
        /// Handles &lt;, &gt;, &amp;, and numeric character references.<br/>
        /// <br/>
        /// <b>Note:</b> This method is NOT used during normal HTML rendering.
        /// MimeKit's HtmlTokenizer decodes entities once at parse time, so text in
        /// CssBox.Text is already decoded. This utility is retained for test scenarios
        /// and any edge cases that need manual entity decoding.
        /// </summary>
        /// <param name="str">the string to decode</param>
        /// <returns>decoded string</returns>
        public static string DecodeHtml(string str)
        {
            if (!string.IsNullOrEmpty(str))
            {
                var strCopy = new StringBuilder(str);
                strCopy = DecodeHtmlCharByCode(strCopy);
                strCopy = DecodeHtmlCharByName(strCopy);

                foreach (var encPair in _encodeDecode)
                {
                    strCopy = strCopy.Replace(encPair.Key, encPair.Value);
                }

                return strCopy.ToString();
            }
            return str;
        }

        #region Private methods
        private static StringBuilder DecodeHtmlCharByCode(StringBuilder str)
        {
            var idx = FindIndexOf(str, "&#", 0, true);
            while (idx > -1)
            {
                bool hex = str.Length > idx + 3 && char.ToLower(str[idx + 2]) == 'x';
                var endIdx = idx + 2 + (hex ? 1 : 0);

                long num = 0;
                while (endIdx < str.Length && CommonUtils.IsDigit(str[endIdx], hex))
                    num = num * (hex ? 16 : 10) + CommonUtils.ToDigit(str[endIdx++], hex);
                endIdx += (endIdx < str.Length && str[endIdx] == ';') ? 1 : 0;

                // https://html.spec.whatwg.org/dev/parsing.html#numeric-character-reference-end-state:
                // a null, out-of-range, or surrogate code point is a parse error that resolves to
                // U+FFFD REPLACEMENT CHARACTER, not to nothing.
                string repl = num == 0 || num > 0x10ffff || (num >= 0xd800 && num <= 0xdfff)
                    ? "�"
                    : char.ConvertFromUtf32((int)num);

                str = str.Remove(idx, endIdx - idx);
                str = str.Insert(idx, repl);

                idx = FindIndexOf(str, "&#", idx + 1);
            }

            return str;
        }

        private static int FindIndexOf(StringBuilder reference, string value, int startIndex, bool ignoreCase = false)
        {
            int len = value.Length;
            int max = (reference.Length - len) + 1;
            var comparableValue = ignoreCase ? value.ToLower() : value;
            var isEqual =
                ignoreCase
                ? (x, y) => char.ToLower(x) == y
                : new Func<char, char, bool>((x, y) => x == y);
            for (int i = startIndex; i < max; ++i)
                if (isEqual(reference[i], comparableValue[0]))
                {
                    int j = 1;
                    while ((j < len) && isEqual(reference[i + j], comparableValue[j]))
                        ++j;
                    if (j == len)
                        return i;
                }
            return -1;
        }

        private static StringBuilder DecodeHtmlCharByName(StringBuilder str)
        {
            var idx = FindIndexOf(str, "&", 0);
            while (idx > -1)
            {
                var endIdx = FindIndexOf(str, ";", idx);
                if (endIdx > -1 && endIdx - idx < 8)
                {
                    var key = str.ToString(idx + 1, endIdx - idx - 1);
                    if (_decodeOnly.TryGetValue(key, out var c))
                    {
                        str = str.Remove(idx, endIdx - idx + 1);
                        str = str.Insert(idx, c);
                    }
                }

                idx = FindIndexOf(str, "&", idx + 1);
            }
            return str;
        }

        #endregion
    }
}