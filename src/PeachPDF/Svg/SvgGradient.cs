// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
//
// - Sun Tsu,
// "The Art of War"

using PeachPDF.Html.Adapters.Entities;
using System.Collections.Generic;

namespace PeachPDF.Svg
{
    /// <summary>
    /// One <c>&lt;stop&gt;</c> of a gradient definition.
    /// </summary>
    internal readonly struct SvgGradientStop
    {
        public double Offset { get; init; }
        public RColor Color { get; init; }
    }

    /// <summary>How a gradient's colors extend beyond its own defined 0-1 stop range.</summary>
    internal enum SvgSpreadMethod
    {
        Pad,
        Reflect,
        Repeat,
    }

    /// <summary>
    /// Common definition shared by <c>&lt;linearGradient&gt;</c>/<c>&lt;radialGradient&gt;</c>. Lives
    /// only in <see cref="SvgDocument"/>'s gradient registry - never painted directly, only referenced
    /// by a <see cref="SvgPaint.ReferenceId"/>.
    /// </summary>
    internal abstract class SvgGradient
    {
        public string? Id { get; init; }

        /// <summary>
        /// True for <c>gradientUnits="userSpaceOnUse"</c>; false (the spec default, including when
        /// the attribute is omitted) means <c>objectBoundingBox</c> - coordinates are fractions of the
        /// referencing shape's bounding box, resolved at paint time (see
        /// <see cref="SvgRenderer"/>/<see cref="SvgGeometryBounds"/>) since a gradient can be shared by
        /// several differently-sized/positioned shapes via <c>fill:url(#id)</c>.
        /// </summary>
        public bool GradientUnitsUserSpaceOnUse { get; init; } = true;

        /// <summary>
        /// Parsed <c>gradientTransform</c> (only translate/scale/matrix are supported - see
        /// <see cref="SvgTransformParser"/>).
        /// </summary>
        public RMatrix? GradientTransform { get; init; }

        public IReadOnlyList<SvgGradientStop> Stops { get; init; } = [];

        /// <summary>
        /// <c>reflect</c>/<c>repeat</c> are both approximated as "don't extend past the defined
        /// gradient region" (rather than truly tiling/mirroring the gradient pattern, which the
        /// underlying PDF shading writer doesn't support without a much larger rework) - a documented
        /// v1 simplification that at least differentiates them from the <c>pad</c> default (which
        /// clamps to the edge stop colors) instead of silently ignoring the attribute.
        /// </summary>
        public SvgSpreadMethod SpreadMethod { get; init; } = SvgSpreadMethod.Pad;
    }

    internal sealed class SvgLinearGradient : SvgGradient
    {
        public double X1 { get; init; }
        public double Y1 { get; init; }
        public double X2 { get; init; }
        public double Y2 { get; init; }
    }

    internal sealed class SvgRadialGradient : SvgGradient
    {
        public double Cx { get; init; }
        public double Cy { get; init; }
        public double R { get; init; }

        /// <summary>Focal point - defaults to (<see cref="Cx"/>, <see cref="Cy"/>) when null (unspecified).</summary>
        public double? Fx { get; init; }
        public double? Fy { get; init; }
    }
}
