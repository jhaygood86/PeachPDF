using PeachDrawing.Core;

namespace PeachDrawing.Svg;

/// <summary>A <see cref="FontFamily"/> that is nothing but its own name.</summary>
internal sealed class SvgCanvasFontFamily(string name) : FontFamily
{
    public override string Name => name;
}
