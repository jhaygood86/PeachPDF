using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace PeachDrawing.Text.Internal.Fonts
{
    /// <summary>
    /// Maps the generic font families (<c>serif</c>, <c>sans-serif</c>, <c>monospace</c>, <c>cursive</c>,
    /// <c>fantasy</c>, <c>system-ui</c>, <c>math</c>) to real family names, matching what Chromium does on each
    /// platform rather than one invented cross-platform table. Chromium hardcodes specific family names on Windows,
    /// macOS and Android, and delegates to the OS's own font matching (fontconfig) on Linux; see
    /// <see cref="LinuxSystemFontResolver.ResolveGenericFamily"/> for that half.
    /// </summary>
    /// <remarks>
    /// The values were checked against Chromium's own font-settings documentation and font-transition discussions
    /// (Windows, macOS, Android), cross-checked across multiple sources. Notably <c>monospace</c> is Consolas
    /// (Windows) and Menlo (macOS), and not the more common "Courier New" substitute this library used before this
    /// table existed.
    /// </remarks>
    internal static class GenericFamilyTable
    {
        private static readonly FrozenDictionary<GenericFamily, string> Windows = new Dictionary<GenericFamily, string>
        {
            [GenericFamily.Serif] = "Times New Roman",
            [GenericFamily.SansSerif] = "Arial",
            [GenericFamily.Monospace] = "Consolas",
            [GenericFamily.Cursive] = "Comic Sans MS",
            [GenericFamily.Fantasy] = "Impact"
        }.ToFrozenDictionary();

        // The Apple platforms follow WebKit's defaults, not Chromium's: Safari's standard, sans-serif and fixed fonts
        // are Times, Helvetica and Courier (Chromium's macOS table has Menlo for monospace). The user interface
        // typefaces are what WebKit maps system-ui and the ui-* generics to; the names here are only the answer
        // when the platform's own question (see AppleUiDesign) could not be asked or had no answer.
        private static readonly FrozenDictionary<GenericFamily, string[]> AppleNames = new Dictionary<GenericFamily, string[]>
        {
            [GenericFamily.Serif] = ["Times", "Times New Roman"],
            [GenericFamily.SansSerif] = ["Helvetica", "Helvetica Neue"],
            [GenericFamily.Monospace] = ["Courier", "Courier New", "Menlo"],
            [GenericFamily.Cursive] = ["Apple Chancery", "Snell Roundhand"],
            [GenericFamily.Fantasy] = ["Papyrus", "Marker Felt"],
            [GenericFamily.SystemUi] = ["Helvetica Neue", "Helvetica"],
            [GenericFamily.UiSansSerif] = ["Helvetica Neue", "Helvetica"],
            [GenericFamily.UiSerif] = ["Times", "Times New Roman"],
            [GenericFamily.UiMonospace] = ["SF Mono", "Menlo", "Courier"],
            [GenericFamily.UiRounded] = ["SF Pro Rounded", "Helvetica Neue", "Helvetica"]
        }.ToFrozenDictionary();

        private static readonly FrozenDictionary<GenericFamily, string> Android = new Dictionary<GenericFamily, string>
        {
            [GenericFamily.Serif] = "Noto Serif",
            [GenericFamily.SansSerif] = "Roboto",
            [GenericFamily.Monospace] = "Droid Sans Mono",
            // Android has no distinct fantasy font either - Chromium reuses cursive's for it there too.
            [GenericFamily.Cursive] = "Dancing Script",
            [GenericFamily.Fantasy] = "Dancing Script"
        }.ToFrozenDictionary();

        // Chromium resolves the math generic to the hardcoded family "Latin Modern Math" on every platform except
        // Android (which has its own setting). That font is not installed by default on Windows or macOS, so each
        // platform's chain continues with the math-capable font it does ship (Cambria Math on Windows, STIX Two Math
        // on macOS) - a chain rather than one name, because "is this family installed" is asked per candidate.
        private static readonly string[] WindowsMathChain = ["Latin Modern Math", "Cambria Math", "STIX Two Math"];
        private static readonly string[] MacOSMathChain = ["Latin Modern Math", "STIX Two Math", "Cambria Math"];
        private static readonly string[] AndroidMathChain = ["Noto Sans Math", "STIX Two Math", "Latin Modern Math"];
        private static readonly string[] OtherMathChain = ["Latin Modern Math", "STIX Two Math", "DejaVu Math TeX Gyre", "Cambria Math", "Noto Sans Math"];

        /// <summary>The CSS keyword of a generic family, which is what fontconfig is asked for on Linux.</summary>
        internal static string CssName(GenericFamily generic) => generic switch
        {
            GenericFamily.Serif => "serif",
            GenericFamily.SansSerif => "sans-serif",
            GenericFamily.Monospace => "monospace",
            GenericFamily.Cursive => "cursive",
            GenericFamily.Fantasy => "fantasy",
            GenericFamily.SystemUi => "system-ui",
            GenericFamily.Math => "math",
            GenericFamily.UiSerif => "ui-serif",
            GenericFamily.UiSansSerif => "ui-sans-serif",
            GenericFamily.UiMonospace => "ui-monospace",
            GenericFamily.UiRounded => "ui-rounded",
            _ => throw new ArgumentOutOfRangeException(nameof(generic), generic, "Not a generic family.")
        };

        /// <summary>
        /// The hardcoded, Chromium-matched name for <paramref name="generic"/> on whichever platform flag is true, or
        /// null when the platform has none (Linux, which asks fontconfig instead, and <c>system-ui</c> and
        /// <c>math</c> everywhere). Takes explicit platform booleans rather than querying the OS, so the table itself
        /// is unit-testable on any host. Checked in Android, Windows, macOS order: Android is Linux-kernel-based and
        /// must win over any Linux flag that is also true.
        /// </summary>
        internal static string? PlatformDefault(GenericFamily generic, bool isWindows, bool isMacOS, bool isAndroid)
        {
            var table = isAndroid ? Android : isWindows ? Windows : null;
            if (table is not null)
                return table.TryGetValue(generic, out var resolved) ? resolved : null;

            return isMacOS && AppleNames.TryGetValue(generic, out var names) ? names[0] : null;
        }

        /// <summary>
        /// The generic family a <c>ui-*</c> generic stands for where the platform has no typeface of its own to offer for
        /// it (everywhere but the Apple platforms): <c>ui-serif</c> is the serif, <c>ui-monospace</c> the monospace,
        /// <c>ui-sans-serif</c> and <c>ui-rounded</c> the interface typeface. Any other generic is its own equivalent.
        /// </summary>
        internal static GenericFamily Equivalent(GenericFamily generic) => generic switch
        {
            GenericFamily.UiSerif => GenericFamily.Serif,
            GenericFamily.UiMonospace => GenericFamily.Monospace,
            GenericFamily.UiSansSerif or GenericFamily.UiRounded => GenericFamily.SystemUi,
            _ => generic
        };

        /// <summary>The Apple platforms' own interface design a generic is, or null for a generic that is not one.</summary>
        internal static Apple.AppleUiDesign? AppleUiDesignOf(GenericFamily generic) => generic switch
        {
            GenericFamily.SystemUi or GenericFamily.UiSansSerif => Apple.AppleUiDesign.System,
            GenericFamily.UiSerif => Apple.AppleUiDesign.Serif,
            GenericFamily.UiMonospace => Apple.AppleUiDesign.Monospaced,
            GenericFamily.UiRounded => Apple.AppleUiDesign.Rounded,
            _ => null
        };

        /// <summary>
        /// The first family of the platform's math chain that <paramref name="isAvailable"/> reports as present, or
        /// null when none is. fontconfig is deliberately not asked on Linux: a pattern for "math" matches no rule and
        /// <c>fc-match math</c> answers with the ordinary default sans-serif family, an installed non-math font that
        /// would then always beat a real math font further down the chain.
        /// </summary>
        internal static string? ResolveMathFamily(bool isWindows, bool isMacOS, bool isAndroid, Func<string, bool> isAvailable)
        {
            var chain = isAndroid ? AndroidMathChain : isWindows ? WindowsMathChain : isMacOS ? MacOSMathChain : OtherMathChain;

            foreach (var family in chain)
            {
                if (isAvailable(family))
                {
                    return family;
                }
            }

            return null;
        }

        /// <summary>
        /// The family <paramref name="generic"/> resolves to, or null when the answer is not available.
        /// </summary>
        /// <param name="generic">The generic family.</param>
        /// <param name="operatingSystemAnswer">
        /// What the operating system's own font matching (fontconfig) said for it, or null off Linux and whenever it
        /// could not answer.
        /// </param>
        /// <param name="isWindows">Whether the platform is Windows.</param>
        /// <param name="isMacOS">Whether the platform is macOS.</param>
        /// <param name="isAndroid">Whether the platform is Android.</param>
        /// <param name="isAvailable">Whether a family name is one the caller can use.</param>
        /// <param name="isIOS">Whether the platform is iOS (or another Apple platform that is not macOS), which shares macOS's table.</param>
        internal static string? Resolve(GenericFamily generic, string? operatingSystemAnswer, bool isWindows, bool isMacOS, bool isAndroid, Func<string, bool> isAvailable, bool isIOS = false)
        {
            var isApple = isMacOS || isIOS;

            // Only the Apple platforms have a typeface of their own behind a ui-* generic; elsewhere it is the generic it resembles.
            if (!isApple)
            {
                generic = Equivalent(generic);
            }

            if (generic == GenericFamily.Math)
            {
                return ResolveMathFamily(isWindows, isApple, isAndroid, isAvailable);
            }

            // The platform's own answer first, then the platform's table: the answer is asked of the system itself,
            // the table is what a system that cannot answer is assumed to have. Where the table is empty (Linux) the
            // answer stands alone, as it always has.
            if (operatingSystemAnswer is not null && isAvailable(operatingSystemAnswer))
            {
                return operatingSystemAnswer;
            }

            if (isApple)
            {
                if (AppleNames.TryGetValue(generic, out var names))
                {
                    foreach (var name in names)
                    {
                        if (isAvailable(name))
                        {
                            return name;
                        }
                    }
                }

                return null;
            }

            var target = PlatformDefault(generic, isWindows, isMacOS, isAndroid);
            return target is not null && isAvailable(target) ? target : null;
        }
    }
}
