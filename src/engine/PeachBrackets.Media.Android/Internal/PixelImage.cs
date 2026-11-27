using PeachDrawing.Core;

namespace PeachBrackets.Media.Android.Internal;

/// <summary>A decoded picture held as pixels, which every <see cref="Canvas"/> can draw by uploading them once.</summary>
internal sealed class PixelImage(int width, int height, byte[] premultipliedRgba) : Image
{
    public override double Width => width;

    public override double Height => height;

    public override bool Interpolate { get; set; }

    public override void Dispose()
    {
    }

    public override PixelBuffer? GetPixels() => new PixelBuffer(width, height, premultipliedRgba);
}
