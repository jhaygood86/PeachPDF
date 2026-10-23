using PeachDrawing.Core;

namespace PeachDrawing.Svg;

/// <summary>
/// The <see cref="PeachDrawing.Core.Image"/> of a tile made by <see cref="SvgCanvas.CreateTile"/>: not pixels but the vector content
/// drawn into the tile, written once into the document's definitions and referenced wherever the tile is drawn or used as a brush.
/// </summary>
internal sealed class SvgTileImage(double width, double height, SvgCanvas canvas) : PeachDrawing.Core.Image
{
    public override double Width { get; } = width;

    public override double Height { get; } = height;

    public override bool Interpolate { get; set; } = true;

    /// <summary>The canvas the tile's content was drawn on.</summary>
    public SvgCanvas Canvas { get; } = canvas;

    public override void Dispose()
    {
    }
}
