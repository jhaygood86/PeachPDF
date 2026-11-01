using PeachDrawing.Core;

namespace PeachDrawing.Skia;

/// <summary>A <see cref="FontFamily"/> that is nothing but its own name.</summary>
internal sealed class SkiaFontFamily(string name) : FontFamily
{
    public override string Name => name;
}
