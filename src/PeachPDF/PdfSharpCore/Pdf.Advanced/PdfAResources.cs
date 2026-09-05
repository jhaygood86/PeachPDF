#nullable enable

using System;
using System.IO;
using System.Linq;

namespace PeachPDF.PdfSharpCore.Pdf.Advanced
{
    /// <summary>
    /// Loads the embedded sRGB ICC profile PeachPDF ships for PDF/A output intents (see
    /// <see cref="PdfOutputIntent"/>) - the profile itself and its license are recorded under
    /// <c>PdfSharpCore/Resources/ColorProfiles/</c> and embedded via <c>PeachPDF.csproj</c>, following
    /// the same embedded-resource convention as <c>PdfSharpCore/Resources/Messages.restext</c> and the
    /// <c>Text/Resources/*.br</c> Unicode data tables.
    /// </summary>
    internal static class PdfAResources
    {
        private static byte[]? _sRgbIccProfile;

        /// <summary>
        /// The raw bytes of the embedded "sRGB2014.icc" profile (International Color Consortium,
        /// freely redistributable - see <c>sRGB2014.LICENSE.txt</c> alongside the embedded file).
        /// Loaded once and cached - the profile is immutable, shared read-only content.
        /// </summary>
        internal static byte[] SRgbIccProfile => _sRgbIccProfile ??= LoadSRgbIccProfile();

        private static byte[] LoadSRgbIccProfile()
        {
            var assembly = typeof(PdfAResources).Assembly;
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("sRGB2014.icc", StringComparison.OrdinalIgnoreCase));

            if (resourceName is null)
                throw new InvalidOperationException("The embedded sRGB2014.icc PDF/A color profile resource is missing from the PeachPDF assembly.");

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException("The embedded sRGB2014.icc PDF/A color profile resource could not be opened.");

            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            return memoryStream.ToArray();
        }
    }
}
