
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
            void SaveAsJpeg(MemoryStream ms);
            bool Transparent { get; }
            void SaveAsPdfBitmap(MemoryStream ms);
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

        // PNG magic bytes: 89 50 4E 47. Shared by both TFM-exclusive ImageSource implementations
        // (StbImageSharpImageSource on net8.0, PeachImageSource on net10.0) so the PNG-sniff-as-
        // "Transparent" heuristic can't drift between them.
        protected static bool IsPng(byte[] bytes) =>
            bytes.Length >= 4 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;

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