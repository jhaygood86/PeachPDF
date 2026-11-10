using PeachDrawing.Core;
using PeachDrawing.PDF.Engine.Drawing;
using PeachDrawing;
using System.IO;

namespace PeachDrawing.PDF
{
    /// <summary>
    /// Turns a finished <see cref="RasterSurface"/> into an <see cref="PdfRasterImage"/> the PDF writer can embed -
    /// the PDF-specific half of what used to be <c>RasterEmbedder</c>. The pixels-to-bytes encoding itself
    /// (<see cref="RasterSurfaceEncoding"/>) has no PDF engine dependency; this file is exactly the
    /// <see cref="PdfRasterImage"/> wrapping around it, kept PeachPDF-side.
    /// </summary>
    internal static class RasterEmbedding
    {
        /// <summary>Whether every pixel of <paramref name="surface"/> is fully opaque, so it needs no alpha plane (and no soft mask) to embed.</summary>
        public static bool IsOpaque(RasterSurface surface) => RasterSurfaceEncoding.IsOpaque(surface);

        /// <summary>
        /// Encodes <paramref name="surface"/> as the streams of a lossless PNG pass-through embed (filtered, compressed RGB, and a separate alpha plane
        /// when there is alpha - see <see cref="RasterPlaneCodec"/>) and wraps them as an <see cref="PdfRasterImage"/>, so the PDF
        /// writer's existing PNG pass-through embeds it without a PNG file in between. The image is
        /// marked <see cref="PdfRasterImage.IsRasterOutput"/> so downscaling never resamples it: its pixel size was chosen to
        /// give a stated physical resolution. A fully opaque surface is encoded without an alpha channel, so it embeds with no soft mask;
        /// with <paramref name="asCmyk"/> it is embedded as DeviceCMYK instead (PDF/X-1a forbids RGB), by the usual naive conversion.
        /// </summary>
        public static PdfRasterImage ToXImage(RasterSurface surface, bool asCmyk = false, bool richBlack = false)
        {
            var opaque = RasterSurfaceEncoding.IsOpaque(surface);
            if (asCmyk && opaque)
                return ToCmykImage(surface, richBlack);

            // Two kinds of surface keep the PNG route because a PNG of them is a different kind of PDF image, which viewers do not draw the same way as
            // the plain RGB plus soft mask built below: hard-edged transparency (every pixel opaque or clear) is a color key, and an opaque surface of
            // 256 colors or fewer is a palette (/Indexed) image. A surface with no pixels has nothing to build.
            if (surface.Width <= 0 || surface.Height <= 0
                || (!opaque && RasterPlaneCodec.HasOnlyBinaryAlpha(surface.Pixels))
                || (opaque && RasterPlaneCodec.HasFewColors(surface.Pixels, 256)))
                return FromPng(surface, opaque);

            // Otherwise the streams of a PNG pass-through embed are made straight from the pixels: no PNG file to encode, read back, and split.
            var passthrough = RasterPlaneCodec.Encode(surface.Pixels, surface.Width, surface.Height, opaque);
            var source = new RasterPassthroughImageSource("*" + System.Guid.NewGuid().ToString("B"), surface.Width, surface.Height, passthrough);

            var image = PdfRasterImage.FromImageSource(source);
            image.IsRasterOutput = true;
            return image;
        }

        /// <summary>The PNG route: <paramref name="surface"/> encoded as a PNG file, which the image source then reads back (the form every raster embed used to take).</summary>
        private static PdfRasterImage FromPng(RasterSurface surface, bool opaque)
        {
            var png = RasterSurfaceEncoding.EncodePng(surface, opaque);
            var stream = new MemoryStream(png);

            var image = PdfRasterImage.FromStream(() => stream);
            image.IsRasterOutput = true;
            return image;
        }

        private static PdfRasterImage ToCmykImage(RasterSurface surface, bool richBlack)
        {
            var cmyk = RasterSurfaceEncoding.ToCmykPixels(surface, richBlack);
            var image = PdfRasterImage.FromImageSource(PeachImageSource.CreateCmykRaster("raster", surface.Width, surface.Height, cmyk));
            image.IsRasterOutput = true;
            return image;
        }
    }
}
