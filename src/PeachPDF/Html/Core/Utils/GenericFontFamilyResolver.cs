using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Maps CSS generic font families (<c>serif</c>/<c>sans-serif</c>/<c>monospace</c>/<c>cursive</c>/
    /// <c>fantasy</c>) to real installed family names, matching real Chromium behavior per platform rather
    /// than a single invented cross-platform table. Chromium hardcodes specific family names on Windows,
    /// macOS, and Android, but delegates to the OS's own font-matching (fontconfig) on Linux - see
    /// <c>PdfSharpCore.Utils.LinuxSystemFontResolver.ResolveGenericFamily</c> for that half.
    /// </summary>
    /// <remarks>
    /// Values verified against Chromium's own font-settings documentation and font-transition discussions
    /// (Windows/macOS/Android), cross-checked across multiple sources. Notably <c>monospace</c> is
    /// Consolas (Windows) / Menlo (macOS) - not the more common "Courier New" substitute this library used
    /// before this table existed.
    /// </remarks>
    internal static class GenericFontFamilyResolver
    {
        /// <summary>Every CSS generic family this resolver maps (excludes <c>system-ui</c>, handled separately - see <see cref="CssConstants.DefaultFont"/>).</summary>
        internal static readonly string[] Generics =
        [
            CssConstants.Serif,
            CssConstants.SansSerif,
            CssConstants.Monospace,
            CssConstants.Cursive,
            CssConstants.Fantasy
        ];

        private static readonly FrozenDictionary<string, string> Windows =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [CssConstants.Serif] = "Times New Roman",
                [CssConstants.SansSerif] = "Arial",
                [CssConstants.Monospace] = "Consolas",
                [CssConstants.Cursive] = "Comic Sans MS",
                [CssConstants.Fantasy] = "Impact"
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        private static readonly FrozenDictionary<string, string> MacOS =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [CssConstants.Serif] = "Times",
                [CssConstants.SansSerif] = "Helvetica",
                [CssConstants.Monospace] = "Menlo",
                [CssConstants.Cursive] = "Apple Chancery",
                [CssConstants.Fantasy] = "Papyrus"
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        private static readonly FrozenDictionary<string, string> Android =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [CssConstants.Serif] = "Noto Serif",
                [CssConstants.SansSerif] = "Roboto",
                [CssConstants.Monospace] = "Droid Sans Mono",
                // Android has no distinct fantasy font either - Chromium reuses cursive's for it there too.
                [CssConstants.Cursive] = "Dancing Script",
                [CssConstants.Fantasy] = "Dancing Script"
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Resolves <paramref name="genericFamily"/> (one of <see cref="Generics"/>) against the
        /// hardcoded, Chromium-matched table for whichever platform flag is true. Takes explicit platform
        /// booleans (mirroring <see cref="PdfSharpCore.Utils.FontResolver.DiscoverSupportedFonts"/>'s own
        /// precedent) rather than querying <see cref="OperatingSystem"/> internally, so the table itself is
        /// directly unit-testable on any CI runner regardless of host OS. Checked in Android/Windows/macOS
        /// order (Android is Linux-kernel-based and must take priority over any Linux flag also being
        /// true); returns the generic name itself unchanged if none apply (the caller's own
        /// installed-family verification step then substitutes a real fallback).
        /// </summary>
        internal static string ResolvePlatformDefault(string genericFamily, bool isWindows, bool isMacOS, bool isAndroid)
        {
            var table = isAndroid ? Android : isWindows ? Windows : isMacOS ? MacOS : null;
            return table is not null && table.TryGetValue(genericFamily, out var resolved) ? resolved : genericFamily;
        }
    }
}
