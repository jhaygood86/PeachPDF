using PeachImage;
using PeachImage.Formats.Png;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PeachDrawing;

/// <summary>
/// Encodes a finished <see cref="RasterSurface"/>'s pixels into bytes a PDF (or any other) embedder can
/// wrap - the "pixels in, encoded bytes out" half of what used to be <c>RasterEmbedder</c>. Deliberately
/// has no PdfSharpCore dependency: this is the half of raster-surface embedding that belongs with the
/// raster backend itself (see <c>PeachPDF.Adapters.RasterEmbedding</c> for the PDF-specific half - the
/// <c>XImage</c> wrapping - that consumes this).
/// </summary>
internal static class RasterSurfaceEncoding
{
    /// <summary>Whether every pixel of <paramref name="surface"/> is fully opaque, so it needs no alpha plane (and no soft mask) to embed.</summary>
    public static bool IsOpaque(RasterSurface surface)
    {
        var pixels = surface.Pixels;
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 255)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Encodes <paramref name="surface"/> as a lossless PNG (byte-for-byte IDAT the PDF writer's existing
    /// PNG pass-through can embed directly, with a split-out alpha plane when there is alpha). A fully
    /// opaque surface is encoded without an alpha channel.
    /// </summary>
    public static byte[] EncodePng(RasterSurface surface, bool opaque)
    {
        using var stream = new MemoryStream();
        Save(surface, stream, "png", new PngEncoderOptions());
        return stream.ToArray();
    }

    /// <summary>
    /// Encodes <paramref name="surface"/>'s pixels (unpremultiplied first, since every PeachImage format
    /// expects straight alpha) into <paramref name="stream"/> in any format PeachImage supports, for a
    /// standalone <see cref="RasterCanvas"/> canvas's own <c>Save</c>. A fully opaque surface is encoded
    /// without an alpha channel, the same as <see cref="EncodePng"/>.
    /// </summary>
    public static void Save(RasterSurface surface, Stream stream, string formatName, EncoderOptions options)
    {
        using var image = ToPeachImage(surface);
        image.Save(stream, formatName, options);
    }

    /// <inheritdoc cref="Save"/>
    public static async Task SaveAsync(RasterSurface surface, Stream stream, string formatName, EncoderOptions options, CancellationToken cancellationToken = default)
    {
        using var image = ToPeachImage(surface);
        await image.SaveAsync(stream, formatName, options, cancellationToken);
    }

    private static Image ToPeachImage(RasterSurface surface)
    {
        var opaque = IsOpaque(surface);
        var image = Image.Create(surface.Width, surface.Height, opaque ? PixelFormat.Rgb24 : PixelFormat.Rgba32);
        if (opaque)
            StripAlpha(surface.Pixels, image.GetPixelSpan());
        else
            Bitmap.Unpremultiply(surface.Pixels, image.GetPixelSpan());

        return image;
    }

    private static void StripAlpha(ReadOnlySpan<byte> rgba, Span<byte> rgb)
    {
        for (int i = 0, j = 0; i + 3 < rgba.Length; i += 4, j += 3)
        {
            rgb[j] = rgba[i];
            rgb[j + 1] = rgba[i + 1];
            rgb[j + 2] = rgba[i + 2];
        }
    }

    /// <summary>
    /// Converts <paramref name="surface"/>'s (necessarily opaque) RGB pixels to raw CMYK bytes by naive
    /// subtractive conversion, for the PDF/X-1a raw-DeviceCMYK embedding path (PDF/X-1a forbids RGB).
    /// </summary>
    public static byte[] ToCmykPixels(RasterSurface surface, bool richBlack)
    {
        var pixels = surface.Pixels;
        var cmyk = new byte[surface.Width * surface.Height * 4];
        for (int i = 0, j = 0; i + 3 < pixels.Length; i += 4, j += 4)
        {
            int r = pixels[i], g = pixels[i + 1], b = pixels[i + 2];
            var max = Math.Max(r, Math.Max(g, b));
            var k = 255 - max;

            // A neutral pixel follows the document's black generation, exactly as neutral vector colours do (PdfXColorSpaceGuard), so a
            // flattened region's black matches the black around it.
            if (richBlack && r == g && g == b)
            {
                cmyk[j] = (byte)Math.Round(0.60 * k);
                cmyk[j + 1] = (byte)Math.Round(0.40 * k);
                cmyk[j + 2] = (byte)Math.Round(0.40 * k);
                cmyk[j + 3] = (byte)k;
                continue;
            }

            if (max == 0)
            {
                cmyk[j + 3] = 255;
                continue;
            }

            cmyk[j] = (byte)((max - r) * 255 / max);
            cmyk[j + 1] = (byte)((max - g) * 255 / max);
            cmyk[j + 2] = (byte)((max - b) * 255 / max);
            cmyk[j + 3] = (byte)k;
        }

        return cmyk;
    }
}
