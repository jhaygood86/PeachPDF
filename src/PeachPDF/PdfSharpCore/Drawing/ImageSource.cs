
using System;
using System.IO;


namespace MigraDocCore.DocumentObjectModel.MigraDoc.DocumentObjectModel.Shapes
{
    /// <summary>
    /// Which bare PDF device color space (and, when an ICC profile accompanies it, which
    /// <c>/ICCBased</c> <c>/N</c>/<c>/Alternate</c>) a <see cref="JpegPassthroughData"/> embed uses.
    /// </summary>
    internal enum JpegPassthroughColorSpace
    {
        Rgb,
        Gray,
        Cmyk,
    }

    /// <summary>
    /// Data needed to embed a JPEG source via byte-for-byte pass-through (PDF's <c>/DCTDecode</c>
    /// filter accepts a JPEG's own compressed bytes directly) instead of decoding and re-embedding it -
    /// see <see cref="ImageSource.IImageSource.JpegPassthrough"/> for when this applies.
    /// </summary>
    internal readonly struct JpegPassthroughData
    {
        /// <summary>The original JPEG file bytes, unchanged.</summary>
        public required byte[] Data { get; init; }

        /// <summary>Which bare Device* color space (or <c>/ICCBased</c> alternate/channel count) applies.</summary>
        public required JpegPassthroughColorSpace ColorSpace { get; init; }

        /// <summary>
        /// Whether a PDF <c>/Decode [1 0 1 0 1 0 1 0]</c> array is needed to undo Adobe's inverted-CMYK
        /// JPEG convention. Only ever <see langword="true"/> when <see cref="ColorSpace"/> is
        /// <see cref="JpegPassthroughColorSpace.Cmyk"/>.
        /// </summary>
        public bool NeedsInvertedDecode { get; init; }

        /// <summary>A usable embedded ICC profile's raw bytes, or <see langword="null"/> if none.</summary>
        public byte[]? IccProfile { get; init; }
    }

    /// <summary>
    /// Which bare Device*/Indexed color space a <see cref="PngPassthroughData"/> embed's pixel data is
    /// in - separate from whether the embed's actual PDF color space ends up <c>/ICCBased</c> instead
    /// (see <see cref="PngPassthroughData.IccProfile"/>, issue #1106): the source's own <c>iCCP</c>
    /// chunk, when PeachImage can parse it into a usable profile of the expected shape, rides straight
    /// through unchanged - no extra decode needed, since <c>PngPassthrough.TryRead</c> already inflates
    /// it as part of reading the pass-through data itself.
    /// </summary>
    internal enum PngPassthroughColorSpace
    {
        Gray,
        Rgb,
        Indexed,
    }

    /// <summary>
    /// Data needed to embed a PNG source via byte-for-byte pass-through: PDF's <c>/FlateDecode</c> filter
    /// (with a <c>/DecodeParms</c> <c>/Predictor</c>) accepts a PNG's own concatenated, CRC-validated
    /// <c>IDAT</c> bytes directly - a complete zlib stream, byte-for-byte as it appears in the file - with
    /// no inflate/deflate round trip. See <see cref="ImageSource.IImageSource.PngPassthrough"/> for when
    /// this applies.
    /// </summary>
    internal readonly struct PngPassthroughData
    {
        /// <summary>
        /// The concatenated, CRC-validated <c>IDAT</c> chunk payloads, unchanged - except when
        /// <see cref="AlphaIdatData"/> is set for a color type 4/6 source, where this is instead
        /// <see cref="PeachImage.Formats.Png.PngAlphaSplitInfo.ColorData"/>, the de-interleaved color-only
        /// plane (see that field's own remarks).
        /// </summary>
        public required byte[] IdatData { get; init; }

        /// <summary>Which PDF color space this pass-through embed uses.</summary>
        public required PngPassthroughColorSpace ColorSpace { get; init; }

        /// <summary>The bit depth of <see cref="IdatData"/>'s samples - written as both <c>/BitsPerComponent</c> and the
        /// <c>/DecodeParms</c> predictor's own bit depth. The PNG's own IHDR bit depth, except when
        /// <see cref="AlphaIdatData"/> is set for a color type 4/6 source, where it's
        /// <see cref="PeachImage.Formats.Png.PngAlphaSplitInfo.ColorBitDepth"/> instead (always 8, per that
        /// API's current v1 limitation).</summary>
        public required byte BitDepth { get; init; }

        /// <summary>
        /// The raw <c>PLTE</c> chunk bytes (tightly packed RGB triples), used to build a PDF
        /// <c>/Indexed</c> color space lookup table. Non-null if and only if <see cref="ColorSpace"/> is
        /// <see cref="PngPassthroughColorSpace.Indexed"/>.
        /// </summary>
        public byte[]? PaletteRgb { get; init; }

        /// <summary>
        /// A PDF color-key <c>/Mask</c> array (flat min/max component-value pairs), built from the
        /// source's <c>tRNS</c> chunk when it's expressible as one - Grayscale/Truecolor <c>tRNS</c> is
        /// always a single exact chroma-key value, and a Palette <c>tRNS</c> qualifies whenever every
        /// listed entry is exactly 0 or 255 <em>and</em> the transparent indices form a single contiguous
        /// run (PDF's colour-key mask for an Indexed colour space is exactly one <c>[min max]</c> range,
        /// per ISO 32000-1 §8.9.6.4 - see <c>PeachImageSource.TryBuildPaletteColorKeyMask</c>).
        /// Null when the source has no <c>tRNS</c>, or (Palette only) when it has one but nothing in it is
        /// actually transparent. Mutually exclusive with <see cref="AlphaIdatData"/> - a source reaches
        /// exactly one of the two, since a real per-pixel alpha channel can't be expressed as a binary
        /// color-key mask (see <see cref="ImageSource.IImageSource.PngPassthrough"/>'s own remarks).
        /// </summary>
        public int[]? ColorKeyMask { get; init; }

        /// <summary>
        /// A complete, independent zlib stream of single-channel (DeviceGray) alpha samples, PNG-row-filtered
        /// the same way <see cref="IdatData"/> itself is - <see cref="PeachImage.Formats.Png.PngAlphaSplitInfo.AlphaData"/>,
        /// for a source with a real per-pixel alpha channel (color type 4/6, or a palette source whose
        /// <c>tRNS</c> has a genuine partial-alpha entry) that <see cref="PeachImage.Formats.Png.PngAlphaSplit"/>
        /// was able to split. Embedded as a child <c>/SMask</c> image object alongside the main color
        /// stream. Null for every ordinary pass-through source (no real alpha, or alpha-split wasn't
        /// possible - e.g. interlaced or 16-bit color type 4/6, still falls back to the existing full
        /// decode+<c>/SMask</c> path in that case).
        /// </summary>
        public byte[]? AlphaIdatData { get; init; }

        /// <summary>The bit depth of <see cref="AlphaIdatData"/>'s samples (always 8 today). Meaningless when <see cref="AlphaIdatData"/> is null.</summary>
        public byte? AlphaBitDepth { get; init; }

        /// <summary>
        /// A usable embedded ICC profile's raw bytes (already inflated -
        /// <see cref="PeachImage.Formats.Png.PngPassthroughInfo.IccProfileData"/>, issue #1106), or
        /// <see langword="null"/> if the source has none. Written as an <c>/ICCBased</c> color space
        /// instead of a bare Device*/Indexed one - see <see cref="ImageSource.IImageSource.PngPassthrough"/>'s
        /// own remarks. Orthogonal to <see cref="AlphaIdatData"/>/<see cref="ColorKeyMask"/>: a source can
        /// carry an ICC profile independent of whether it also has alpha or a chroma-key mask.
        /// </summary>
        public byte[]? IccProfile { get; init; }
    }

    /// <summary>
    /// Data needed to embed a GIF source via <c>/LZWDecode</c> pass-through, avoiding a full LZW
    /// decompress-then-recompress round trip - but GIF's own LZW bitstream isn't literally byte-for-byte
    /// reusable as PDF's (different bit-packing order and code-width growth timing despite the same
    /// underlying code values), so the actual embed re-packs it - see
    /// <c>PeachPDF.PdfSharpCore.Pdf.Advanced.PdfImage.RepackGifLzwForPdf</c>'s own remarks for the full
    /// explanation. See <see cref="ImageSource.IImageSource.GifPassthrough"/> for when this applies.
    /// </summary>
    internal readonly struct GifPassthroughData
    {
        /// <summary>The frame's LZW-compressed image data, verbatim from the GIF file (not yet re-packed for PDF - see this struct's own remarks).</summary>
        public required byte[] LzwData { get; init; }

        /// <summary>The frame's effective palette (flat RGB triples), used to build a PDF <c>/Indexed</c> color space lookup table.</summary>
        public required byte[] Palette { get; init; }

        /// <summary>
        /// A PDF color-key <c>/Mask</c> array (a single <c>[index index]</c> pair), built from the frame's
        /// declared transparent color index - GIF transparency is always exactly one index, unlike PNG's
        /// palette <c>tRNS</c> which can mark several. Null when the frame declared no transparent index.
        /// </summary>
        public int[]? ColorKeyMask { get; init; }
    }

    /// <summary>
    /// Data needed to embed a CMYK source with no PDF-native byte-for-byte pass-through filter (TIFF -
    /// unlike JPEG's <c>/DCTDecode</c>, there is no <c>/TIFFDecode</c>) as a raw <c>/FlateDecode</c> CMYK
    /// stream instead - see <see cref="ImageSource.IImageSource.CmykRaster"/> for when this applies.
    /// Always CMYK (4 channels); there is no RGB/Gray equivalent of this struct because a non-CMYK raster
    /// source already has a normal, working embed path (<see cref="ImageSource.IImageSource.SaveAsPdfBitmap"/>).
    /// </summary>
    internal readonly struct CmykRasterData
    {
        /// <summary>
        /// The decoded pixel buffer: <c>Width * Height * 4</c> bytes, tightly interleaved C,M,Y,K per
        /// pixel, row-major, no padding - PeachImage's own <c>PixelFormat.Cmyk32</c> layout, copied
        /// (not a live view - the source <c>Image</c> may be disposed once this struct is built).
        /// </summary>
        public required byte[] Data { get; init; }

        /// <summary>A usable embedded ICC profile's raw bytes, or <see langword="null"/> if none.</summary>
        public byte[]? IccProfile { get; init; }
    }

    internal abstract class ImageSource
    {
        /// <summary>
        /// Gets or sets the image source implementation to use for reading images.
        /// </summary>
        /// <value>The image source impl.</value>
        public static ImageSource ImageSourceImpl { get; set; } = null!;

        internal interface IImageSource
        {
            int Width { get; }
            int Height { get; }
            string Name { get; }

            /// <summary>
            /// Encodes as JPEG. When <paramref name="targetWidth"/>/<paramref name="targetHeight"/> are
            /// given and differ from <see cref="Width"/>/<see cref="Height"/>, the image is resized to
            /// that pixel size first. <paramref name="qualityOverride"/> replaces the instance's own
            /// default quality when given (used for a downscaled embed's own quality setting).
            /// </summary>
            void SaveAsJpeg(MemoryStream ms, int? targetWidth = null, int? targetHeight = null, int? qualityOverride = null);
            bool Transparent { get; }

            /// <summary>
            /// True when this source's pixel data is single-channel grayscale (only ever meaningful for
            /// a JPEG source decoded via its own native pixel format instead of being forced to Rgba32 -
            /// see <c>PeachImageSource.DecodeRgbOrGrayJpeg</c>). <see cref="SaveAsJpeg"/> re-encodes a
            /// grayscale source as a genuine 1-component grayscale JPEG rather than 3-component YCbCr, so
            /// a caller writing the PDF <c>/ColorSpace</c> for that re-encoded stream needs to know which
            /// it got - unlike <see cref="IsCmyk"/>'s always-pass-through case, a resized RGB/Gray source
            /// still reaches this re-encode path (see <see cref="JpegPassthrough"/>'s own remarks on the
            /// resize fallback).
            /// </summary>
            bool IsGrayscale { get; }

            /// <summary>
            /// Encodes as an uncompressed PDF-embeddable bitmap. When <paramref name="targetWidth"/>/
            /// <paramref name="targetHeight"/> are given and differ from <see cref="Width"/>/
            /// <see cref="Height"/>, the image is resized to that pixel size first.
            /// </summary>
            void SaveAsPdfBitmap(MemoryStream ms, int? targetWidth = null, int? targetHeight = null);

            /// <summary>
            /// True if this source's native pixel data is CMYK (a CMYK/YCCK JPEG - see
            /// <c>PeachImageSource</c>'s routing). Drives <c>PdfImageTable</c>'s resize skip and
            /// <c>PdfACmykImageGuard</c>'s PDF/A gate - a CMYK source is never resized and, under PDF/A
            /// conformance, must carry an embedded ICC profile (see <see cref="JpegPassthrough"/>).
            /// </summary>
            bool IsCmyk { get; }

            /// <summary>
            /// Non-null when this source should be embedded via byte-for-byte JPEG pass-through instead
            /// of the normal lossy-re-encode (<see cref="SaveAsJpeg"/>) or bitmap (<see cref="SaveAsPdfBitmap"/>)
            /// paths: always non-null when <see cref="IsCmyk"/> and the source is a CMYK/YCCK JPEG, with
            /// or without an ICC profile, and non-null for an RGB/Gray JPEG only when it carries a
            /// usable embedded ICC profile - preserving that profile is the only reason to prefer
            /// pass-through over the existing RGB JPEG handling. Null for every non-JPEG source, and for
            /// an RGB/Gray JPEG with no usable ICC profile.
            /// </summary>
            JpegPassthroughData? JpegPassthrough { get; }

            /// <summary>
            /// Non-null when <see cref="IsCmyk"/> is true but <see cref="JpegPassthrough"/> is null - a
            /// CMYK source with no PDF-native byte-for-byte pass-through filter available (TIFF today,
            /// the only other PeachImage codec that decodes to CMYK). Mutually exclusive with
            /// <see cref="JpegPassthrough"/>: exactly one is non-null whenever <see cref="IsCmyk"/> is
            /// true, and both are null otherwise.
            /// </summary>
            CmykRasterData? CmykRaster { get; }

            /// <summary>
            /// Non-null when this PNG source is eligible for <c>/FlateDecode</c> pass-through instead of
            /// the normal lossy-re-encode (<see cref="SaveAsJpeg"/>) or bitmap (<see cref="SaveAsPdfBitmap"/>)
            /// paths: not interlaced (PDF's <c>/Predictor</c> has no Adam7 concept). A <c>tRNS</c> chunk
            /// doesn't disqualify a source by itself - Grayscale/Truecolor <c>tRNS</c> is always a single
            /// exact chroma-key value, and a Palette <c>tRNS</c> qualifies whenever every listed entry is
            /// exactly 0 or 255 and the transparent indices form a single contiguous run - both map onto
            /// <see cref="PngPassthroughData.ColorKeyMask"/>, PDF's own equivalent chroma-key mechanism.
            /// A source with a real per-pixel alpha channel (color type 4/6, or a palette source with a
            /// genuine partial-alpha <c>tRNS</c> entry) is also eligible when
            /// <c>PeachImage.Formats.Png.PngAlphaSplit</c> can split its alpha plane out - see
            /// <see cref="PngPassthroughData.AlphaIdatData"/> - embedded as byte-for-byte color data plus a
            /// child <c>/SMask</c>, rather than the full decode+<c>/SMask</c> path. Non-contiguous
            /// transparent palette indices, or an alpha-split PeachImage can't perform (interlaced, or
            /// 16-bit color type 4/6), still fall back to decode. A usable embedded <c>iCCP</c> profile
            /// (issue #1106) rides along on <see cref="PngPassthroughData.IccProfile"/> independent of
            /// eligibility for any of the above - a PNG can pass through and preserve its ICC profile
            /// regardless of which color-space/alpha/mask shape it takes. Null for every non-PNG source
            /// and for a PNG that isn't pass-through-eligible either way.
            /// </summary>
            PngPassthroughData? PngPassthrough { get; }

            /// <summary>
            /// Non-null when this GIF source is eligible for <c>/LZWDecode</c> pass-through instead of the
            /// normal full decode+<c>/FlateDecode</c> raw-RGB path: not interlaced (GIF's LZW compresses
            /// whatever row order it's given - an interlaced frame's raw bytes decompress into indices in
            /// 4-pass order, not top-to-bottom, and PDF has no de-interlace step), the frame's
            /// <c>MinCodeSize</c> is exactly 8 (GIF then starts codes at the same 9-bit width/Clear-code-256
            /// convention PDF's <c>/LZWDecode</c> always uses - the code *values* and widths line up, even
            /// though the bit-packing itself still needs re-packing, since GIF and PDF pack codes in
            /// opposite bit order - see <c>PdfImage.RepackGifLzwForPdf</c>), and the frame covers the full
            /// logical canvas (pass-through has no canvas-compositing step, unlike the existing full
            /// decode). A declared transparent color
            /// index maps onto <see cref="GifPassthroughData.ColorKeyMask"/>, PDF's color-key mask
            /// mechanism - GIF transparency is always exactly one index, so this never disqualifies a
            /// source the way a genuine partial-alpha PNG palette entry does. Null for every non-GIF
            /// source and for a GIF that isn't eligible.
            /// </summary>
            GifPassthroughData? GifPassthrough { get; }

            /// <summary>
            /// True when this source's pixel data is known to be lossless: always true for PNG, BMP, or
            /// GIF (neither has a lossy encoding mode at all), and true for WebP/AVIF/TIFF only when this
            /// specific decoded source actually used its format's lossless mode
            /// (<see cref="PeachImage.ImageInfo.IsLosslessEncoding"/>, PeachImage 0.4.6+ - issue #1107).
            /// Drives <see cref="PeachPDF.ImageCompression.Auto"/>/<see cref="PeachPDF.ImageCompression.Lossless"/>'s
            /// decision to never silently re-encode this source as lossy JPEG - see
            /// <c>PdfImage.InitializeJpeg</c>. True regardless of whether <see cref="PngPassthrough"/> is
            /// actually non-null (a PNG can be losslessly-formatted but not itself pass-through-eligible,
            /// e.g. interlaced).
            /// </summary>
            bool IsLosslessSourceFormat { get; }

            /// <summary>
            /// A usable embedded ICC profile's raw bytes for a WebP or AVIF source (issue #1106), or
            /// <see langword="null"/> if none/not applicable. Rides the raw-bitmap <c>ReadTrueColorMemoryBitmap</c>
            /// embed path (the only one these two formats ever reach - neither has a pass-through
            /// mechanism of its own the way PNG/JPEG do), replacing that path's hardcoded bare
            /// <c>/DeviceRGB</c> with an <c>/ICCBased</c> color space when present. Always null for every
            /// other format - PNG's own embedded profile rides <see cref="PngPassthroughData.IccProfile"/>
            /// instead, and JPEG/CMYK's ride <see cref="JpegPassthroughData.IccProfile"/>.
            /// </summary>
            byte[]? RgbIccProfile { get; }
        }

        /// <remarks>
        /// A decode failure (unrecognized/unsupported format, malformed bytes) must be surfaced as an
        /// <see cref="InvalidOperationException"/> - callers such as
        /// <c>PeachPDF.Html.Core.Handlers.ImageLoadHandler.LoadImageFromStream</c> and
        /// <c>PeachPDF.Svg.SvgTreeBuilder.DecodeRasterImage</c> catch exactly that type to treat a
        /// broken image as a non-fatal, unresolved replaced element rather than aborting the render.
        /// </remarks>
        protected abstract IImageSource FromFileImpl(string path, int? quality = 75);
        /// <inheritdoc cref="FromFileImpl"/>
        protected abstract IImageSource FromBinaryImpl(string name, Func<byte[]> imageSource, int? quality = 75);
        /// <inheritdoc cref="FromFileImpl"/>
        protected abstract IImageSource FromStreamImpl(string name, Func<Stream> imageStream, int? quality = 75);

        public static IImageSource FromFile(string path, int? quality = 75)
        {
            return ImageSourceImpl.FromFileImpl(path, quality);
        }

        public static IImageSource FromBinary(string name, Func<byte[]> imageSource, int? quality = 75)
        {
            return ImageSourceImpl.FromBinaryImpl(name, imageSource, quality);
        }

        public static IImageSource FromStream(string name, Func<Stream> imageStream, int? quality = 75)
        {
            return ImageSourceImpl.FromStreamImpl(name, imageStream, quality);
        }
    }
}