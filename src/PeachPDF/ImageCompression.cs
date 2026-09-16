#nullable enable

namespace PeachPDF
{
    /// <summary>
    /// How an opaque, lossless raster source is embedded, set via
    /// <see cref="PdfGenerateConfig.ImageCompression"/>. PNG, BMP, and GIF always qualify (none of the
    /// three has a lossy encoding mode at all). WebP, AVIF, and TIFF each support both a lossy and a
    /// lossless encoding mode, so a source in one of those formats only qualifies when it was actually
    /// encoded losslessly - a lossy-encoded WebP/AVIF/TIFF is unaffected by this setting in every mode
    /// and stays on the same JPEG-re-encode path it always used.
    /// </summary>
    public enum ImageCompression
    {
        /// <summary>
        /// Default. A PNG eligible for byte-for-byte <c>/FlateDecode</c> pass-through - not interlaced,
        /// and either fully opaque or carrying only a <c>tRNS</c> chroma-key transparency PDF's own
        /// color-key <c>/Mask</c> mechanism can represent (a real per-pixel alpha channel still needs the
        /// existing decode+<c>/SMask</c> path) - is always embedded that way, regardless of
        /// <see cref="PdfGenerateConfig.DownscaleImages"/> - pass-through can't be resized, the same as a
        /// CMYK JPEG is always embedded at natural size. A lossless source (PNG/BMP/GIF always, or a
        /// losslessly-encoded WebP/AVIF/TIFF) that isn't pass-through-eligible at its own natural size (an
        /// interlaced PNG, or a format with no pass-through mechanism at all) still never gets re-encoded
        /// as lossy JPEG - it falls back to the existing decode-and-<c>/FlateDecode</c> path instead. One
        /// being downscaled (its on-page display size is smaller
        /// than its natural size) keeps the existing downscale-to-JPEG-at-<see cref="PdfGenerateConfig.DownscaleQuality"/>
        /// behavior unchanged, since that's an intentional, separate size/quality trade-off - except a
        /// <c>tRNS</c>-transparent source, which stays pass-through-embedded (and so natural-size)
        /// regardless of downscaling too, since JPEG can't represent that transparency at all.
        /// </summary>
        Auto,

        /// <summary>
        /// Same as <see cref="Auto"/>, but a downscaled opaque lossless source also never gets re-encoded as
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
