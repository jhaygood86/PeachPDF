using System.Collections.Generic;
using PeachBrackets.Css;
using PeachDrawing.Core;

namespace PeachBrackets.Html.Entities
{
    internal sealed class ParsedLinearGradient
    {
        public double AngleRad { get; init; }
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
}
