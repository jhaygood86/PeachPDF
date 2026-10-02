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

using System;
using System.Collections.Generic;
using System.Numerics;

namespace PeachDrawing.Core
{
    /// <summary>
    /// A fill or stroke paint source - a closed set of plain-data shapes (a solid colour and three
    /// gradient kinds) any <see cref="Canvas"/> backend can interpret without knowing which
    /// other backend built the instance. This is the same discriminated-union shape <c>CssImage</c>
    /// (<c>Html/Core/Entities/CssImage.cs</c>) already uses for background/gradient/list-marker images -
    /// see <c>CssImagePainter.Paint</c>'s type-dispatch for the precedent. A concrete <see cref="Canvas"/>
    /// (<c>GraphicsAdapter</c>, <c>RasterCanvas</c>) pattern-matches on the concrete subtype to build
    /// whatever native paint representation it needs; nothing here names a PDF- or raster-specific type.
    /// </summary>
    /// <remarks>
    /// Brushes used to be opaque, backend-owned handles (a zero-member marker plus a same-assembly
    /// downcast to read real data out of them) - workable only because exactly one concrete backend
    /// family existed. Making every subtype real, public data instead is what lets a second backend
    /// (the raster backend) - or, eventually, a third party's own <see cref="Canvas"/> - read a brush
    /// without reaching into another backend's internals.
    /// </remarks>
    public abstract class Brush : IDisposable
    {
        /// <summary>No native resource is owned by any of these plain-data shapes; kept for source
        /// compatibility with existing <c>using var brush = ...</c> call sites.</summary>
        public virtual void Dispose() { }
    }

    /// <summary>A single fill colour.</summary>
    public sealed class SolidBrush(PaintColor color) : Brush
    {
        /// <summary>The fill/stroke colour.</summary>
        public PaintColor PaintColor { get; } = color;
    }

    /// <summary>One colour/position pair in a gradient's stop list.</summary>
    public readonly record struct GradientStop(PaintColor PaintColor, double Position);

    /// <summary>Whether a gradient's stop list covers <c>[0, 1]</c> once (<see cref="Pad"/>, CSS's default) or repeats past
    /// its own extent (<see cref="Repeat"/>, CSS <c>repeating-*-gradient()</c>).</summary>
    public enum GradientSpread
    {
        /// <summary>Past its own extent, the gradient holds the colour of its nearest end stop (CSS's default).</summary>
        Pad,
        /// <summary>The gradient's own extent repeats past its ends (CSS <c>repeating-*-gradient()</c>).</summary>
        Repeat,
    }

    /// <summary>A gradient that varies along the line from <see cref="Start"/> to <see cref="End"/> (both in the same
    /// user-space coordinates the brush is painted in).</summary>
    public sealed class LinearGradientBrush(PaintPoint start, PaintPoint end, IReadOnlyList<GradientStop> stops, GradientSpread spread) : Brush
    {
        /// <summary>The point the gradient line starts at.</summary>
        public PaintPoint Start { get; } = start;
        /// <summary>The point the gradient line ends at.</summary>
        public PaintPoint End { get; } = end;
        /// <summary>The gradient's colour stops, in ascending <see cref="GradientStop"/> <c>Position</c> order.</summary>
        public IReadOnlyList<GradientStop> Stops { get; } = stops;
        /// <summary>How the gradient behaves past its own two ends.</summary>
        public GradientSpread Spread { get; } = spread;
    }

    /// <summary>A gradient that varies with distance from <see cref="Center"/>/<see cref="Focus"/> out to an ellipse of
    /// radius (<see cref="RadiusX"/>, <see cref="RadiusY"/>).</summary>
    public sealed class RadialGradientBrush(PaintPoint center, PaintPoint focus, double radiusX, double radiusY, IReadOnlyList<GradientStop> stops, GradientSpread spread, Matrix3x2? transform = null) : Brush
    {
        /// <summary>An optional matrix, applied after the geometry above, that carries the gradient's ellipse into the
        /// coordinates the brush is painted in - what lets it rotate or skew, which a pair of axis-aligned radii cannot
        /// express. <see langword="null"/> when the geometry is already in paint coordinates.</summary>
        public Matrix3x2? Transform { get; } = transform;

        /// <summary>The radius of the circle around <see cref="Focus"/> the gradient's first stop fills, in the units of
        /// <see cref="RadiusX"/> (0, the default, is a point). Meaningful for a circular gradient; with a nonzero value
        /// the gradient is the two-circle (conical) gradient from this circle to the outer one.</summary>
        public double FocusRadius { get; init; }

        /// <summary>The center of the outer ellipse the gradient's last stop reaches.</summary>
        public PaintPoint Center { get; } = center;
        /// <summary>The point the gradient's first stop starts at - equal to <see cref="Center"/> for a concentric radial gradient.</summary>
        public PaintPoint Focus { get; } = focus;
        /// <summary>The outer ellipse's horizontal radius.</summary>
        public double RadiusX { get; } = radiusX;
        /// <summary>The outer ellipse's vertical radius.</summary>
        public double RadiusY { get; } = radiusY;
        /// <summary>The gradient's colour stops, in ascending <see cref="GradientStop"/> <c>Position</c> order.</summary>
        public IReadOnlyList<GradientStop> Stops { get; } = stops;
        /// <summary>How the gradient behaves past its own outer ellipse.</summary>
        public GradientSpread Spread { get; } = spread;
    }

    /// <summary>A gradient that varies with angle around <see cref="Center"/>. Conic gradients have no
    /// repeating form in this codebase today (CSS <c>repeating-conic-gradient()</c> is not yet
    /// supported - see the accepted-gaps notes), so there is no <see cref="GradientSpread"/> here.</summary>
    public sealed class ConicGradientBrush(PaintPoint center, double outerRadius, IReadOnlyList<GradientStop> stops, IReadOnlyList<double> anglesRadians) : Brush
    {
        /// <summary>The point the gradient sweeps around.</summary>
        public PaintPoint Center { get; } = center;
        /// <summary>The radius of the circle the gradient is painted over.</summary>
        public double OuterRadius { get; } = outerRadius;
        /// <summary>The gradient's colour stops, matched positionally with <see cref="AnglesRadians"/>.</summary>
        public IReadOnlyList<GradientStop> Stops { get; } = stops;
        /// <summary>Each stop's angle, in radians clockwise from the gradient's own starting angle.</summary>
        public IReadOnlyList<double> AnglesRadians { get; } = anglesRadians;
    }

}
