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

namespace PeachPDF.Html.Core.Utils
{
    internal static class HtmlUtils
    {
        #region Fields and Consts

        /// <summary>
        /// List of html tags that don't have content
        /// </summary>
        private static readonly FrozenSet<string> _list = new HashSet<string>
        {
            "area", "base", "basefont", "br", "col",
            "frame", "hr", "img", "input", "isindex",
            "link", "meta", "param"
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
            var decodeOnlyBuilder = new Dictionary<string, char>(StringComparer.InvariantCultureIgnoreCase);

            // "nbsp" below decodes to the real U+00A0 non-breaking space, not a plain ASCII space -
            // callers that need to tell it apart from ordinary collapsible whitespace (line-wrapping,
            // whitespace collapsing) must use IsCollapsibleWhitespace/IsNullOrCollapsibleWhitespace
            // below rather than char.IsWhiteSpace, which is also true for U+00A0.

            decodeOnlyBuilder["nbsp"] = ' ';
            decodeOnlyBuilder["rdquo"] = '"';
            decodeOnlyBuilder["lsquo"] = '\'';
            decodeOnlyBuilder["apos"] = '\'';

            // ISO 8859-1 Symbols
            decodeOnlyBuilder["iexcl"] = Convert.ToChar(161);
            decodeOnlyBuilder["cent"] = Convert.ToChar(162);
            decodeOnlyBuilder["pound"] = Convert.ToChar(163);
            decodeOnlyBuilder["curren"] = Convert.ToChar(164);
            decodeOnlyBuilder["yen"] = Convert.ToChar(165);
            decodeOnlyBuilder["brvbar"] = Convert.ToChar(166);
            decodeOnlyBuilder["sect"] = Convert.ToChar(167);
            decodeOnlyBuilder["uml"] = Convert.ToChar(168);
            decodeOnlyBuilder["copy"] = Convert.ToChar(169);
            decodeOnlyBuilder["ordf"] = Convert.ToChar(170);
            decodeOnlyBuilder["laquo"] = Convert.ToChar(171);
            decodeOnlyBuilder["not"] = Convert.ToChar(172);
            decodeOnlyBuilder["shy"] = Convert.ToChar(173);
            decodeOnlyBuilder["reg"] = Convert.ToChar(174);
            decodeOnlyBuilder["macr"] = Convert.ToChar(175);
            decodeOnlyBuilder["deg"] = Convert.ToChar(176);
            decodeOnlyBuilder["plusmn"] = Convert.ToChar(177);
            decodeOnlyBuilder["sup2"] = Convert.ToChar(178);
            decodeOnlyBuilder["sup3"] = Convert.ToChar(179);
            decodeOnlyBuilder["acute"] = Convert.ToChar(180);
            decodeOnlyBuilder["micro"] = Convert.ToChar(181);
            decodeOnlyBuilder["para"] = Convert.ToChar(182);
            decodeOnlyBuilder["middot"] = Convert.ToChar(183);
            decodeOnlyBuilder["cedil"] = Convert.ToChar(184);
            decodeOnlyBuilder["sup1"] = Convert.ToChar(185);
            decodeOnlyBuilder["ordm"] = Convert.ToChar(186);
            decodeOnlyBuilder["raquo"] = Convert.ToChar(187);
            decodeOnlyBuilder["frac14"] = Convert.ToChar(188);
            decodeOnlyBuilder["frac12"] = Convert.ToChar(189);
            decodeOnlyBuilder["frac34"] = Convert.ToChar(190);
            decodeOnlyBuilder["iquest"] = Convert.ToChar(191);
            decodeOnlyBuilder["times"] = Convert.ToChar(215);
            decodeOnlyBuilder["divide"] = Convert.ToChar(247);

            // ISO 8859-1 Characters
            decodeOnlyBuilder["Agrave"] = Convert.ToChar(192);
            decodeOnlyBuilder["Aacute"] = Convert.ToChar(193);
            decodeOnlyBuilder["Acirc"] = Convert.ToChar(194);
            decodeOnlyBuilder["Atilde"] = Convert.ToChar(195);
            decodeOnlyBuilder["Auml"] = Convert.ToChar(196);
            decodeOnlyBuilder["Aring"] = Convert.ToChar(197);
            decodeOnlyBuilder["AElig"] = Convert.ToChar(198);
            decodeOnlyBuilder["Ccedil"] = Convert.ToChar(199);
            decodeOnlyBuilder["Egrave"] = Convert.ToChar(200);
            decodeOnlyBuilder["Eacute"] = Convert.ToChar(201);
            decodeOnlyBuilder["Ecirc"] = Convert.ToChar(202);
            decodeOnlyBuilder["Euml"] = Convert.ToChar(203);
            decodeOnlyBuilder["Igrave"] = Convert.ToChar(204);
            decodeOnlyBuilder["Iacute"] = Convert.ToChar(205);
            decodeOnlyBuilder["Icirc"] = Convert.ToChar(206);
            decodeOnlyBuilder["Iuml"] = Convert.ToChar(207);
            decodeOnlyBuilder["ETH"] = Convert.ToChar(208);
            decodeOnlyBuilder["Ntilde"] = Convert.ToChar(209);
            decodeOnlyBuilder["Ograve"] = Convert.ToChar(210);
            decodeOnlyBuilder["Oacute"] = Convert.ToChar(211);
            decodeOnlyBuilder["Ocirc"] = Convert.ToChar(212);
            decodeOnlyBuilder["Otilde"] = Convert.ToChar(213);
            decodeOnlyBuilder["Ouml"] = Convert.ToChar(214);
            decodeOnlyBuilder["Oslash"] = Convert.ToChar(216);
            decodeOnlyBuilder["Ugrave"] = Convert.ToChar(217);
            decodeOnlyBuilder["Uacute"] = Convert.ToChar(218);
            decodeOnlyBuilder["Ucirc"] = Convert.ToChar(219);
            decodeOnlyBuilder["Uuml"] = Convert.ToChar(220);
            decodeOnlyBuilder["Yacute"] = Convert.ToChar(221);
            decodeOnlyBuilder["THORN"] = Convert.ToChar(222);
            decodeOnlyBuilder["szlig"] = Convert.ToChar(223);
            decodeOnlyBuilder["agrave"] = Convert.ToChar(224);
            decodeOnlyBuilder["aacute"] = Convert.ToChar(225);
            decodeOnlyBuilder["acirc"] = Convert.ToChar(226);
            decodeOnlyBuilder["atilde"] = Convert.ToChar(227);
            decodeOnlyBuilder["auml"] = Convert.ToChar(228);
            decodeOnlyBuilder["aring"] = Convert.ToChar(229);
            decodeOnlyBuilder["aelig"] = Convert.ToChar(230);
            decodeOnlyBuilder["ccedil"] = Convert.ToChar(231);
            decodeOnlyBuilder["egrave"] = Convert.ToChar(232);
            decodeOnlyBuilder["eacute"] = Convert.ToChar(233);
            decodeOnlyBuilder["ecirc"] = Convert.ToChar(234);
            decodeOnlyBuilder["euml"] = Convert.ToChar(235);
            decodeOnlyBuilder["igrave"] = Convert.ToChar(236);
            decodeOnlyBuilder["iacute"] = Convert.ToChar(237);
            decodeOnlyBuilder["icirc"] = Convert.ToChar(238);
            decodeOnlyBuilder["iuml"] = Convert.ToChar(239);
            decodeOnlyBuilder["eth"] = Convert.ToChar(240);
            decodeOnlyBuilder["ntilde"] = Convert.ToChar(241);
            decodeOnlyBuilder["ograve"] = Convert.ToChar(242);
            decodeOnlyBuilder["oacute"] = Convert.ToChar(243);
            decodeOnlyBuilder["ocirc"] = Convert.ToChar(244);
            decodeOnlyBuilder["otilde"] = Convert.ToChar(245);
            decodeOnlyBuilder["ouml"] = Convert.ToChar(246);
            decodeOnlyBuilder["oslash"] = Convert.ToChar(248);
            decodeOnlyBuilder["ugrave"] = Convert.ToChar(249);
            decodeOnlyBuilder["uacute"] = Convert.ToChar(250);
            decodeOnlyBuilder["ucirc"] = Convert.ToChar(251);
            decodeOnlyBuilder["uuml"] = Convert.ToChar(252);
            decodeOnlyBuilder["yacute"] = Convert.ToChar(253);
            decodeOnlyBuilder["thorn"] = Convert.ToChar(254);
            decodeOnlyBuilder["yuml"] = Convert.ToChar(255);

            // Math Symbols Supported by HTML
            decodeOnlyBuilder["forall"] = Convert.ToChar(8704);
            decodeOnlyBuilder["part"] = Convert.ToChar(8706);
            decodeOnlyBuilder["exist"] = Convert.ToChar(8707);
            decodeOnlyBuilder["empty"] = Convert.ToChar(8709);
            decodeOnlyBuilder["nabla"] = Convert.ToChar(8711);
            decodeOnlyBuilder["isin"] = Convert.ToChar(8712);
            decodeOnlyBuilder["notin"] = Convert.ToChar(8713);
            decodeOnlyBuilder["ni"] = Convert.ToChar(8715);
            decodeOnlyBuilder["prod"] = Convert.ToChar(8719);
            decodeOnlyBuilder["sum"] = Convert.ToChar(8721);
            decodeOnlyBuilder["minus"] = Convert.ToChar(8722);
            decodeOnlyBuilder["lowast"] = Convert.ToChar(8727);
            decodeOnlyBuilder["radic"] = Convert.ToChar(8730);
            decodeOnlyBuilder["prop"] = Convert.ToChar(8733);
            decodeOnlyBuilder["infin"] = Convert.ToChar(8734);
            decodeOnlyBuilder["ang"] = Convert.ToChar(8736);
            decodeOnlyBuilder["and"] = Convert.ToChar(8743);
            decodeOnlyBuilder["or"] = Convert.ToChar(8744);
            decodeOnlyBuilder["cap"] = Convert.ToChar(8745);
            decodeOnlyBuilder["cup"] = Convert.ToChar(8746);
            decodeOnlyBuilder["int"] = Convert.ToChar(8747);
            decodeOnlyBuilder["there4"] = Convert.ToChar(8756);
            decodeOnlyBuilder["sim"] = Convert.ToChar(8764);
            decodeOnlyBuilder["cong"] = Convert.ToChar(8773);
            decodeOnlyBuilder["asymp"] = Convert.ToChar(8776);
            decodeOnlyBuilder["ne"] = Convert.ToChar(8800);
            decodeOnlyBuilder["equiv"] = Convert.ToChar(8801);
            decodeOnlyBuilder["le"] = Convert.ToChar(8804);
            decodeOnlyBuilder["ge"] = Convert.ToChar(8805);
            decodeOnlyBuilder["sub"] = Convert.ToChar(8834);
            decodeOnlyBuilder["sup"] = Convert.ToChar(8835);
            decodeOnlyBuilder["nsub"] = Convert.ToChar(8836);
            decodeOnlyBuilder["sube"] = Convert.ToChar(8838);
            decodeOnlyBuilder["supe"] = Convert.ToChar(8839);
            decodeOnlyBuilder["oplus"] = Convert.ToChar(8853);
            decodeOnlyBuilder["otimes"] = Convert.ToChar(8855);
            decodeOnlyBuilder["perp"] = Convert.ToChar(8869);
            decodeOnlyBuilder["sdot"] = Convert.ToChar(8901);

            // Greek Letters Supported by HTML
            decodeOnlyBuilder["Alpha"] = Convert.ToChar(913);
            decodeOnlyBuilder["Beta"] = Convert.ToChar(914);
            decodeOnlyBuilder["Gamma"] = Convert.ToChar(915);
            decodeOnlyBuilder["Delta"] = Convert.ToChar(916);
            decodeOnlyBuilder["Epsilon"] = Convert.ToChar(917);
            decodeOnlyBuilder["Zeta"] = Convert.ToChar(918);
            decodeOnlyBuilder["Eta"] = Convert.ToChar(919);
            decodeOnlyBuilder["Theta"] = Convert.ToChar(920);
            decodeOnlyBuilder["Iota"] = Convert.ToChar(921);
            decodeOnlyBuilder["Kappa"] = Convert.ToChar(922);
            decodeOnlyBuilder["Lambda"] = Convert.ToChar(923);
            decodeOnlyBuilder["Mu"] = Convert.ToChar(924);
            decodeOnlyBuilder["Nu"] = Convert.ToChar(925);
            decodeOnlyBuilder["Xi"] = Convert.ToChar(926);
            decodeOnlyBuilder["Omicron"] = Convert.ToChar(927);
            decodeOnlyBuilder["Pi"] = Convert.ToChar(928);
            decodeOnlyBuilder["Rho"] = Convert.ToChar(929);
            decodeOnlyBuilder["Sigma"] = Convert.ToChar(931);
            decodeOnlyBuilder["Tau"] = Convert.ToChar(932);
            decodeOnlyBuilder["Upsilon"] = Convert.ToChar(933);
            decodeOnlyBuilder["Phi"] = Convert.ToChar(934);
            decodeOnlyBuilder["Chi"] = Convert.ToChar(935);
            decodeOnlyBuilder["Psi"] = Convert.ToChar(936);
            decodeOnlyBuilder["Omega"] = Convert.ToChar(937);
            decodeOnlyBuilder["alpha"] = Convert.ToChar(945);
            decodeOnlyBuilder["beta"] = Convert.ToChar(946);
            decodeOnlyBuilder["gamma"] = Convert.ToChar(947);
            decodeOnlyBuilder["delta"] = Convert.ToChar(948);
            decodeOnlyBuilder["epsilon"] = Convert.ToChar(949);
            decodeOnlyBuilder["zeta"] = Convert.ToChar(950);
            decodeOnlyBuilder["eta"] = Convert.ToChar(951);
            decodeOnlyBuilder["theta"] = Convert.ToChar(952);
            decodeOnlyBuilder["iota"] = Convert.ToChar(953);
            decodeOnlyBuilder["kappa"] = Convert.ToChar(954);
            decodeOnlyBuilder["lambda"] = Convert.ToChar(955);
            decodeOnlyBuilder["mu"] = Convert.ToChar(956);
            decodeOnlyBuilder["nu"] = Convert.ToChar(957);
            decodeOnlyBuilder["xi"] = Convert.ToChar(958);
            decodeOnlyBuilder["omicron"] = Convert.ToChar(959);
            decodeOnlyBuilder["pi"] = Convert.ToChar(960);
            decodeOnlyBuilder["rho"] = Convert.ToChar(961);
            decodeOnlyBuilder["sigmaf"] = Convert.ToChar(962);
            decodeOnlyBuilder["sigma"] = Convert.ToChar(963);
            decodeOnlyBuilder["tau"] = Convert.ToChar(964);
            decodeOnlyBuilder["upsilon"] = Convert.ToChar(965);
            decodeOnlyBuilder["phi"] = Convert.ToChar(966);
            decodeOnlyBuilder["chi"] = Convert.ToChar(967);
            decodeOnlyBuilder["psi"] = Convert.ToChar(968);
            decodeOnlyBuilder["omega"] = Convert.ToChar(969);
            decodeOnlyBuilder["thetasym"] = Convert.ToChar(977);
            decodeOnlyBuilder["upsih"] = Convert.ToChar(978);
            decodeOnlyBuilder["piv"] = Convert.ToChar(982);

            // Other Entities Supported by HTML
            decodeOnlyBuilder["OElig"] = Convert.ToChar(338);
            decodeOnlyBuilder["oelig"] = Convert.ToChar(339);
            decodeOnlyBuilder["Scaron"] = Convert.ToChar(352);
            decodeOnlyBuilder["scaron"] = Convert.ToChar(353);
            decodeOnlyBuilder["Yuml"] = Convert.ToChar(376);
            decodeOnlyBuilder["fnof"] = Convert.ToChar(402);
            decodeOnlyBuilder["circ"] = Convert.ToChar(710);
            decodeOnlyBuilder["tilde"] = Convert.ToChar(732);
            decodeOnlyBuilder["ndash"] = Convert.ToChar(8211);
            decodeOnlyBuilder["mdash"] = Convert.ToChar(8212);
            decodeOnlyBuilder["lsquo"] = Convert.ToChar(8216);
            decodeOnlyBuilder["rsquo"] = Convert.ToChar(8217);
            decodeOnlyBuilder["sbquo"] = Convert.ToChar(8218);
            decodeOnlyBuilder["ldquo"] = Convert.ToChar(8220);
            decodeOnlyBuilder["rdquo"] = Convert.ToChar(8221);
            decodeOnlyBuilder["bdquo"] = Convert.ToChar(8222);
            decodeOnlyBuilder["dagger"] = Convert.ToChar(8224);
            decodeOnlyBuilder["Dagger"] = Convert.ToChar(8225);
            decodeOnlyBuilder["bull"] = Convert.ToChar(8226);
            decodeOnlyBuilder["hellip"] = Convert.ToChar(8230);
            decodeOnlyBuilder["permil"] = Convert.ToChar(8240);
            decodeOnlyBuilder["prime"] = Convert.ToChar(8242);
            decodeOnlyBuilder["Prime"] = Convert.ToChar(8243);
            decodeOnlyBuilder["lsaquo"] = Convert.ToChar(8249);
            decodeOnlyBuilder["rsaquo"] = Convert.ToChar(8250);
            decodeOnlyBuilder["oline"] = Convert.ToChar(8254);
            decodeOnlyBuilder["euro"] = Convert.ToChar(8364);
            decodeOnlyBuilder["trade"] = Convert.ToChar(153);
            decodeOnlyBuilder["larr"] = Convert.ToChar(8592);
            decodeOnlyBuilder["uarr"] = Convert.ToChar(8593);
            decodeOnlyBuilder["rarr"] = Convert.ToChar(8594);
            decodeOnlyBuilder["darr"] = Convert.ToChar(8595);
            decodeOnlyBuilder["harr"] = Convert.ToChar(8596);
            decodeOnlyBuilder["crarr"] = Convert.ToChar(8629);
            decodeOnlyBuilder["lceil"] = Convert.ToChar(8968);
            decodeOnlyBuilder["rceil"] = Convert.ToChar(8969);
            decodeOnlyBuilder["lfloor"] = Convert.ToChar(8970);
            decodeOnlyBuilder["rfloor"] = Convert.ToChar(8971);
            decodeOnlyBuilder["loz"] = Convert.ToChar(9674);
            decodeOnlyBuilder["spades"] = Convert.ToChar(9824);
            decodeOnlyBuilder["clubs"] = Convert.ToChar(9827);
            decodeOnlyBuilder["hearts"] = Convert.ToChar(9829);
            decodeOnlyBuilder["diams"] = Convert.ToChar(9830);

            _decodeOnly = decodeOnlyBuilder.ToFrozenDictionary(StringComparer.InvariantCultureIgnoreCase);
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
        /// Handles &lt;, &gt;, "&amp;.
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

        // https://html.spec.whatwg.org/dev/dom.html#concept-element-tag-omission
        public static bool CanEndTagBeOmitted(string currentTagName, string tagName)
        {
            return currentTagName switch
            {
                "p" => ShouldTagCloseParagraph(tagName),
                "td" => ShouldTagCloseTableCell(tagName),
                "tr" => ShouldTagCloseTableRow(tagName),
                _ => false
            };
        }

        // Close <p> tags per https://html.spec.whatwg.org/dev/grouping-content.html#the-p-element
        public static bool ShouldTagCloseParagraph(string tagName)
        {
            return tagName switch
            {
                "address" or "article" or "aside" or "blockquote" or "details" or "dialog" or "div" or "dl"
                    or "fieldset" or "figcaption" or "figure" or "footer" or "form" or "h1" or "h2" or "h3" or "h4"
                    or "h5" or "h6" or "header" or "hgroup" or "hr" or "main" or "menu" or "nav" or "ol" or "p" or "pre"
                    or "search" or "section" or "table" or "ul" => true,
                _ => false
            };
        }

        // Close <td> tags per https://html.spec.whatwg.org/dev/tables.html#the-td-element
        public static bool ShouldTagCloseTableCell(string tagName)
        {
            return tagName switch
            {
                "td" or "th" or "tr" => true,
                _ => false
            };
        }

        public static bool ShouldTagCloseTableRow(string tagName)
        {
            return tagName is "tr";
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

                string repl = string.Empty;
                if (num >= 0 && num <= 0x10ffff && !(num >= 0xd800 && num <= 0xdfff))
                    repl = char.ConvertFromUtf32((int)num);

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