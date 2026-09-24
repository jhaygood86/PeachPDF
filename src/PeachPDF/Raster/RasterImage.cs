using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;

namespace PeachPDF.Raster;

/// <summary>
/// An <see cref="RImage"/> backed by a <see cref="RasterSurface"/>: what <c>RasterGraphics.CreateTile</c> hands
/// back. Its <see cref="Width"/>/<see cref="Height"/> are the tile's layout size (the same convention the PDF
/// backend's Form XObject tiles use), not its pixel count.
/// </summary>
internal sealed class RasterImage : RImage
{
    private Bitmap? _bitmap;

    public RasterImage(RasterSurface surface, double layoutWidth, double layoutHeight)
    {
        Surface = surface;
        Width = layoutWidth;
        Height = layoutHeight;
    }

    public RasterSurface Surface { get; }

    public override double Width { get; }

    public override double Height { get; }

    public override bool Interpolate { get; set; } = true;

    /// <summary>A sampling view of the surface's current pixels (built on first use, so paint into the tile first).</summary>
    public Bitmap GetBitmap() => _bitmap ??= new Bitmap(Surface.Width, Surface.Height, Surface.Buffer);

    public override void Dispose()
    {
        Surface.Dispose();
    }
}
