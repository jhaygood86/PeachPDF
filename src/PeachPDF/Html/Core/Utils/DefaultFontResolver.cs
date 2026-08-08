using PeachPDF.Fonts;
using PeachPDF.PdfSharpCore.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Resolves the platform-appropriate default font-size and font-family used when no
    /// CSS <c>font-size</c>/<c>font-family</c> is specified. Not a CSS keyword grammar - see
    /// <see cref="PeachPDF.CSS.Keywords"/> for those - this is purely a
    /// runtime/platform concern.
    /// </summary>
    internal static class DefaultFontResolver
    {
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
            OperatingSystem.IsAndroid(), OperatingSystem.IsBrowser());

        internal static string DetermineDefaultFont(bool isWindows, bool isMacOS, bool isLinux, bool isAndroid, bool isBrowser)
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
            // most likely to have bundled; PdfSharpAdapter.AddFont then points the default at
            // whatever family is actually registered first if that guess turns out wrong.
            if (isBrowser)
                return PickBrowserDefaultFont(GetInstalledFontFamilyNames());

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
