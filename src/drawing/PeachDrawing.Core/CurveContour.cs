using System.Collections.Generic;

namespace PeachDrawing.Core
{
    /// <summary>What a <see cref="PathCommand"/> draws from the previous point to its <see cref="PathCommand.End"/>.</summary>
    public enum PathCommandKind
    {
        /// <summary>A straight line.</summary>
        Line,

        /// <summary>A cubic Bézier curve, shaped by <see cref="PathCommand.Control1"/> and <see cref="PathCommand.Control2"/>.</summary>
        Cubic,
    }

    /// <summary>
    /// One drawing step of a <see cref="CurveContour"/>: a straight line or a cubic Bézier from the previous point (the
    /// contour's <see cref="CurveContour.Start"/> for the first step) to <see cref="End"/>. Elliptical arcs never appear as
    /// their own kind: <see cref="GraphicsPath"/> records every arc as the cubic Béziers that approximate it.
    /// </summary>
    /// <param name="Kind">whether this step is a line or a cubic curve</param>
    /// <param name="Control1">the curve's first control point; equal to <paramref name="End"/> for a line</param>
    /// <param name="Control2">the curve's second control point; equal to <paramref name="End"/> for a line</param>
    /// <param name="End">where the step ends</param>
    public readonly record struct PathCommand(PathCommandKind Kind, PaintPoint Control1, PaintPoint Control2, PaintPoint End)
    {
        /// <summary>A straight line to <paramref name="end"/>.</summary>
        public static PathCommand LineTo(PaintPoint end) => new(PathCommandKind.Line, end, end, end);

        /// <summary>A cubic Bézier curve to <paramref name="end"/>.</summary>
        public static PathCommand CubicTo(PaintPoint control1, PaintPoint control2, PaintPoint end) =>
            new(PathCommandKind.Cubic, control1, control2, end);
    }

    /// <summary>
    /// One subpath of a <see cref="GraphicsPath"/> with its curves intact - the counterpart of <see cref="PathContour"/>,
    /// which is the same subpath already reduced to a polyline. Read it with <see cref="GraphicsPath.GetCurveContours"/>.
    /// </summary>
    /// <param name="Start">where the subpath begins</param>
    /// <param name="Commands">the lines and cubic curves that follow <paramref name="Start"/>, in order</param>
    /// <param name="Closed">whether the subpath was explicitly closed (<see cref="GraphicsPath.CloseFigure"/>): a closed subpath
    /// is joined back to <paramref name="Start"/> by an implicit final line</param>
    public sealed record CurveContour(PaintPoint Start, IReadOnlyList<PathCommand> Commands, bool Closed);
}
