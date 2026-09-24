using PeachImage;
using PeachImage.Formats.Png;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Utils;
using System;
using System.IO;

namespace PeachPDF.Raster;

/// <summary>Turns a finished <see cref="RasterSurface"/> into an image the PDF writer can embed.</summary>
internal static class RasterEmbedder
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
    /// Encodes <paramref name="surface"/> as a lossless PNG and wraps it as an <see cref="XImage"/>, so the PDF
    /// writer's existing PNG pass-through (byte-for-byte IDAT, with a split-out alpha plane when there is alpha) embeds it. The image is
    /// marked <see cref="XImage.IsRasterOutput"/> so downscaling never resamples it: its pixel size was chosen to
    /// give a stated physical resolution. A fully opaque surface is encoded without an alpha channel, so it embeds with no soft mask;
    /// with <paramref name="asCmyk"/> it is embedded as DeviceCMYK instead (PDF/X-1a forbids RGB), by the usual naive conversion.
    /// </summary>
    public static XImage ToXImage(RasterSurface surface, bool asCmyk = false, bool richBlack = false)
    {
        if (asCmyk && IsOpaque(surface))
            return ToCmykImage(surface, richBlack);

        var opaque = IsOpaque(surface);
        using var png = Image.Create(surface.Width, surface.Height, opaque ? PixelFormat.Rgb24 : PixelFormat.Rgba32);
        if (opaque)
            StripAlpha(surface.Pixels, png.GetPixelSpan());
        else
            Bitmap.Unpremultiply(surface.Pixels, png.GetPixelSpan());

        using var stream = new MemoryStream();
        png.Save(stream, "png", new PngEncoderOptions());
        stream.Position = 0;

        var image = XImage.FromStream(() => stream);
        image.IsRasterOutput = true;
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

    private static XImage ToCmykImage(RasterSurface surface, bool richBlack)
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

        var image = XImage.FromImageSource(PeachImageSource.CreateCmykRaster("raster", surface.Width, surface.Height, cmyk));
        image.IsRasterOutput = true;
        return image;
    }
}
