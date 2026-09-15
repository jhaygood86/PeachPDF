
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