using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using System;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Lays a <c>dotted</c>/<c>dashed</c> pattern along one open border/outline edge so it starts
    /// and ends flush with the edge, the way a browser does, instead of running a fixed-period pattern
    /// off the end and leaving a ragged stub in the corner.
    /// </summary>
    /// <remarks>
    /// The ideal pattern is a dash of <c>width</c> (dotted) or <c>2 x width</c> (dashed) separated by a
    /// gap of <c>width</c>. That almost never divides the edge exactly, so <see cref="Fit"/> tries the
    /// two whole dash counts that bracket the edge length and keeps whichever needs a gap closer to the
    /// ideal - which is sometimes the *smaller* count, not simply "round up". The rule, the dash/gap
    /// ratios and the resulting counts were all derived by measuring Chrome's rasterization of the same
    /// boxes (Blink's <c>StyledStrokeData</c>/<c>SelectBestDashGap</c>) and reproduce it exactly for
    /// every case measured, down to 2px borders.
    ///
    /// Dots are drawn as a zero-length dash under a round cap, which PDF paints as a filled circle of
    /// the pen's width - verified to render identically in PDFium and MuPDF, per this repo's
    /// two-renderer paint-verification convention. That is also why <see cref="Apply"/> hands back a
    /// span rather than the caller's raw edge: a dot is centered on the path, so the path has to be
    /// inset by half a width at each end for the first and last dots to sit inside the corner.
    /// </remarks>
    internal static class StyledStrokeFitting
    {
        /// <summary>One fitted pattern: a dash of <paramref name="DashLength"/> every <see cref="Period"/>.</summary>
        internal readonly record struct Pattern(double DashLength, double GapLength)
        {
            internal double Period => DashLength + GapLength;
        }

        /// <summary>
        /// Fits the pattern for an edge of <paramref name="edgeLength"/>, or null when the edge is too
        /// short to hold more than one dash - the caller then strokes it solid, since a single dash
        /// spanning the whole edge is what a "dashed" pattern degenerates to anyway.
        /// </summary>
        internal static Pattern? Fit(bool dotted, double strokeWidth, double edgeLength)
        {
            if (strokeWidth <= 0 || edgeLength <= 0) return null;

            var dashLength = dotted ? strokeWidth : strokeWidth * 2;
            var idealGap = strokeWidth;

            // The two whole dash counts bracketing the edge. An open run of n dashes has n-1 gaps, so
            // fewer than two dashes has no gap to size and nothing to fit. The count is clamped rather
            // than cast blind: a hairline width against a long edge can exceed int range, and an
            // overflowed (negative) count would silently produce nonsense geometry instead of failing.
            var exactCount = Math.Floor(edgeLength / (dashLength + idealGap));
            if (exactCount >= int.MaxValue) return null;

            var lowCount = (int)exactCount;
            var highCount = lowCount + 1;
            if (highCount < 2) return null;

            var highGap = (edgeLength - highCount * dashLength) / (highCount - 1);

            // highCount dashes may simply not fit; lowCount needs at least two to have a gap at all.
            if (lowCount < 2)
                return highGap > 0 ? new Pattern(dashLength, highGap) : null;

            var lowGap = (edgeLength - lowCount * dashLength) / (lowCount - 1);
            if (highGap <= 0)
                return new Pattern(dashLength, lowGap);

            return Math.Abs(lowGap - idealGap) <= Math.Abs(highGap - idealGap)
                ? new Pattern(dashLength, lowGap)
                : new Pattern(dashLength, highGap);
        }

        /// <summary>
        /// <see cref="Fit"/> for a closed path, where the run wraps around rather than stopping - so
        /// there are exactly as many gaps as dashes, and a dash count fixes the gap outright. Returns
        /// null when even one dash plus its gap does not fit.
        /// </summary>
        internal static Pattern? FitClosed(bool dotted, double strokeWidth, double pathLength)
        {
            if (strokeWidth <= 0 || pathLength <= 0) return null;

            var dashLength = dotted ? strokeWidth : strokeWidth * 2;
            var idealGap = strokeWidth;

            var ideal = pathLength / (dashLength + idealGap);
            if (ideal < 1 || ideal >= int.MaxValue) return null;

            var lowCount = (int)Math.Floor(ideal);
            var highCount = lowCount + 1;

            var lowGap = pathLength / lowCount - dashLength;
            var highGap = pathLength / highCount - dashLength;

            if (highGap <= 0)
                return lowGap > 0 ? new Pattern(dashLength, lowGap) : null;

            return Math.Abs(lowGap - idealGap) <= Math.Abs(highGap - idealGap)
                ? new Pattern(dashLength, lowGap)
                : new Pattern(dashLength, highGap);
        }

        /// <summary>
        /// Ramanujan's approximation of a full ellipse's perimeter, accurate to well under a part in
        /// 10,000 across the eccentricities a border radius produces. Used to measure a rounded border's
        /// corners so a dash pattern can be fitted to the whole outline - an ellipse has no closed-form
        /// arc length, and this is far more than precise enough to place dots evenly.
        /// </summary>
        internal static double EllipsePerimeter(double radiusX, double radiusY)
        {
            if (radiusX <= 0 && radiusY <= 0) return 0;

            var a = Math.Max(radiusX, 0);
            var b = Math.Max(radiusY, 0);
            return Math.PI * (3 * (a + b) - Math.Sqrt((3 * a + b) * (a + 3 * b)));
        }

        /// <summary>
        /// Length of an elliptical arc. Five-point Gauss-Legendre integration is effectively exact at
        /// border-rendering precision for the at-most-quarter-turn arcs used by rounded borders.
        /// </summary>
        internal static double EllipseArcLength(
            double radiusX, double radiusY, double startAngle, double endAngle)
        {
            if (radiusX <= 0 || radiusY <= 0 || Math.Abs(endAngle - startAngle) <= double.Epsilon)
                return 0;

            static double Speed(double angle, double x, double y)
            {
                var dx = x * Math.Sin(angle);
                var dy = y * Math.Cos(angle);
                return Math.Sqrt(dx * dx + dy * dy);
            }

            const double innerNode = 0.5384693101056831;
            const double outerNode = 0.9061798459386640;
            const double centerWeight = 0.5688888888888889;
            const double innerWeight = 0.4786286704993665;
            const double outerWeight = 0.2369268850561891;

            var midpoint = (startAngle + endAngle) / 2;
            var halfRange = Math.Abs(endAngle - startAngle) / 2;
            return halfRange * (
                centerWeight * Speed(midpoint, radiusX, radiusY) +
                innerWeight * (
                    Speed(midpoint - halfRange * innerNode, radiusX, radiusY) +
                    Speed(midpoint + halfRange * innerNode, radiusX, radiusY)) +
                outerWeight * (
                    Speed(midpoint - halfRange * outerNode, radiusX, radiusY) +
                    Speed(midpoint + halfRange * outerNode, radiusX, radiusY)));
        }

        /// <summary>
        /// Configures <paramref name="pen"/> for a dotted/dashed edge running from
        /// <paramref name="edgeStart"/> to <paramref name="edgeEnd"/> along its own axis, and returns
        /// the span to actually stroke. Returns null when the edge cannot carry a pattern, meaning the
        /// caller should stroke it solid over the original span.
        /// </summary>
        /// <param name="pen">the pen to configure - its <see cref="RPen.Width"/> must already be set</param>
        /// <param name="dotted">true for <c>dotted</c>, false for <c>dashed</c></param>
        /// <param name="strokeWidth">the border/outline width, in the caller's raw layout-space units</param>
        /// <param name="edgeStart">where the edge begins along its axis, in layout-space units</param>
        /// <param name="edgeEnd">where the edge ends along its axis, in layout-space units</param>
        /// <param name="pixelsPerPoint">
        /// the graphics device's layout-units-per-point ratio. A dash array, like a pen's stroke width
        /// and unlike every coordinate handed to <c>DrawLine</c>, reaches the backend undivided, so it
        /// has to be converted here rather than being passed through as a layout-space value.
        /// </param>
        internal static (double Start, double End)? Apply(
            RPen pen, bool dotted, double strokeWidth, double edgeStart, double edgeEnd, double pixelsPerPoint)
        {
            var fitted = Fit(dotted, strokeWidth, Math.Abs(edgeEnd - edgeStart));
            if (fitted is not { } pattern) return null;

            if (!dotted)
            {
                pen.LineCap = RLineCap.Butt;
                pen.SetDashPattern([pattern.DashLength / pixelsPerPoint, pattern.GapLength / pixelsPerPoint], 0);
                return (edgeStart, edgeEnd);
            }

            // A round cap turns each zero-length dash into a circle centred on the path, so the path
            // runs centre-to-centre: inset half a width at each end, leaving the first and last dots
            // tangent to the box's corners exactly as a browser draws them.
            pen.LineCap = RLineCap.Round;
            pen.SetDashPattern([0, pattern.Period / pixelsPerPoint], 0);

            var direction = Math.Sign(edgeEnd - edgeStart);
            var start = edgeStart + direction * strokeWidth / 2;
            var end = edgeEnd - direction * strokeWidth / 2;

            // Run half a period past the final dot. Landing a zero-length dash exactly on the path's
            // end is a coin flip - PDFium drops it, MuPDF keeps it - and anything short of a full
            // period cannot introduce a dot that should not be there.
            return (start, end + direction * pattern.Period / 2);
        }
    }
}
