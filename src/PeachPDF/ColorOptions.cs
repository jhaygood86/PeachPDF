#nullable enable

namespace PeachPDF
{
    /// <summary>
    /// Print color-management options, set via <see cref="PdfGenerateConfig.ColorOptions"/>. Mirrors the
    /// <see cref="PdfGenerateConfig.Metadata"/>/<see cref="PdfDocumentMetadata"/> nested-options pattern.
    /// </summary>
    /// <remarks>
    /// Real, colorimetric ICC device-to-device conversion (<see cref="ConversionMode"/> beyond
    /// <see cref="ColorConversionMode.PreserveAsAuthored"/>, <see cref="RenderingIntent"/>,
    /// <see cref="UseBlackPointCompensation"/>) via PeachImage's <c>IccColorProfile.ConvertTo</c> - no
    /// naive RGB&lt;-&gt;CMYK formula is used anywhere. A color's source profile is always well-defined: an
    /// RGB-authored color's source is the ICC-published sRGB profile PeachPDF already bundles for PDF/A
    /// (CSS colors are sRGB by definition outside of <c>device-cmyk()</c>); a <c>device-cmyk()</c>-authored
    /// color is uncalibrated ink by definition (CSS Color 5 §6) and has no source profile unless
    /// <see cref="FallbackCmykProfile"/> supplies one, so without that set, a <c>device-cmyk()</c> color is
    /// left exactly as authored even under a conversion mode - there is nothing to convert *from*.
    /// </remarks>
    public sealed class ColorOptions
    {
        /// <summary>
        /// The ICC profile bytes to embed as the document's <c>/OutputIntents</c> destination profile, and
        /// (when <see cref="ConversionMode"/> is <see cref="ColorConversionMode.ConvertToOutputIntent"/>)
        /// the conversion target every color is converted into. Required when
        /// <see cref="PdfGenerateConfig.PdfXConformance"/> is anything but <see cref="PeachPDF.PdfXConformance.None"/>
        /// - PeachPDF bundles no default press profile, unlike the sRGB profile it bundles for
        /// <see cref="PdfAConformance"/>, since there is no single correct default for a print output
        /// intent. Under <see cref="PeachPDF.PdfXConformance.X1a"/> this profile must itself be a CMYK
        /// profile (validated at generation time); <see cref="PeachPDF.PdfXConformance.X3"/>/
        /// <see cref="PeachPDF.PdfXConformance.X4"/> accept a CMYK, RGB, or Gray profile.
        /// </summary>
        public byte[]? OutputIntentProfile { get; set; }

        /// <summary>
        /// A human-readable name for <see cref="OutputIntentProfile"/>'s intended output condition (e.g.
        /// <c>"Coated FOGRA39"</c>) - written as the output intent's <c>/OutputConditionIdentifier</c>/
        /// <c>/Info</c>. Required alongside <see cref="OutputIntentProfile"/> whenever
        /// <see cref="PdfGenerateConfig.PdfXConformance"/> is not <see cref="PeachPDF.PdfXConformance.None"/>.
        /// </summary>
        public string? OutputIntentIdentifier { get; set; }

        /// <summary>
        /// How an achromatic (gray, including pure black) RGB-authored color is represented when it must
        /// become CMYK - under <see cref="PeachPDF.PdfXConformance.X1a"/> today, since that level rejects
        /// chromatic RGB outright but would otherwise reject every un-set default text/border color too
        /// (the UA stylesheet's colors are RGB). Defaults to <see cref="ColorBlackGeneration.UseTrueBlack"/>.
        /// An achromatic value has an exact, lossless ink mapping either way (unlike a chromatic color,
        /// which needs a real color-managed conversion - see <see cref="ConversionMode"/>) - this specific
        /// mapping is not an approximation, and is independent of <see cref="ConversionMode"/>/
        /// <see cref="RenderingIntent"/> (it never goes through PeachImage's ICC engine).
        /// </summary>
        public ColorBlackGeneration BlackGeneration { get; set; } = ColorBlackGeneration.UseTrueBlack;

        /// <summary>
        /// A CMYK ICC profile giving a <c>device-cmyk()</c>-authored color a defined source profile to
        /// convert *from* under a non-<see cref="ColorConversionMode.PreserveAsAuthored"/>
        /// <see cref="ConversionMode"/> (see this class's remarks - without this set, such a color is left
        /// unconverted). Also the required conversion target for <see cref="ColorConversionMode.GrayscaleViaK"/>
        /// - every color (RGB via the bundled sRGB source, or CMYK via this profile) is converted into it
        /// and only the resulting K channel is kept.
        /// </summary>
        public byte[]? FallbackCmykProfile { get; set; }

        /// <summary>
        /// How document colors should be converted for output. Defaults to
        /// <see cref="ColorConversionMode.PreserveAsAuthored"/> (no conversion). Every mode is real,
        /// colorimetric ICC conversion via PeachImage's <c>IccColorProfile.ConvertTo</c> - see this class's
        /// remarks for which colors have a defined source profile to convert from.
        /// </summary>
        public ColorConversionMode ConversionMode { get; set; } = ColorConversionMode.PreserveAsAuthored;

        /// <summary>
        /// An arbitrary ICC profile to convert every (RGB, or CMYK when <see cref="FallbackCmykProfile"/>
        /// gives it a source) color into, when <see cref="ConversionMode"/> is
        /// <see cref="ColorConversionMode.ConvertToProfile"/>.
        /// </summary>
        public byte[]? ConvertToProfile { get; set; }

        /// <summary>
        /// The ICC rendering intent to use for a color-managed conversion (<see cref="ConversionMode"/>
        /// other than <see cref="ColorConversionMode.PreserveAsAuthored"/>). Defaults to
        /// <see cref="ColorRenderingIntent.RelativeColorimetric"/> (the common print default).
        /// </summary>
        public ColorRenderingIntent RenderingIntent { get; set; } = ColorRenderingIntent.RelativeColorimetric;

        /// <summary>
        /// Whether a color-managed conversion applies black point compensation (ICC.1:2010 Annex A) -
        /// scaling the source profile's black point to the destination profile's, reducing shadow
        /// clipping/crushing when the two differ. Meaningful for <see cref="ColorRenderingIntent.Perceptual"/>/
        /// <see cref="ColorRenderingIntent.RelativeColorimetric"/>/<see cref="ColorRenderingIntent.Saturation"/>
        /// but not <see cref="ColorRenderingIntent.AbsoluteColorimetric"/>, which by definition preserves
        /// the actual white/black points instead. Defaults to <see langword="true"/>. Note that
        /// <see cref="ColorRenderingIntent.Perceptual"/> already applies its own built-in black-point
        /// handling independent of this flag - the two compound, so if shadows look over-compressed with
        /// both in play under Perceptual, try <see cref="ColorRenderingIntent.RelativeColorimetric"/> instead.
        /// </summary>
        public bool UseBlackPointCompensation { get; set; } = true;
    }

    /// <summary>
    /// How to represent an achromatic (gray/black) RGB-authored color as CMYK - see
    /// <see cref="ColorOptions.BlackGeneration"/>.
    /// </summary>
    public enum ColorBlackGeneration
    {
        /// <summary>K-only: C=M=Y=0, K scaled to the source gray's darkness. Default.</summary>
        UseTrueBlack = 0,

        /// <summary>
        /// K-only plus proportionally-scaled C/M/Y for ink density on press (common for large solid-black
        /// areas, at the cost of registration sensitivity) - only the darkest values get meaningful CMY;
        /// lighter grays stay close to neutral.
        /// </summary>
        UseRichBlack,
    }

    /// <summary>
    /// How document colors should be converted for output - see <see cref="ColorOptions.ConversionMode"/>.
    /// Every non-<see cref="PreserveAsAuthored"/> mode requires the relevant destination ICC profile
    /// property to be set (and parseable) - <see cref="PdfGenerator"/> validates this at generation time
    /// rather than discovering it deep in painting.
    /// </summary>
    public enum ColorConversionMode
    {
        /// <summary>
        /// Every color is written in whichever space it was authored/resolved in - RGB stays RGB,
        /// <c>device-cmyk()</c> stays CMYK (PDF's mixed-color-space document support). No conversion.
        /// Default.
        /// </summary>
        PreserveAsAuthored = 0,

        /// <summary>Convert every color into <see cref="ColorOptions.OutputIntentProfile"/>'s space.</summary>
        ConvertToOutputIntent,

        /// <summary>
        /// Convert every color into <see cref="ColorOptions.FallbackCmykProfile"/>'s (required) CMYK space
        /// and keep only the resulting K (black) channel - true ink-based grayscale, not a luminosity
        /// approximation.
        /// </summary>
        GrayscaleViaK,

        /// <summary>Convert every color into <see cref="ColorOptions.ConvertToProfile"/>'s space.</summary>
        ConvertToProfile,
    }

    /// <summary>
    /// An ICC rendering intent (ICC.1:2010 §7.2.15) - see <see cref="ColorOptions.RenderingIntent"/>.
    /// </summary>
    public enum ColorRenderingIntent
    {
        /// <summary>Compresses the whole source gamut into the destination's, preserving perceived relationships.</summary>
        Perceptual = 0,

        /// <summary>Maps in-gamut colors exactly and clips out-of-gamut colors to the destination gamut's boundary.</summary>
        RelativeColorimetric,

        /// <summary>Preserves saturation over exact color accuracy - suited to charts/diagrams rather than photographs.</summary>
        Saturation,

        /// <summary>Like <see cref="RelativeColorimetric"/>, but also preserves the destination medium's actual white point rather than rescaling to it.</summary>
        AbsoluteColorimetric,
    }
}
