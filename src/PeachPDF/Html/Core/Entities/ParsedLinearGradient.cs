using PeachPDF.CSS;
using PeachDrawing.Core;

namespace PeachPDF.Html.Core.Entities
{
    internal sealed class ParsedLinearGradient
    {
        public double AngleRad { get; init; }
        public required (PaintColor? PaintColor, Length? Position, bool IsHint)[] Stops { get; init; }
        public bool IsRepeating { get; init; }
        public GradientColorSpace ColorSpace { get; init; } = GradientColorSpace.Srgb;
        public HueInterpolationMethod HueMethod { get; init; } = HueInterpolationMethod.Shorter;
    }
}
