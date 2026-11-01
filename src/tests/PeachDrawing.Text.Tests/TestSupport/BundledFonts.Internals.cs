using PeachDrawing.Text.Internal.Fonts;

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>
    /// The part of <see cref="BundledFonts"/> that reads the engine's internals, so it lives only in the engine's
    /// test project: <c>PeachPDF.Tests</c> sees the public API alone.
    /// </summary>
    internal static partial class BundledFonts
    {
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
