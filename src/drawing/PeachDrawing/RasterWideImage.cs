using PeachDrawing.Core;
using System;

namespace PeachDrawing;

/// <summary>An <see cref="Image"/> of floating-point pixels, which a surface that keeps floats draws as they are and any other draws as the eight-bit view of the same pixels.</summary>
internal sealed class RasterWideImage : Image
{
    private RasterWideImage(Bitmap bitmap)
    {
        Bitmap = bitmap;
    }

    public Bitmap Bitmap { get; }

    public override double Width => Bitmap.Width;

    public override double Height => Bitmap.Height;

    public override bool Interpolate { get; set; } = true;

    /// <summary>Makes an image of straight RGBA floats (four per pixel).</summary>
    public static RasterWideImage FromStraight(int width, int height, ReadOnlySpan<float> straight)
    {
        var wide = new float[width * height * 4];
        var bytes = new byte[width * height * 4];
        for (var i = 0; i + 3 < wide.Length; i += 4)
        {
            var a = Math.Clamp(float.IsNaN(straight[i + 3]) ? 0f : straight[i + 3], 0f, 1f);
            for (var c = 0; c < 3; c++)
            {
                var v = (float.IsNaN(straight[i + c]) ? 0f : straight[i + c]) * a;
                wide[i + c] = v;
                bytes[i + c] = (byte)Math.Round(Math.Clamp(Math.Min(v, a), 0f, 1f) * 255f);
            }

            wide[i + 3] = a;
            bytes[i + 3] = (byte)Math.Round(a * 255f);
        }

        return new RasterWideImage(new Bitmap(width, height, bytes) { Wide = wide });
    }

    public override PixelBuffer? GetPixels() => new(Bitmap.Width, Bitmap.Height, Bitmap.Pixels);

    public override void Dispose()
    {
    }
}
