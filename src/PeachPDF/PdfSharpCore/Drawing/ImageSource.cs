
using System;
using System.IO;


namespace MigraDocCore.DocumentObjectModel.MigraDoc.DocumentObjectModel.Shapes
{


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
            /// Encodes as an uncompressed PDF-embeddable bitmap. When <paramref name="targetWidth"/>/
            /// <paramref name="targetHeight"/> are given and differ from <see cref="Width"/>/
            /// <see cref="Height"/>, the image is resized to that pixel size first.
            /// </summary>
            void SaveAsPdfBitmap(MemoryStream ms, int? targetWidth = null, int? targetHeight = null);
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