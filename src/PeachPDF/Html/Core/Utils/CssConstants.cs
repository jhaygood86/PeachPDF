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

using PeachPDF.PdfSharpCore.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// String constants to avoid typing errors.
    /// </summary>
    internal static class CssConstants
    {
        public const string Absolute = "absolute";
        public const string After = "after";
        public const string Always = "always";
        public const string Auto = "auto";
        public const string Avoid = "avoid";
        public const string Baseline = "baseline";
        public const string Before = "before";
        public const string Blink = "blink";
        public const string Block = "block";
        public const string InlineBlock = "inline-block";
        public const string Bold = "bold";
        public const string Bolder = "bolder";
        public const string BorderBox = "border-box";
        public const string Both = "both";
        public const string Bottom = "bottom";
        public const string BreakAll = "break-all";
        public const string KeepAll = "keep-all";
        public const string Center = "center";
        public const string Collapse = "collapse";
        public const string Counter = "counter";
        public const string ContentBox = "content-box";
        public const string CurrentColor = "currentcolor";
        public const string Cursive = "cursive";
        public const string Circle = "circle";
        public const string Decimal = "decimal";
        public const string DecimalLeadingZero = "decimal-leading-zero";
        public const string Disc = "disc";
        public const string Fantasy = "fantasy";
        public const string Fixed = "fixed";
        public const string Hide = "hide";
        public const string Inherit = "inherit";
        public const string Initial = "initial";
        public const string Revert = "revert";
        public const string RevertLayer = "revert-layer";
        public const string Unset = "unset";
        public const string Inline = "inline";
        public const string InlineTable = "inline-table";
        public const string Inside = "inside";
        public const string Inset = "inset";
        public const string Italic = "italic";
        public const string Justify = "justify";
        public const string Large = "large";
        public const string Larger = "larger";
        public const string Left = "left";
        public const string Lighter = "lighter";
        public const string LineThrough = "line-through";
        public const string ListItem = "list-item";
        public const string Ltr = "ltr";
        public const string LowerAlpha = "lower-alpha";
        public const string LowerLatin = "lower-latin";
        public const string LowerRoman = "lower-roman";
        public const string LowerGreek = "lower-greek";
        public const string Armenian = "armenian";
        public const string Georgian = "georgian";
        public const string Hebrew = "hebrew";
        public const string Hiragana = "hiragana";
        public const string HiraganaIroha = "hiragana-iroha";
        public const string Katakana = "katakana";
        public const string KatakanaIroha = "katakana-iroha";
        public const string Medium = "medium";
        public const string Middle = "middle";
        public const string Monospace = "monospace";
        public const string None = "none";
        public const string Normal = "normal";
        public const string NoWrap = "nowrap";
        public const string Oblique = "oblique";
        public const string Outset = "outset";
        public const string Outside = "outside";
        public const string Overline = "overline";
        public const string PaddingBox = "padding-box";
        public const string Page = "page";
        public const string Percent = "%";
        public const string Pre = "pre";
        public const string PreWrap = "pre-wrap";
        public const string PreLine = "pre-line";
        public const string Relative = "relative";
        public const string Repeat = "repeat";
        public const string Rem = "rem";
        public const string Right = "right";
        public const string Rtl = "rtl";
        public const string SansSerif = "sans-serif";
        public const string Scroll = "scroll";
        public const string Serif = "serif";
        public const string Show = "show";
        public const string Small = "small";
        public const string Smaller = "smaller";
        public const string Solid = "solid";
        public const string Static = "static";
        public const string Sticky = "sticky";
        public const string Sub = "sub";
        public const string Super = "super";
        public const string Square = "square";
        public const string Table = "table";
        public const string TableRow = "table-row";
        public const string TableRowGroup = "table-row-group";
        public const string TableHeaderGroup = "table-header-group";
        public const string TableFooterGroup = "table-footer-group";
        public const string TableColumn = "table-column";
        public const string TableColumnGroup = "table-column-group";
        public const string TableCell = "table-cell";
        public const string TableCaption = "table-caption";
        public const string TextBottom = "text-bottom";
        public const string TextTop = "text-top";
        public const string Thin = "thin";
        public const string Thick = "thick";
        public const string Top = "top";
        public const string Transparent = "transparent";
        public const string Underline = "underline";
        public const string UpperAlpha = "upper-alpha";
        public const string UpperLatin = "upper-latin";
        public const string UpperRoman = "upper-roman";
        public const string XLarge = "x-large";
        public const string XSmall = "x-small";
        public const string XXLarge = "xx-large";
        public const string XXSmall = "xx-small";
        public const string Visible = "visible";
        public const string Hidden = "hidden";
        public const string Dotted = "dotted";
        public const string Dashed = "dashed";
        public const string Double = "double";
        public const string Groove = "groove";
        public const string Ridge = "ridge";
        public const string Wavy = "wavy";
        public const string PeachBaselineMiddle = "-peachpdf-baseline-middle"; // same as -webkit-baseline-middle

        public const string Flex          = "flex";
        public const string InlineFlex    = "inline-flex";
        public const string FlexStart     = "flex-start";
        public const string FlexEnd       = "flex-end";
        public const string SpaceBetween  = "space-between";
        public const string SpaceAround   = "space-around";
        public const string SpaceEvenly   = "space-evenly";
        public const string Stretch       = "stretch";
        public const string RowReverse    = "row-reverse";
        public const string Column        = "column";
        public const string ColumnReverse = "column-reverse";
        public const string WrapReverse   = "wrap-reverse";

        /// <summary>
        /// Centimeters
        /// </summary>
        public const string Cm = "cm";

        /// <summary>
        /// Millimeters
        /// </summary>
        public const string Mm = "mm";

        /// <summary>
        /// Pixels
        /// </summary>
        public const string Px = "px";

        /// <summary>
        /// Inches
        /// </summary>
        public const string In = "in";

        /// <summary>
        /// Em - The font size of the relevant font
        /// </summary>
        public const string Em = "em";

        /// <summary>
        /// The 'x-height' of the relevan font
        /// </summary>
        public const string Ex = "ex";

        /// <summary>
        /// Points
        /// </summary>
        public const string Pt = "pt";

        /// <summary>
        /// Picas
        /// </summary>
        public const string Pc = "pc";

        /// <summary>
        /// Default font size in points. Change this value to modify the default font size.
        /// </summary>
        public const double FontSize = 11f;

        /// <summary>
        /// Common metrically-compatible Arial substitutes shipped by mainstream Linux
        /// distributions, in preference order.
        /// </summary>
        /// <remarks>
        /// Must be declared (and therefore initialized) before <see cref="DefaultFont"/>:
        /// static field initializers run in textual declaration order, and DefaultFont's
        /// initializer transitively reads this array via PickLinuxDefaultFont.
        /// </remarks>
        private static readonly string[] LinuxArialAlternatives =
        [
            "Liberation Sans",
            "Arimo",
            "Nimbus Sans",
            "DejaVu Sans",
            "FreeSans",
            "Noto Sans",
            "Helvetica",
            "Verdana",
            "Arial",
        ];

        /// <summary>
        /// Common metrically-compatible Arial substitutes available on Android, in
        /// preference order. Roboto is the flagship system font on every Android version
        /// that ships a working font resolver (5.0+); Noto Sans is Google's cross-platform
        /// fallback family and is present on most devices for non-Latin script coverage.
        /// </summary>
        /// <remarks>
        /// Must be declared (and therefore initialized) before <see cref="DefaultFont"/> —
        /// see the remarks on <see cref="LinuxArialAlternatives"/>.
        /// </remarks>
        private static readonly string[] AndroidArialAlternatives =
        [
            "Roboto",
            "Noto Sans",
            "Droid Sans",
            "Liberation Sans",
            "Arimo",
            "DejaVu Sans",
            "Helvetica",
            "Arial",
        ];

        /// <summary>
        /// Default font used when no font-family is specified. "Segoe UI" only exists on
        /// Windows, so macOS, Linux, and Android need a different, actually-installed
        /// default.
        /// </summary>
        public static readonly string DefaultFont = DetermineDefaultFont(
            OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), OperatingSystem.IsLinux(), OperatingSystem.IsAndroid());

        internal static string DetermineDefaultFont(bool isWindows, bool isMacOS, bool isLinux, bool isAndroid)
        {
            // Checked before isLinux: Android is Linux-kernel-based and isLinux may also be
            // true there depending on how it was computed, so Android must take priority.
            if (isAndroid)
                return PickAndroidDefaultFont(GetInstalledFontFamilyNames());

            if (isWindows)
                return "Segoe UI";

            if (isMacOS)
                return "Arial";

            if (isLinux)
                return PickLinuxDefaultFont(GetInstalledFontFamilyNames());

            return "Segoe UI";
        }

        internal static IEnumerable<string> GetInstalledFontFamilyNames()
        {
            foreach (var path in FontResolver.SupportedFonts)
            {
                string? family = null;
                try
                {
                    family = TtfFontDescription.LoadDescription(path).FontFamilyInvariantCulture;
                }
                catch
                {
                    // Ignore unparsable/corrupt font files, same tolerance FontResolver itself uses.
                }

                if (!string.IsNullOrEmpty(family))
                    yield return family;
            }
        }

        /// <summary>
        /// Picks the best available default font from a list of installed font family
        /// names, preferring common Arial alternatives and otherwise falling back to
        /// whatever font was actually found so this never names a font that isn't there.
        /// </summary>
        internal static string PickLinuxDefaultFont(IEnumerable<string> installedFontFamilyNames) =>
            PickBestAvailableFont(installedFontFamilyNames, LinuxArialAlternatives);

        /// <summary>
        /// Picks the best available default font from a list of installed font family
        /// names, preferring common Arial alternatives available on Android and otherwise
        /// falling back to whatever font was actually found so this never names a font
        /// that isn't there.
        /// </summary>
        internal static string PickAndroidDefaultFont(IEnumerable<string> installedFontFamilyNames) =>
            PickBestAvailableFont(installedFontFamilyNames, AndroidArialAlternatives);

        /// <summary>
        /// Shared picking logic for <see cref="PickLinuxDefaultFont"/> and
        /// <see cref="PickAndroidDefaultFont"/>: prefer the first candidate (in preference
        /// order) that's actually installed, otherwise fall back to whatever font was found,
        /// otherwise fall back to <paramref name="preferenceOrder"/>'s first (and most
        /// likely to be present) entry so this never returns an empty string.
        /// </summary>
        private static string PickBestAvailableFont(IEnumerable<string> installedFontFamilyNames, string[] preferenceOrder)
        {
            var installed = new HashSet<string>(installedFontFamilyNames, StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in preferenceOrder)
            {
                if (installed.Contains(candidate))
                    return candidate;
            }

            return installed.FirstOrDefault() ?? preferenceOrder[0];
        }
    }
}