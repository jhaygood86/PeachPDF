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

    /// <summary>
    /// Common definition shared by <c>&lt;linearGradient&gt;</c>/<c>&lt;radialGradient&gt;</c>. Lives
    /// only in <see cref="SvgDocument"/>'s gradient registry - never painted directly, only referenced
    /// by a <see cref="SvgPaint.GradientId"/>.
    /// </summary>
    internal abstract class SvgGradient
    {
        public string? Id { get; init; }

        /// <summary>
        /// True for <c>gradientUnits="userSpaceOnUse"</c> - the only mode supported in v1.
        /// <c>objectBoundingBox</c> (the spec default) is out of scope; a gradient without an explicit
        /// <c>userSpaceOnUse</c> is still treated as user-space, which is only correct when the
        /// document itself was authored with explicit user-space gradient coordinates.
        /// </summary>
        public bool GradientUnitsUserSpaceOnUse { get; init; } = true;

        /// <summary>
        /// Parsed <c>gradientTransform</c> (only translate/scale/matrix are supported - see
        /// <see cref="SvgTransformParser"/>).
        /// </summary>
        public RMatrix? GradientTransform { get; init; }

        public IReadOnlyList<SvgGradientStop> Stops { get; init; } = [];
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
    }
}
