using PeachPDF.PdfSharpCore.Utils;
using System;
using System.IO;
using System.Linq;

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>
    /// Font-related tests should never depend on what fonts happen to be installed on the
    /// machine running them. These bundled, OFL-1.1-licensed assets guarantee at least one
    /// TrueType (glyf) and one OpenType (CFF) font are always available, regardless of
    /// platform or CI environment.
    ///
    /// The TTF (Source Sans 3) and OTF (Source Code Pro) intentionally come from different
    /// font families rather than being TTF/OTF flavors of the same family: PeachPDF's
    /// process-wide <c>FontFamilyCache</c> caches resolved font data keyed only by family
    /// name, so a same-named TTF and OTF loaded in the same test run would collide and one
    /// would silently shadow the other.
    /// </summary>
    internal static class BundledFonts
    {
        internal static string Ttf => Path.Combine(AppContext.BaseDirectory, "SourceSans3-Regular.ttf");

        internal static string Otf => Path.Combine(AppContext.BaseDirectory, "SourceCodePro-Regular.otf");

        internal static string Woff2 => Path.Combine(AppContext.BaseDirectory, "Inter-Medium.woff2");

        /// <summary>
        /// A real font file path: the first one the host OS reports, or the bundled TTF
        /// if the host reports none.
        /// </summary>
        internal static string AnySupportedFontPath =>
            FontResolver.SupportedFonts.FirstOrDefault() ?? Ttf;

        /// <summary>
        /// Ensures <paramref name="resolver"/> can resolve at least one font family and
        /// returns its name, using a system font if one was detected or registering the
        /// bundled TTF as a custom font otherwise.
        /// </summary>
        internal static string GetOrRegisterKnownFamily(FontResolver resolver)
        {
            if (FontResolver.SupportedFonts.Length > 0)
                return TtfFontDescription.LoadDescription(FontResolver.SupportedFonts[0]).FontFamilyInvariantCulture;

            const string fallbackFamilyName = "__BundledTestFont__";
            using var stream = File.OpenRead(Ttf);
            resolver.AddFont(stream, fallbackFamilyName);
            return fallbackFamilyName;
        }
    }
}
