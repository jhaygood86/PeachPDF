using PeachDrawing.Text.Outlines;
using System;

namespace PeachDrawing.Text.Internal.Fonts.OpenType.Variations
{
    /// <summary>
    /// The font bounding box of an instance of a variable font: the <c>head</c> box moves with the outlines, which nothing in <c>MVAR</c>
    /// says how to do, so it is worked out from the glyphs the way a tool that writes a static instance does. The rule is the one fontTools'
    /// instancer follows when it saves the instance: for TrueType outlines the box of every point of every glyph (off-curve ones included,
    /// as the bounds of a <c>glyf</c> entry are), rounded to the nearest unit; for CFF2 the box of the curves the charstrings draw, rounded
    /// outwards.
    /// </summary>
    internal static class InstanceFontBox
    {
        /// <summary>How many operands and operators reading every charstring of a CFF2 font may execute, in all: real fonts take a small fraction.</summary>
        private const long MaxCff2Steps = 1L << 27;

        /// <summary>
        /// Works out the box of <paramref name="face"/> at <paramref name="variation"/>. <see langword="false"/> when the font has no outlines this
        /// reader knows, no ink at all, or is too large or damaged to be read in full within the limits; the caller keeps the <c>head</c> box then.
        /// </summary>
        internal static bool TryCompute(OpenTypeFontface face, VariationCoordinates variation, out (int XMin, int YMin, int XMax, int YMax) box)
        {
            box = default;
            if (variation.IsDefault)
                return false;

            if (face.glyf is not null)
                return GlyphOutlineDecoder.TryGetControlBounds(face, variation, out box);

            return face.cff2 is { IsSupported: true } cff2 && TryComputeCff2(cff2, variation, out box);
        }

        private static bool TryComputeCff2(Cff2Table cff2, VariationCoordinates variation, out (int XMin, int YMin, int XMax, int YMax) box)
        {
            box = default;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            long stepsLeft = MaxCff2Steps;

            for (int glyph = 0; glyph < cff2.GlyphCount; glyph++)
            {
                if (!Type2CharstringInterpreter.TryGetGlyphOutline(cff2, glyph, variation, out var outline, ref stepsLeft))
                {
                    // A glyph with no path (a space) has no ink; one that ran out of budget ends the search.
                    if (stepsLeft <= 0)
                        return false;

                    continue;
                }

                foreach (var contour in outline.Contours)
                {
                    Include(contour.Start, ref minX, ref minY, ref maxX, ref maxY);
                    var current = contour.Start;
                    foreach (var segment in contour.Segments)
                    {
                        if (segment.IsCubic)
                            IncludeCubic(current, segment.Control1, segment.Control2, segment.End, ref minX, ref minY, ref maxX, ref maxY);
                        else
                            Include(segment.End, ref minX, ref minY, ref maxX, ref maxY);

                        current = segment.End;
                    }
                }
            }

            if (minX > maxX)
                return false;

            box = (ToShort(Math.Floor(minX)), ToShort(Math.Floor(minY)), ToShort(Math.Ceiling(maxX)), ToShort(Math.Ceiling(maxY)));
            return true;
        }

        private static int ToShort(double value) => (int)Math.Clamp(value, short.MinValue, short.MaxValue);

        private static void Include(OutlinePoint p, ref double minX, ref double minY, ref double maxX, ref double maxY)
        {
            minX = Math.Min(minX, p.X);
            maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y);
            maxY = Math.Max(maxY, p.Y);
        }

        /// <summary>Adds the end point and the extremes of a cubic Bezier curve (the points where its derivative is zero on either axis).</summary>
        private static void IncludeCubic(OutlinePoint p0, OutlinePoint p1, OutlinePoint p2, OutlinePoint p3,
            ref double minX, ref double minY, ref double maxX, ref double maxY)
        {
            Include(p3, ref minX, ref minY, ref maxX, ref maxY);

            Span<double> roots = stackalloc double[4];
            int count = Extremes(p0.X, p1.X, p2.X, p3.X, roots);
            count += Extremes(p0.Y, p1.Y, p2.Y, p3.Y, roots[count..]);
            for (int i = 0; i < count; i++)
            {
                double t = roots[i];
                double u = 1 - t;
                double x = u * u * u * p0.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * p3.X;
                double y = u * u * u * p0.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * p3.Y;
                Include(new OutlinePoint(x, y), ref minX, ref minY, ref maxX, ref maxY);
            }
        }

        /// <summary>Writes the parameters in (0, 1) at which one coordinate of a cubic Bezier curve has a zero derivative to <paramref name="roots"/> (room for two) and returns how many.</summary>
        private static int Extremes(double a, double b, double c, double d, Span<double> roots)
        {
            // The derivative is 3 * (q2 * t^2 + q1 * t + q0) with these coefficients.
            double q2 = -a + 3 * b - 3 * c + d;
            double q1 = 2 * (a - 2 * b + c);
            double q0 = b - a;

            int count = 0;
            if (Math.Abs(q2) < 1e-12)
            {
                if (Math.Abs(q1) > 1e-12)
                {
                    double t = -q0 / q1;
                    if (t > 0 && t < 1)
                        roots[count++] = t;
                }

                return count;
            }

            double discriminant = q1 * q1 - 4 * q2 * q0;
            if (discriminant < 0)
                return 0;

            double root = Math.Sqrt(discriminant);
            double first = (-q1 + root) / (2 * q2);
            double second = (-q1 - root) / (2 * q2);
            if (first > 0 && first < 1)
                roots[count++] = first;
            if (second > 0 && second < 1)
                roots[count++] = second;

            return count;
        }
    }
}