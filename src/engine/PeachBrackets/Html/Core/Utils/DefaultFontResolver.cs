using PeachDrawing.Text;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Html.Utils
{
    /// <summary>
    /// Resolves the platform-appropriate default font-size and font-family used when no
    /// CSS <c>font-size</c>/<c>font-family</c> is specified. Not a CSS keyword grammar - see
    /// <see cref="PeachBrackets.Css.Keywords"/> for those - this is purely a
    /// runtime/platform concern.
    /// </summary>
    internal static class DefaultFontResolver
    {
        /// <summary>
        /// Default font size in points. Change this value to modify the default font size.
        /// </summary>
        public const double FontSize = 11f;

        /// <summary>
        /// The size of <c>font-size: medium</c> in points for the medium: 12 pt (16 CSS px, what every browser uses) on screen,
        /// <see cref="FontSize"/> on paper, where the page, not a browser, sets the type.
        /// </summary>
        public static double Medium(bool screen) => screen ? ScreenMedium : FontSize;

        /// <summary>Medium on screen: 16 px.</summary>
        private const double ScreenMedium = 12;

        /// <summary>
        /// The size in points of an absolute-size keyword, given as its step from <c>medium</c> on paper (-4 <c>xx-small</c>, -3 <c>x-small</c>,
        /// -2 <c>small</c>, 0 <c>medium</c>, 2 <c>large</c>, 3 <c>x-large</c>, 4 <c>xx-large</c>). Screen follows the scale browsers use (CSS Fonts 4
        /// §2.5, "font-size-prop": 9, 10, 13, 16, 18, 24 and 32 px around a 16 px medium), which is not a fixed step.
        /// </summary>
        public static double Keyword(int paperStep, bool screen) => !screen
            ? FontSize + paperStep
            : paperStep switch
            {
                -4 => 6.75,
                -3 => 7.5,
                -2 => 9.75,
                2 => 13.5,
                3 => 18,
                4 => 24,
                _ => ScreenMedium,
            };

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
        /// Common metrically-compatible Arial substitutes for a browser/WebAssembly host, in
        /// preference order. Such a host has no discoverable system fonts at all — the
        /// application must register its own — so in practice this list names what an
        /// application is most likely to have bundled, and Liberation Sans (metrically
        /// compatible with Arial, and OFL-licensed) is the conventional choice.
        /// </summary>
        /// <remarks>
        /// Must be declared (and therefore initialized) before <see cref="DefaultFont"/> —
        /// see the remarks on <see cref="LinuxArialAlternatives"/>.
        /// </remarks>
        private static readonly string[] BrowserArialAlternatives =
        [
            "Liberation Sans",
            "Arimo",
            "DejaVu Sans",
            "Noto Sans",
            "Helvetica",
            "Arial",
        ];

        /// <summary>
        /// Default font used when no font-family is specified. "Segoe UI" only exists on
        /// Windows, so macOS, Linux, Android, and browser/WebAssembly hosts need a different,
        /// actually-installed default.
        /// </summary>
        public static readonly string DefaultFont = DetermineDefaultFont(
            OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), OperatingSystem.IsLinux(),
            OperatingSystem.IsAndroid(), OperatingSystem.IsBrowser(),
            OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsTvOS());

        /// <summary>
        /// The families an Apple platform other than macOS (iOS, iPadOS, Mac Catalyst, tvOS) may have to stand in for the Arial macOS
        /// uses, in preference order: those platforms ship Arial too, but not every release does.
        /// </summary>
        private static readonly string[] AppleArialAlternatives = ["Arial", "Helvetica", "Helvetica Neue", "Times New Roman"];

        internal static string DetermineDefaultFont(bool isWindows, bool isMacOS, bool isLinux, bool isAndroid, bool isBrowser, bool isAppleMobile = false)
        {
            // Checked before isLinux: Android is Linux-kernel-based and isLinux may also be
            // true there depending on how it was computed, so Android must take priority.
            if (isAndroid)
                return PickAndroidDefaultFont(GetInstalledFontFamilyNames());

            // Likewise checked early: a browser/WebAssembly host matches none of the desktop
            // platforms and would otherwise fall through to the "Segoe UI" default below - a
            // font that cannot possibly be present, since FontResolver discovers none there.
            // Nothing is registered yet when this runs (the application registers its fonts
            // afterwards, via PdfGenerator.AddFontFromStream), so this names the font it is
            // most likely to have bundled; HtmlRenderContext.AddFont then points the default at
            // whatever family is actually registered first if that guess turns out wrong.
            if (isBrowser)
                return PickBrowserDefaultFont(GetInstalledFontFamilyNames());

            if (isWindows)
                return "Segoe UI";

            if (isMacOS)
                return "Arial";

            // iOS, iPadOS, Mac Catalyst and tvOS would otherwise fall through to "Segoe UI", which none of them has.
            if (isAppleMobile)
                return PickAppleMobileDefaultFont(GetInstalledFontFamilyNames());

            if (isLinux)
                return PickLinuxDefaultFont(GetInstalledFontFamilyNames());

            return "Segoe UI";
        }

        /// <summary>
        /// The family names of the fonts installed on this machine - every face of a <c>.ttc</c>/<c>.otc</c>
        /// collection included, and any file the engine could not parse left out.
        /// </summary>
        internal static IEnumerable<string> GetInstalledFontFamilyNames() => FontSet.InstalledFamilyNames;

        /// <summary>
        /// Picks the best available default font from a list of installed font family
        /// names, preferring common Arial alternatives and otherwise falling back to
        /// whatever font was actually found so this never names a font that isn't there.
        /// </summary>
        internal static string PickLinuxDefaultFont(IEnumerable<string> installedFontFamilyNames) =>
            PickBestAvailableFont(installedFontFamilyNames, LinuxArialAlternatives);

        /// <summary>The default font of iOS, iPadOS, Mac Catalyst and tvOS: Arial where it is installed, else the nearest family that is.</summary>
        internal static string PickAppleMobileDefaultFont(IEnumerable<string> installedFontFamilyNames) =>
            PickBestAvailableFont(installedFontFamilyNames, AppleArialAlternatives);

        /// <summary>
        /// Picks the best available default font from a list of installed font family
        /// names, preferring common Arial alternatives available on Android and otherwise
        /// falling back to whatever font was actually found so this never names a font
        /// that isn't there.
        /// </summary>
        internal static string PickAndroidDefaultFont(IEnumerable<string> installedFontFamilyNames) =>
            PickBestAvailableFont(installedFontFamilyNames, AndroidArialAlternatives);

        /// <summary>
        /// Picks the best available default font from a list of installed font family
        /// names for a browser/WebAssembly host. That list is normally empty there (nothing
        /// is discoverable, and the application registers its fonts after this runs), so in
        /// practice this returns the first preference — Liberation Sans.
        /// </summary>
        internal static string PickBrowserDefaultFont(IEnumerable<string> installedFontFamilyNames) =>
            PickBestAvailableFont(installedFontFamilyNames, BrowserArialAlternatives);

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
