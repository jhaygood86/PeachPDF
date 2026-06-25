using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;

namespace PeachPDF.Html.Core.Entities;

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
    public required (RColor? Color, Length? Position, bool IsHint)[] Stops { get; init; }
    public bool IsRepeating { get; init; }
    public GradientColorSpace ColorSpace { get; init; } = GradientColorSpace.Srgb;
    public HueInterpolationMethod HueMethod { get; init; } = HueInterpolationMethod.Shorter;
}
