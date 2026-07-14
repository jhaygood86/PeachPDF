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

namespace PeachPDF.Svg
{
    /// <summary>
    /// The normalized alphabet a parsed SVG <c>d</c> attribute is reduced to - deliberately smaller
    /// than SVG's own path grammar (no separate quadratic/smooth/horizontal/vertical commands) so the
    /// renderer only ever has to handle five primitive kinds.
    /// </summary>
    internal enum PathSegmentKind
    {
        MoveTo,
        LineTo,
        CubicBezierTo,
        ArcTo,
        ClosePath,
    }

    /// <summary>
    /// One normalized path segment. All coordinates are absolute (already resolved from relative
    /// commands during parsing).
    /// </summary>
    internal readonly struct PathSegment
    {
        public PathSegmentKind Kind { get; private init; }

        /// <summary>End point of the segment (unused for <see cref="PathSegmentKind.ClosePath"/>).</summary>
        public double X { get; private init; }
        public double Y { get; private init; }

        /// <summary>First cubic Bezier control point (only for <see cref="PathSegmentKind.CubicBezierTo"/>).</summary>
        public double X1 { get; private init; }
        public double Y1 { get; private init; }

        /// <summary>Second cubic Bezier control point (only for <see cref="PathSegmentKind.CubicBezierTo"/>).</summary>
        public double X2 { get; private init; }
        public double Y2 { get; private init; }

        /// <summary>Ellipse radii (only for <see cref="PathSegmentKind.ArcTo"/>).</summary>
        public double RadiusX { get; private init; }
        public double RadiusY { get; private init; }

        /// <summary>Ellipse x-axis rotation, in degrees (only for <see cref="PathSegmentKind.ArcTo"/>).</summary>
        public double RotationAngle { get; private init; }

        /// <summary>Arc flags (only for <see cref="PathSegmentKind.ArcTo"/>).</summary>
        public bool IsLargeArc { get; private init; }
        public bool SweepClockwise { get; private init; }

        public static PathSegment MoveTo(double x, double y) =>
            new() { Kind = PathSegmentKind.MoveTo, X = x, Y = y };

        public static PathSegment LineTo(double x, double y) =>
            new() { Kind = PathSegmentKind.LineTo, X = x, Y = y };

        public static PathSegment CubicBezierTo(double x1, double y1, double x2, double y2, double x, double y) =>
            new() { Kind = PathSegmentKind.CubicBezierTo, X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, X = x, Y = y };

        public static PathSegment ArcTo(double radiusX, double radiusY, double rotationAngle, bool isLargeArc, bool sweepClockwise, double x, double y) =>
            new()
            {
                Kind = PathSegmentKind.ArcTo,
                RadiusX = radiusX,
                RadiusY = radiusY,
                RotationAngle = rotationAngle,
                IsLargeArc = isLargeArc,
                SweepClockwise = sweepClockwise,
                X = x,
                Y = y,
            };

        public static PathSegment ClosePath() => new() { Kind = PathSegmentKind.ClosePath };
    }
}
