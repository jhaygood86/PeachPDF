using PeachPDF.CSS;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;

using PeachDrawing.Text.Internal.Fonts;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Maps CSS generic font families (<c>serif</c>/<c>sans-serif</c>/<c>monospace</c>/<c>cursive</c>/
    /// <c>fantasy</c>/<c>math</c>) to real installed family names, matching real Chromium behavior per platform rather
    /// than a single invented cross-platform table. Chromium hardcodes specific family names on Windows,
    /// macOS, and Android, but delegates to the OS's own font-matching (fontconfig) on Linux - see
    /// <c>PeachDrawing.Text.Internal.Fonts.LinuxSystemFontResolver.ResolveGenericFamily</c> for that half.
    /// </summary>
    /// <remarks>
    /// Values verified against Chromium's own font-settings documentation and font-transition discussions
    /// (Windows/macOS/Android), cross-checked across multiple sources. Notably <c>monospace</c> is
    /// Consolas (Windows) / Menlo (macOS) - not the more common "Courier New" substitute this library used
    /// before this table existed.
    /// </remarks>
    internal static class GenericFontFamilyResolver
    {
        /// <summary>
        /// Every CSS generic family this resolver maps (excludes <c>system-ui</c>, handled separately - see
        /// <see cref="DefaultFontResolver.DefaultFont"/>). <c>math</c> is in the list but is not a
        /// single-name mapping: see <see cref="ResolveMathFamily"/>.
        /// </summary>
        internal static readonly string[] Generics =
        [
            Keywords.Serif,
            Keywords.SansSerif,
            Keywords.Monospace,
            Keywords.Cursive,
            Keywords.Fantasy,
            Keywords.Math
        ];

        private static readonly FrozenDictionary<string, string> Windows =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [Keywords.Serif] = "Times New Roman",
                [Keywords.SansSerif] = "Arial",
                [Keywords.Monospace] = "Consolas",
                [Keywords.Cursive] = "Comic Sans MS",
                [Keywords.Fantasy] = "Impact"
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        private static readonly FrozenDictionary<string, string> MacOS =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [Keywords.Serif] = "Times",
                [Keywords.SansSerif] = "Helvetica",
                [Keywords.Monospace] = "Menlo",
                [Keywords.Cursive] = "Apple Chancery",
                [Keywords.Fantasy] = "Papyrus"
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        private static readonly FrozenDictionary<string, string> Android =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [Keywords.Serif] = "Noto Serif",
                [Keywords.SansSerif] = "Roboto",
                [Keywords.Monospace] = "Droid Sans Mono",
                // Android has no distinct fantasy font either - Chromium reuses cursive's for it there too.
                [Keywords.Cursive] = "Dancing Script",
                [Keywords.Fantasy] = "Dancing Script"
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Resolves <paramref name="genericFamily"/> (one of <see cref="Generics"/>) against the
        /// hardcoded, Chromium-matched table for whichever platform flag is true. Takes explicit platform
        /// booleans (mirroring <see cref="PeachDrawing.Text.Internal.Fonts.FontResolver.DiscoverSupportedFonts"/>'s own
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

        // Chromium resolves the math generic to the hardcoded family "Latin Modern Math" on every
        // platform except Android (which has its own setting). That font is not installed by default on
        // Windows or macOS, so each platform's chain continues with the math-capable font it does ship
        // (Cambria Math on Windows, STIX Two Math on macOS) - a chain rather than one name, because the
        // adapter's "is this family installed" check happens per candidate.
        private static readonly string[] WindowsMathChain = ["Latin Modern Math", "Cambria Math", "STIX Two Math"];
        private static readonly string[] MacOSMathChain = ["Latin Modern Math", "STIX Two Math", "Cambria Math"];
        private static readonly string[] AndroidMathChain = ["Noto Sans Math", "STIX Two Math", "Latin Modern Math"];
        private static readonly string[] OtherMathChain = ["Latin Modern Math", "STIX Two Math", "DejaVu Math TeX Gyre", "Cambria Math", "Noto Sans Math"];

        /// <summary>
        /// Resolves the <c>math</c> generic family (CSS Fonts 4 §2.1.1: "a font intended for mathematical
        /// expressions", typically one carrying an OpenType MATH table) to the first family of the
        /// platform's candidate chain that <paramref name="fontExists"/> reports as installed, or
        /// <c>null</c> when none is (the caller then substitutes the platform default font).
        /// </summary>
        /// <remarks>
        /// fontconfig is deliberately not asked on Linux: a pattern for "math" matches no rule and
        /// <c>fc-match math</c> answers with the ordinary default sans-serif family - an installed,
        /// non-math font that would then always win over a real math font further down the chain.
        /// Takes explicit platform booleans, like <see cref="ResolvePlatformDefault"/>, so every chain is
        /// unit-testable on any host.
        /// </remarks>
        internal static string? ResolveMathFamily(bool isWindows, bool isMacOS, bool isAndroid, Func<string, bool> fontExists)
        {
            var chain = isAndroid ? AndroidMathChain : isWindows ? WindowsMathChain : isMacOS ? MacOSMathChain : OtherMathChain;

            foreach (var family in chain)
            {
                if (fontExists(family))
                {
                    return family;
                }
            }

            return null;
        }
    }
}
