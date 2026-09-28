using PeachDrawing.Abstractions;

namespace PeachDrawing;

/// <summary>A <see cref="FontFamily"/> that is nothing but its own name - what <see cref="RasterRenderContext"/> registers a resolved <see cref="PeachDrawing.Text.TypefaceFamily"/> under.</summary>
internal sealed class NamedFontFamily(string name) : FontFamily
{
    public override string Name => name;
}
