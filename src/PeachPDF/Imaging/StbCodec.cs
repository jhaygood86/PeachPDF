using System.IO;
using StbImageSharp;
using WriteComponents = StbImageWriteSharp.ColorComponents;
using WriteImageWriter = StbImageWriteSharp.ImageWriter;

namespace PeachPDF.Imaging
{
    /// <summary>
    /// The universal fallback codec - StbImageSharp/StbImageWriteSharp, available on every OS regardless
    /// of native codec support. Shared by <c>PeachPDF.PdfSharpCore.Utils.NativeOrStbImageSource</c> (the
    /// fallback path) and <see cref="PeachPDF.PdfSharpCore.Utils.StbImageSharpImageSource"/> (kept
    /// independently testable and unchanged, including its own PNG-sniff-based Transparent convention).
    /// </summary>
    internal static class StbCodec
    {
        public static DecodedImage Decode(byte[] bytes)
        {
            var image = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);
            return DecodedImage.FromRgba(image.Data, image.Width, image.Height);
        }

        public static byte[] EncodeJpeg(in DecodedImage image, int quality)
        {
            using var ms = new MemoryStream();
            new WriteImageWriter().WriteJpg(image.Rgba, image.Width, image.Height, WriteComponents.RedGreenBlueAlpha, ms, quality);
            return ms.ToArray();
        }

        public static byte[] EncodeBmp(in DecodedImage image)
        {
            using var ms = new MemoryStream();
            new WriteImageWriter().WriteBmp(image.Rgba, image.Width, image.Height, WriteComponents.RedGreenBlueAlpha, ms);
            return ms.ToArray();
        }
    }
}
