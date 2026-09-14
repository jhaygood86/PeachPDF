using System;
using System.IO;

namespace PeachPDF
{
    /// <summary>
    /// A reusable, resolve-once image source for the declarative document-building API
    /// (<see cref="PdfGenerator.CreateDocument"/>) - pass the same instance to more than one
    /// <c>IContainer.Image(PdfImage)</c> call to place the same image in multiple places without
    /// re-reading the source bytes/file/stream for each placement.
    /// </summary>
    public sealed class PdfImage
    {
        internal string Source { get; }

        private PdfImage(string source)
        {
            Source = source;
        }

        /// <summary>Loads an image from a local file path.</summary>
        public static PdfImage FromFile(string path)
        {
            ArgumentNullException.ThrowIfNull(path);
            return new PdfImage(path);
        }

        /// <summary>Loads an image from raw bytes.</summary>
        public static PdfImage FromBytes(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            return new PdfImage(Layout.DataUri.FromBytes(data));
        }

        /// <summary>Loads an image read fully from a stream.</summary>
        public static PdfImage FromStream(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return FromBytes(ms.ToArray());
        }
    }
}
