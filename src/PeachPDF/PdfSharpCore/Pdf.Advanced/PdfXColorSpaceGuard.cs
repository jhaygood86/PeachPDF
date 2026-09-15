#nullable enable

using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.PdfSharpCore.Pdf.Advanced
{
    /// <summary>
    /// PDF/X-1a's CMYK-only content restriction (ISO 15930-1/4: every color must be CMYK or a named spot
    /// color - PeachPDF has no spot-color support, so in practice CMYK only). Called from
    /// <see cref="Drawing.Pdf.PdfGraphicsState.RealizeFillColor"/>/<see cref="Drawing.Pdf.PdfGraphicsState.RealizePen"/>
    /// right after <see cref="Pdf.Internal.ColorSpaceHelper"/>'s <c>EnsureColorMode</c>, mirroring
    /// <see cref="PdfACmykImageGuard"/>'s "throw a clear, conformance-specific exception" shape but for
    /// vector/text fill and stroke colors rather than embedded images. PDF/X-3 and PDF/X-4 permit
    /// ICC-managed RGB (the output intent profile describes how a RIP should interpret it), so neither
    /// needs this check - see <see cref="RequireAllowedOrConvert"/>.
    /// </summary>
    internal static class PdfXColorSpaceGuard
    {
        /// <summary>
        /// Returns <paramref name="color"/> unchanged if it's already CMYK, or if the document's
        /// <see cref="PdfDocumentOptions.PdfXConformance"/> isn't <see cref="PeachPDF.PdfXConformance.X1a"/>.
        /// Under X1a, an achromatic (gray, R==G==B) RGB color - the common case for un-set UA-stylesheet
        /// defaults like <c>color: black</c>, not something an author necessarily set deliberately - is
        /// converted to CMYK via <see cref="ColorOptions.BlackGeneration"/> rather than rejected (see
        /// <see cref="ColorBlackGeneration"/>'s doc comments: this has an exact, lossless ink mapping,
        /// unlike a chromatic color, so it isn't the kind of approximation this project otherwise
        /// declines to compute). A genuinely chromatic RGB color still throws
        /// <see cref="PdfXConformanceException"/> - there is no correct-by-construction way to turn an
        /// arbitrary hue into CMYK without a real color-managed conversion PeachPDF doesn't have yet.
        /// </summary>
        internal static XColor RequireAllowedOrConvert(PdfDocument document, XColor color, string featureDescription)
        {
            if (document.Options.PdfXConformance != PdfXConformance.X1a) return color;
            if (color.ColorSpace == XColorSpace.Cmyk) return color;

            if (color.R == color.G && color.G == color.B)
            {
                var blackGeneration = document.Options.ColorOptions?.BlackGeneration ?? ColorBlackGeneration.UseTrueBlack;
                return ConvertAchromaticToCmyk(color, blackGeneration);
            }

            throw new PdfXConformanceException(
                $"{featureDescription} is a chromatic RGB color, which PDF/X-1a forbids (content must be " +
                "CMYK or a named spot color). Author it with device-cmyk() instead, or target " +
                "PdfXConformance.X3 or PdfXConformance.X4, both of which permit ICC-managed RGB.");
        }

        private static XColor ConvertAchromaticToCmyk(XColor color, ColorBlackGeneration blackGeneration)
        {
            // R == G == B (checked by the caller): the fraction of the way from white (0) to black (1).
            double darkness = 1.0 - color.R / 255.0;

            if (blackGeneration == ColorBlackGeneration.UseRichBlack)
            {
                // A commonly-cited rich-black recipe, scaled by darkness so lighter grays stay close to
                // neutral and only true black gets the full CMY+K ink coverage.
                return XColor.FromCmyk(color.A, 0.60 * darkness, 0.40 * darkness, 0.40 * darkness, darkness);
            }

            return XColor.FromCmyk(color.A, 0, 0, 0, darkness);
        }
    }
}
