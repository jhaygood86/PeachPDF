using PeachDrawing.Core;

namespace PeachDrawing.Win2D;

/// <summary>A <see cref="FontFamily"/> that is nothing but its own name.</summary>
internal sealed class Win2dFontFamily(string name) : FontFamily
{
    public override string Name => name;
}
