using System.Collections.Generic;
using PeachDrawing.Core;

namespace PeachBrackets.Html.Entities
{
    internal sealed class ParsedConicGradient
    {
        public double FromAngleRad { get; init; } = 0.0;
        public double CenterX { get; init; } = 0.5;
        public double CenterY { get; init; } = 0.5;
        public required (PaintColor? PaintColor, double? PositionRad, bool IsHint)[] Stops { get; init; }
        /// <summary>
        /// The indexes into <see cref="Stops"/> of the stops written <c>currentcolor</c>, which take the painting box's own color
        /// when the gradient is drawn; null when there are none.
        /// </summary>
        public IReadOnlyList<int>? CurrentColorStops { get; init; }
        public bool IsRepeating { get; init; }
        public GradientColorSpace ColorSpace { get; init; } = GradientColorSpace.Srgb;
        public HueInterpolationMethod HueMethod { get; init; } = HueInterpolationMethod.Shorter;

        /// <summary>This gradient with <paramref name="stops"/> in place of its own (and no stop left to resolve).</summary>
        public ParsedConicGradient WithStops((PaintColor? PaintColor, double? PositionRad, bool IsHint)[] stops) => new()
        {
            FromAngleRad = FromAngleRad,
            CenterX = CenterX,
            CenterY = CenterY,
            Stops = stops,
            IsRepeating = IsRepeating,
            ColorSpace = ColorSpace,
            HueMethod = HueMethod,
        };
    }
}
