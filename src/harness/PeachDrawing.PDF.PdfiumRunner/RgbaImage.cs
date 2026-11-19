using PeachImage;

namespace PeachDrawing.PDF.PdfiumRunner;

/// <summary>A decoded, opaque bitmap, four bytes (R, G, B, A) per pixel, top row first.</summary>
internal sealed record RgbaImage(int Width, int Height, byte[] Pixels)
{
    /// <summary>Decodes a PNG, compositing any transparency over white (how PDFium's test suite fills a page before drawing it).</summary>
    public static RgbaImage? FromPng(byte[] png)
    {
        if (!Image.TryLoad(new MemoryStream(png), out var image, new DecoderOptions { TargetPixelFormat = PixelFormat.Rgba32 }) || image is null)
            return null;

        using (image)
        {
            var pixels = image.PixelMemory.Span.ToArray();
            for (var i = 0; i < pixels.Length; i += 4)
            {
                var a = pixels[i + 3];
                if (a == 255)
                    continue;
                for (var c = 0; c < 3; c++)
                    pixels[i + c] = (byte)((pixels[i + c] * a + 255 * (255 - a) + 127) / 255);
                pixels[i + 3] = 255;
            }

            return new RgbaImage(image.Width, image.Height, pixels);
        }
    }

    /// <summary>True when every pixel is white: a renderer that drew nothing, or a page that has nothing to draw.</summary>
    public bool IsBlank()
    {
        var p = Pixels;
        for (var i = 0; i < p.Length; i += 4)
        {
            if (p[i] != 255 || p[i + 1] != 255 || p[i + 2] != 255)
                return false;
        }

        return true;
    }

    /// <summary>
    /// How far this image is from <paramref name="other"/>: the pixels whose largest R, G or B difference exceeds <paramref name="threshold"/>, and
    /// the mean of the per-pixel differences. Null when the sizes differ.
    /// </summary>
    public (int Differing, double MeanDifference)? Compare(RgbaImage other, int threshold)
    {
        if (Width != other.Width || Height != other.Height)
            return null;

        int count = 0;
        long sum = 0;
        var a = Pixels;
        var b = other.Pixels;
        for (var i = 0; i < a.Length; i += 4)
        {
            var d = Math.Max(Math.Abs(a[i] - b[i]), Math.Max(Math.Abs(a[i + 1] - b[i + 1]), Math.Abs(a[i + 2] - b[i + 2])));
            sum += d;
            if (d > threshold)
                count++;
        }

        return (count, a.Length == 0 ? 0 : sum / (double)(a.Length / 4));
    }

    /// <summary>The image as a PNG, for the --dump option.</summary>
    public byte[] ToPng()
    {
        using var image = Image.Create(Width, Height, PixelFormat.Rgba32);
        Pixels.CopyTo(image.GetPixelSpan());
        using var stream = new MemoryStream();
        image.Save(stream, "png", null!);
        return stream.ToArray();
    }
}
