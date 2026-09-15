#nullable enable

using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Runtime.CompilerServices;

namespace PeachPDF.PdfSharpCore.Pdf.Advanced
{
    /// <summary>
    /// Applies <see cref="ColorOptions.ConversionMode"/> - the Phase B half of the CMYK/ICC epic
    /// (jhaygood86/PeachPDF#1090), gated on PeachImage 0.4.4's <c>IccColorProfile.ConvertTo</c> (the
    /// device-to-device primitive Phase A's <c>ColorOptions</c> shape was published ahead of - see that
    /// class's remarks). Called from <see cref="Drawing.Pdf.PdfGraphicsState.RealizeFillColor"/>/
    /// <see cref="Drawing.Pdf.PdfGraphicsState.RealizePen"/> right after
    /// <see cref="Pdf.Internal.ColorSpaceHelper"/>'s <c>EnsureColorMode</c> and before
    /// <see cref="PdfXColorSpaceGuard"/> - so a color is already in its final, converted space by the time
    /// PDF/X-1a's CMYK-only check runs (a <see cref="ColorConversionMode.ConvertToOutputIntent"/>/
    /// <see cref="ColorConversionMode.GrayscaleViaK"/> conversion into a CMYK profile means that check has
    /// nothing left to reject).
    /// </summary>
    internal static class PdfColorConversionGuard
    {
        // Keyed by the exact byte[] reference a caller's ColorOptions supplies (and by PdfAResources'
        // own cached sRGB array) - parsing an ICC profile isn't free, and a solid-color-heavy document
        // realizes the same few colors/profiles over and over. ConditionalWeakTable needs no manual
        // eviction: an entry disappears once nothing but the cache still references the byte[] key.
        private static readonly ConditionalWeakTable<byte[], PeachImage.IccColorProfile> ProfileCache = new();

        /// <summary>
        /// Returns <paramref name="color"/> converted per the document's <see cref="ColorOptions.ConversionMode"/>,
        /// or unchanged if <see cref="PdfDocumentOptions.ColorOptions"/> is unset, its
        /// <see cref="ColorOptions.ConversionMode"/> is <see cref="ColorConversionMode.PreserveAsAuthored"/>
        /// (the default), or <paramref name="color"/>'s own space has no defined source profile to convert
        /// from (see this method's remarks on <see cref="XColorSpace.Cmyk"/> handling).
        /// </summary>
        /// <remarks>
        /// An <see cref="XColorSpace.Rgb"/> color's source profile is always the ICC-published sRGB
        /// profile PeachPDF already bundles for PDF/A (<see cref="PdfAResources.SRgbIccProfile"/>) - CSS
        /// colors are sRGB by definition (CSS Color 4 §4.1) outside of <c>device-cmyk()</c>. An
        /// <see cref="XColorSpace.Cmyk"/> color (CSS <c>device-cmyk()</c>) is uncalibrated ink by
        /// definition (CSS Color 5 §6) - it has no defined source profile unless the caller supplies one
        /// via <see cref="ColorOptions.FallbackCmykProfile"/>, so without that set, a <c>device-cmyk()</c>
        /// color is left exactly as authored even under a non-<see cref="ColorConversionMode.PreserveAsAuthored"/>
        /// mode (there is nothing to convert *from*). An <see cref="XColorSpace.GrayScale"/> color is
        /// likewise left unchanged - no CSS construct produces one.
        /// </remarks>
        internal static XColor ApplyConversion(PdfDocument document, XColor color)
        {
            var options = document.Options.ColorOptions;
            if (options is null || options.ConversionMode == ColorConversionMode.PreserveAsAuthored)
                return color;

            var source = ResolveSourceProfile(color, options);
            if (source is null) return color;

            var destination = options.ConversionMode switch
            {
                ColorConversionMode.ConvertToOutputIntent => GetOrParseProfile(options.OutputIntentProfile!),
                ColorConversionMode.ConvertToProfile => GetOrParseProfile(options.ConvertToProfile!),
                ColorConversionMode.GrayscaleViaK => GetOrParseProfile(options.FallbackCmykProfile!),
                _ => null,
            };
            if (destination is null) return color;

            var converted = Convert(color, source, destination, options);
            return options.ConversionMode == ColorConversionMode.GrayscaleViaK ? ToKOnly(converted) : converted;
        }

        private static PeachImage.IccColorProfile? ResolveSourceProfile(XColor color, ColorOptions options)
        {
            return color.ColorSpace switch
            {
                XColorSpace.Rgb => GetOrParseProfile(PdfAResources.SRgbIccProfile),
                XColorSpace.Cmyk => options.FallbackCmykProfile is { Length: > 0 } fallback ? GetOrParseProfile(fallback) : null,
                _ => null,
            };
        }

        private static PeachImage.IccColorProfile? GetOrParseProfile(byte[] profileBytes)
        {
            if (ProfileCache.TryGetValue(profileBytes, out var cached)) return cached;

            if (!PeachImage.IccColorProfile.TryCreate(profileBytes, out var parsed) || parsed is null) return null;

            ProfileCache.AddOrUpdate(profileBytes, parsed);
            return parsed;
        }

        private static XColor Convert(XColor color, PeachImage.IccColorProfile source, PeachImage.IccColorProfile destination, ColorOptions options)
        {
            Span<byte> deviceValues = stackalloc byte[source.ChannelCount];
            WriteDeviceValues(color, source.DataColorSpace, deviceValues);

            Span<byte> destinationValues = stackalloc byte[destination.ChannelCount];
            var intent = MapIntent(options.RenderingIntent);
            source.ConvertTo(destination, deviceValues, destinationValues, 1, intent, options.UseBlackPointCompensation);

            return BuildColor(destination.DataColorSpace, destinationValues, color.A);
        }

        private static void WriteDeviceValues(XColor color, PeachImage.IccColorSpace space, Span<byte> destination)
        {
            switch (space)
            {
                case PeachImage.IccColorSpace.Rgb:
                    destination[0] = color.R;
                    destination[1] = color.G;
                    destination[2] = color.B;
                    break;
                case PeachImage.IccColorSpace.Cmyk:
                    destination[0] = ToByte(color.C);
                    destination[1] = ToByte(color.M);
                    destination[2] = ToByte(color.Y);
                    destination[3] = ToByte(color.K);
                    break;
                case PeachImage.IccColorSpace.Gray:
                    destination[0] = color.R; // R==G==B for any color this method is ever called with today.
                    break;
            }
        }

        private static XColor BuildColor(PeachImage.IccColorSpace space, ReadOnlySpan<byte> values, double alpha)
        {
            return space switch
            {
                PeachImage.IccColorSpace.Cmyk => XColor.FromCmyk(alpha, values[0] / 255.0, values[1] / 255.0, values[2] / 255.0, values[3] / 255.0),
                PeachImage.IccColorSpace.Gray => XColor.FromGrayScale(values[0] / 255.0),
                _ => XColor.FromArgb((int)Math.Round(alpha * 255), values[0], values[1], values[2]),
            };
        }

        private static XColor ToKOnly(XColor color)
        {
            // Already the result of a real ICC conversion into a CMYK profile (Convert always builds a
            // Cmyk-space XColor for a Cmyk destination.DataColorSpace, which GrayscaleViaK's caller
            // always requires - see ApplyConversion) - discard C/M/Y, keep only K.
            return color.ColorSpace == XColorSpace.Cmyk ? XColor.FromCmyk(color.A, 0, 0, 0, color.K) : color;
        }

        private static byte ToByte(double normalized) => (byte)Math.Round(Math.Clamp(normalized, 0.0, 1.0) * 255.0);

        private static PeachImage.IccRenderingIntent MapIntent(ColorRenderingIntent intent) => intent switch
        {
            ColorRenderingIntent.Perceptual => PeachImage.IccRenderingIntent.Perceptual,
            ColorRenderingIntent.Saturation => PeachImage.IccRenderingIntent.Saturation,
            ColorRenderingIntent.AbsoluteColorimetric => PeachImage.IccRenderingIntent.AbsoluteColorimetric,
            _ => PeachImage.IccRenderingIntent.RelativeColorimetric,
        };
    }
}
