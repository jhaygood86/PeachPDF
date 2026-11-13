namespace PeachBrackets.WptRunner;

/// <summary>A decoded bitmap, four bytes (R, G, B, A) per pixel, top row first.</summary>
internal sealed record RgbaImage(int Width, int Height, byte[] Pixels)
{
    /// <summary>True when the image is one flat colour of nothing: fully transparent or opaque white throughout (a renderer that drew nothing).</summary>
    public bool IsBlank()
    {
        var p = Pixels;
        for (var i = 0; i < p.Length; i += 4)
        {
            var transparent = p[i + 3] == 0;
            var white = p[i] == 255 && p[i + 1] == 255 && p[i + 2] == 255 && p[i + 3] == 255;
            if (!transparent && !white)
                return false;
        }

        return true;
    }

    /// <summary>The largest per-channel (R, G, B) difference and the number of pixels differing in any of those channels; null when the sizes differ.</summary>
    public (int MaxDifference, int DifferingPixels)? Compare(RgbaImage other)
    {
        if (Width != other.Width || Height != other.Height)
            return null;

        int max = 0, count = 0;
        var a = Pixels;
        var b = other.Pixels;
        for (var i = 0; i < a.Length; i += 4)
        {
            var d = Math.Max(Math.Abs(a[i] - b[i]), Math.Max(Math.Abs(a[i + 1] - b[i + 1]), Math.Abs(a[i + 2] - b[i + 2])));
            if (d == 0)
                continue;
            count++;
            if (d > max)
                max = d;
        }

        return (max, count);
    }
}
