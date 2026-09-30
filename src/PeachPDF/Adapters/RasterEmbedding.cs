using PeachDrawing.Core;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Utils;
using PeachDrawing;
using System.IO;

namespace PeachPDF.Adapters
{
    /// <summary>
    /// Turns a finished <see cref="RasterSurface"/> into an <see cref="XImage"/> the PDF writer can embed -
    /// the PDF-specific half of what used to be <c>RasterEmbedder</c>. The pixels-to-bytes encoding itself
    /// (<see cref="RasterSurfaceEncoding"/>) has no PdfSharpCore dependency; this file is exactly the
    /// <see cref="XImage"/> wrapping around it, kept PeachPDF-side.
    /// </summary>
    internal static class RasterEmbedding
    {
        /// <summary>Whether every pixel of <paramref name="surface"/> is fully opaque, so it needs no alpha plane (and no soft mask) to embed.</summary>
        public static bool IsOpaque(RasterSurface surface) => RasterSurfaceEncoding.IsOpaque(surface);

        /// <summary>
        /// Encodes <paramref name="surface"/> as a lossless PNG and wraps it as an <see cref="XImage"/>, so the PDF
        /// writer's existing PNG pass-through (byte-for-byte IDAT, with a split-out alpha plane when there is alpha) embeds it. The image is
        /// marked <see cref="XImage.IsRasterOutput"/> so downscaling never resamples it: its pixel size was chosen to
        /// give a stated physical resolution. A fully opaque surface is encoded without an alpha channel, so it embeds with no soft mask;
        /// with <paramref name="asCmyk"/> it is embedded as DeviceCMYK instead (PDF/X-1a forbids RGB), by the usual naive conversion.
        /// </summary>
        public static XImage ToXImage(RasterSurface surface, bool asCmyk = false, bool richBlack = false)
        {
            var opaque = RasterSurfaceEncoding.IsOpaque(surface);
            if (asCmyk && opaque)
                return ToCmykImage(surface, richBlack);

            var png = RasterSurfaceEncoding.EncodePng(surface, opaque);
            var stream = new MemoryStream(png);

            var image = XImage.FromStream(() => stream);
            image.IsRasterOutput = true;
            return image;
        }

        private static XImage ToCmykImage(RasterSurface surface, bool richBlack)
        {
            var cmyk = RasterSurfaceEncoding.ToCmykPixels(surface, richBlack);
            var image = XImage.FromImageSource(PeachImageSource.CreateCmykRaster("raster", surface.Width, surface.Height, cmyk));
            image.IsRasterOutput = true;
            return image;
        }
    }
}
