#nullable enable

namespace PeachPDF.PdfSharpCore.Pdf.Advanced
{
    /// <summary>
    /// The choke point a CMYK image embed passes through on its way into the document (see
    /// <see cref="PdfImage.EmbedJpegPassthrough"/>). PeachPDF's PDF/A output intent is always the
    /// embedded sRGB profile (see <see cref="PdfOutputIntent"/>'s own remarks - PeachPDF only ever
    /// generates RGB content-stream colors), which a bare <c>/DeviceCMYK</c> image has no relationship
    /// to at all - unlike a bare <c>/DeviceRGB</c>/<c>/DeviceGray</c> image, which stays consistent with
    /// that output intent with or without its own ICC profile. An <c>/ICCBased</c> CMYK image is
    /// self-describing via its own embedded profile and doesn't have this problem, so PDF/A conformance
    /// requires a CMYK image to carry one rather than rejecting CMYK outright.
    /// </summary>
    internal static class PdfACmykImageGuard
    {
        /// <summary>
        /// Throws a <see cref="PdfAConformanceException"/> if <paramref name="document"/>'s
        /// <see cref="PdfAConformance"/> is anything other than <see cref="PdfAConformance.None"/> and
        /// <paramref name="hasIccProfile"/> is <see langword="false"/>.
        /// </summary>
        internal static void RequireIccProfile(PdfDocument document, bool hasIccProfile, string featureDescription)
        {
            if (hasIccProfile || document.Options.PdfAConformance is PdfAConformance.None)
            {
                return;
            }

            throw new PdfAConformanceException(
                $"{featureDescription} is not permitted under PDF/A conformance - a bare /DeviceCMYK " +
                "image has no relationship to PeachPDF's RGB-based PDF/A output intent. Provide a source " +
                "image with an embedded ICC profile, or remove PdfAConformance.");
        }
    }
}
