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

namespace PeachDrawing.Core
{
    /// <summary>
    /// The center parameterization of an <see cref="GraphicsPath.AddArc"/>-style endpoint-parameterized
    /// elliptical arc - <c>StartAngle</c>/<c>EndAngle</c> are in the ellipse's own (rotated)
    /// local parameter space, so a caller samples a point at angle <c>t</c> as
    /// <c>(CenterX + RadiusX*cos(t)*cos(RotationRadians) - RadiusY*sin(t)*sin(RotationRadians), CenterY + RadiusX*cos(t)*sin(RotationRadians) + RadiusY*sin(t)*cos(RotationRadians))</c>.
    /// </summary>
    public readonly record struct EllipticalArcCenter(double CenterX, double CenterY, double RadiusX, double RadiusY, double RotationRadians, double StartAngle, double EndAngle);

    /// <summary>
    /// Converts an SVG-style endpoint-parameterized elliptical arc (the shape of a path's own <c>A</c>
    /// command, and of <see cref="GraphicsPath.AddArc"/>) to the center parameterization every ellipse
    /// point/tangent computation actually needs, per the standard algorithm (SVG 1.1 Appendix F.6.5,
    /// "Conversion from endpoint to center parameterization" - the same construction every SVG-conformant
    /// renderer uses, not something specific to this codebase).
    /// </summary>
    public static class EllipticalArc
    {
        /// <summary>
        /// Returns false for a degenerate arc (coincident endpoints, or a zero radius) - SVG's own rule
        /// for that case is to treat the command as a straight line, which the caller does instead.
        /// </summary>
        public static bool TryGetCenterParameterization(
            double x1, double y1, double x2, double y2, double radiusX, double radiusY,
            double rotationRadians, bool isLargeArc, bool sweepClockwise, out EllipticalArcCenter arc)
        {
            arc = default;

            if ((x1 == x2 && y1 == y2) || radiusX == 0 || radiusY == 0)
                return false;

            var rx = Math.Abs(radiusX);
            var ry = Math.Abs(radiusY);
            var cosPhi = Math.Cos(rotationRadians);
            var sinPhi = Math.Sin(rotationRadians);

            // Step 1: (x1, y1) in the frame centred on the chord midpoint, rotated by -phi.
            var dx2 = (x1 - x2) / 2;
            var dy2 = (y1 - y2) / 2;
            var x1P = cosPhi * dx2 + sinPhi * dy2;
            var y1P = -sinPhi * dx2 + cosPhi * dy2;

            // Step 2: scale up the radii if the chord is too long for them to reach at all.
            var lambda = x1P * x1P / (rx * rx) + y1P * y1P / (ry * ry);
            if (lambda > 1)
            {
                var scale = Math.Sqrt(lambda);
                rx *= scale;
                ry *= scale;
            }

            // Step 3: the ellipse centre in the same rotated, chord-midpoint-relative frame.
            var rxSq = rx * rx;
            var rySq = ry * ry;
            var x1PSq = x1P * x1P;
            var y1PSq = y1P * y1P;
            var num = rxSq * rySq - rxSq * y1PSq - rySq * x1PSq;
            var den = rxSq * y1PSq + rySq * x1PSq;
            var coefSign = isLargeArc != sweepClockwise ? 1.0 : -1.0;
            var coef = den == 0 ? 0 : coefSign * Math.Sqrt(Math.Max(0, num / den));
            var cxP = coef * (rx * y1P / ry);
            var cyP = coef * (-ry * x1P / rx);

            // Step 4: rotate/translate the centre back into the original coordinate space.
            var cx = cosPhi * cxP - sinPhi * cyP + (x1 + x2) / 2;
            var cy = sinPhi * cxP + cosPhi * cyP + (y1 + y2) / 2;

            // Step 5: the start angle and angular sweep, in the ellipse's own unrotated parameter space.
            var startAngle = VectorAngle(1, 0, (x1P - cxP) / rx, (y1P - cyP) / ry);
            var sweep = VectorAngle((x1P - cxP) / rx, (y1P - cyP) / ry, (-x1P - cxP) / rx, (-y1P - cyP) / ry);

            if (!sweepClockwise && sweep > 0)
                sweep -= 2 * Math.PI;
            else if (sweepClockwise && sweep < 0)
                sweep += 2 * Math.PI;

            arc = new EllipticalArcCenter(cx, cy, rx, ry, rotationRadians, startAngle, startAngle + sweep);
            return true;
        }

        /// <summary>The signed angle from <c>u</c> to <c>v</c>, in (-pi, pi].</summary>
        private static double VectorAngle(double ux, double uy, double vx, double vy)
        {
            var dot = ux * vx + uy * vy;
            var len = Math.Sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy));
            var cos = len == 0 ? 1 : Math.Clamp(dot / len, -1.0, 1.0);
            var angle = Math.Acos(cos);
            return (ux * vy - uy * vx) < 0 ? -angle : angle;
        }
    }
}
