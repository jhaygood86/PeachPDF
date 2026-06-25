using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;

namespace PeachPDF.Html.Core.Entities
{
    internal sealed class ParsedLinearGradient
    {
        public double AngleRad { get; init; }
        public required (RColor? Color, Length? Position, bool IsHint)[] Stops { get; init; }
        public bool IsRepeating { get; init; }
        public GradientColorSpace ColorSpace { get; init; } = GradientColorSpace.Srgb;
        public HueInterpolationMethod HueMethod { get; init; } = HueInterpolationMethod.Shorter;
    }
}
