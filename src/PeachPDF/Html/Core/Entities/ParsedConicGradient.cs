using PeachDrawing.Core;

namespace PeachPDF.Html.Core.Entities
{
    internal sealed class ParsedConicGradient
    {
        public double FromAngleRad { get; init; } = 0.0;
        public double CenterX { get; init; } = 0.5;
        public double CenterY { get; init; } = 0.5;
        public required (PaintColor? PaintColor, double? PositionRad, bool IsHint)[] Stops { get; init; }
        public bool IsRepeating { get; init; }
        public GradientColorSpace ColorSpace { get; init; } = GradientColorSpace.Srgb;
        public HueInterpolationMethod HueMethod { get; init; } = HueInterpolationMethod.Shorter;
    }
}
