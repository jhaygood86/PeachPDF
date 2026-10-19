using System.Collections.Generic;
using PeachBrackets.Css;
using PeachDrawing.Core;

namespace PeachBrackets.Html.Entities;

internal enum RadialGradientSize
{
    FarthestCorner,
    ClosestCorner,
    FarthestSide,
    ClosestSide,
}

internal sealed class ParsedRadialGradient
{
    public bool IsCircle { get; init; }
    public double CenterX { get; init; } = 0.5;
    public double CenterY { get; init; } = 0.5;
    public RadialGradientSize Size { get; init; } = RadialGradientSize.FarthestCorner;
    public Length? ExplicitRadiusX { get; init; }
    public Length? ExplicitRadiusY { get; init; }
    public required (PaintColor? PaintColor, Length? Position, bool IsHint)[] Stops { get; init; }
    /// <summary>
    /// The indexes into <see cref="Stops"/> of the stops written <c>currentcolor</c>, which take the painting box's own color
    /// when the gradient is drawn; null when there are none.
    /// </summary>
    public IReadOnlyList<int>? CurrentColorStops { get; init; }
    public bool IsRepeating { get; init; }
    public GradientColorSpace ColorSpace { get; init; } = GradientColorSpace.Srgb;
    public HueInterpolationMethod HueMethod { get; init; } = HueInterpolationMethod.Shorter;
}
