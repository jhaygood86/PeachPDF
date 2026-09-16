#nullable enable

namespace PeachPDF
{
    /// <summary>
    /// How an opaque, unconditionally-lossless raster source (PNG, BMP, or GIF - none of the three has a
    /// lossy encoding mode) is embedded, set via <see cref="PdfGenerateConfig.ImageCompression"/>. WebP,
    /// AVIF, and TIFF sources are unaffected by this setting in every mode - PeachImage doesn't currently
    /// expose whether a decoded source of those formats was itself lossy- or lossless-encoded, so they
    /// stay on whatever path they were already on before this option existed. See
    /// <c>.claude/accepted-gaps/webp-avif-tiff-lossy-detection-unavailable.md</c>.
    /// </summary>
    public enum ImageCompression
    {
        /// <summary>
        /// Default. A PNG eligible for byte-for-byte <c>/FlateDecode</c> pass-through - not interlaced,
        /// and either fully opaque or carrying only a <c>tRNS</c> chroma-key transparency PDF's own
        /// color-key <c>/Mask</c> mechanism can represent (a real per-pixel alpha channel still needs the
        /// existing decode+<c>/SMask</c> path) - is always embedded that way, regardless of
        /// <see cref="PdfGenerateConfig.DownscaleImages"/> - pass-through can't be resized, the same as a
        /// CMYK JPEG is always embedded at natural size. A PNG/BMP/GIF source that isn't pass-through-
        /// eligible at its own natural size (an interlaced PNG, or a format with no pass-through mechanism
        /// at all) still never gets re-encoded as lossy JPEG - it falls back to the existing decode-and-
        /// <c>/FlateDecode</c> path instead. One being downscaled (its on-page display size is smaller
        /// than its natural size) keeps the existing downscale-to-JPEG-at-<see cref="PdfGenerateConfig.DownscaleQuality"/>
        /// behavior unchanged, since that's an intentional, separate size/quality trade-off - except a
        /// <c>tRNS</c>-transparent source, which stays pass-through-embedded (and so natural-size)
        /// regardless of downscaling too, since JPEG can't represent that transparency at all.
        /// </summary>
        Auto,

        /// <summary>
        /// Same as <see cref="Auto"/>, but a downscaled opaque PNG/BMP/GIF also never gets re-encoded as
        /// lossy JPEG - it's decoded, resampled, and re-<c>/FlateDecode</c>-encoded instead of
        /// JPEG-compressed at <see cref="PdfGenerateConfig.DownscaleQuality"/>. Larger downscaled output,
        /// always pixel-exact regardless of size. A <c>tRNS</c>-transparent PNG being downscaled is
        /// unaffected by this distinction either way - it already forfeits pass-through and falls into
        /// that same decode+resize path under both <see cref="Auto"/> and this value, since its real,
        /// decoded alpha survives the resize correctly regardless of which of these two modes triggered it.
        /// </summary>
        Lossless,

        /// <summary>
        /// Always re-encode an opaque PNG/BMP/GIF as JPEG, even one that would otherwise be pass-through-
        /// eligible - the behavior every version of PeachPDF used before this option existed. An explicit
        /// opt-in for anyone who wants the smallest files even for diagram/line-art content and accepts
        /// the fidelity loss. Doesn't apply to a <c>tRNS</c>-transparent PNG: JPEG has no way to represent
        /// that transparency at all, so forcing it through the lossy path would silently make the
        /// transparent color solid instead of merely losing some fidelity - the same source is still
        /// embedded via pass-through under this value too, exactly like a real per-pixel-alpha PNG is
        /// already unaffected by every value of this setting.
        /// </summary>
        Lossy,
    }
}
