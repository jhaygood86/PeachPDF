using PeachDrawing;
using PeachDrawing.Core;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Runtime.CompilerServices;

namespace PeachPDF.Adapters
{
    /// <summary>
    /// Implemented by an image source that can hand its decoded pixels to <see cref="ImageAdapter.GetPixels"/>.
    /// Optional on purpose: <c>ImageSource.IImageSource</c> is the PDF embedder's contract and has no need
    /// for pixel access.
    /// </summary>
    internal interface IRgbaPixelProvider
    {
        /// <summary>
        /// Decodes to tightly packed, straight-alpha RGBA8 (<c>width * height * 4</c> bytes). Returns false when
        /// the source has no RGB rendition (a CMYK image).
        /// </summary>
        bool TryGetRgba(out int width, out int height, out byte[] rgba);
    }

    /// <summary>
    /// Decodes an <see cref="XImage"/> to a premultiplied-RGBA8 <see cref="PixelBuffer"/> once and remembers
    /// the result for the image's lifetime - the cache <see cref="ImageAdapter.GetPixels"/> reads through.
    /// This is PDF-embedding-specific (keyed on <see cref="XImage"/>) and deliberately lives here rather than
    /// in <c>PeachDrawing</c>: the raster backend's own pixel math (<see cref="PixelMath"/>'s premultiply
    /// helpers) has no <see cref="XImage"/> concept at all, so the decode-and-cache step belongs on the PDF
    /// adapter side of that boundary, not the raster side of it.
    /// </summary>
    internal static class ImageDecodeCache
    {
        private static readonly ConditionalWeakTable<XImage, PixelBufferBox> Cache = [];

        public static PixelBuffer? Get(XImage image)
        {
            if (Cache.TryGetValue(image, out var cached))
                return cached.Value;

            PixelBuffer? buffer = null;
            if (image.TryGetRgba(out var w, out var h, out var rgba) && w > 0 && h > 0 && rgba.Length >= w * h * 4)
            {
                PixelMath.Premultiply(rgba.AsSpan(0, w * h * 4));
                buffer = new PixelBuffer(w, h, rgba);
            }

            Cache.AddOrUpdate(image, new PixelBufferBox(buffer));
            return buffer;
        }

        // ConditionalWeakTable's value type must be a reference type, and PixelBuffer is a struct.
        private sealed class PixelBufferBox(PixelBuffer? value)
        {
            public readonly PixelBuffer? Value = value;
        }
    }
}
