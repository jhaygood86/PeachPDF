#region PeachPDF - A .NET library for rendering HTML to PDF
//
// Decodes TrueType `glyf` outlines into drawable vector paths (contours of
// straight and cubic-Bezier segments, in font design units, y-up). This is a
// pure geometry reader with no PDF/color dependency; it backs both the
// COLR/CPAL color-glyph painter and any future vector text-outline work.
//
// The `glyf` binary layout followed here is the same one reconstructed by
// Woff2Converter (simple-glyph flag run-length + delta-coordinate decode,
// composite component transforms). TrueType uses quadratic on-/off-curve
// points; since the graphics path layer only exposes cubic Beziers, each
// quadratic is elevated to a cubic here.
//
#endregion

using System.Collections.Generic;

namespace PeachPDF.Fonts.OpenType
{
    /// <summary>A point in font design units (y-up).</summary>
    internal readonly record struct GlyphOutlinePoint(double X, double Y);

    /// <summary>A single path segment: a straight line, or a cubic Bezier.</summary>
    internal readonly struct GlyphSegment
    {
        public bool IsCubic { get; }
        public GlyphOutlinePoint Control1 { get; }
        public GlyphOutlinePoint Control2 { get; }
        public GlyphOutlinePoint End { get; }

        private GlyphSegment(bool isCubic, GlyphOutlinePoint c1, GlyphOutlinePoint c2, GlyphOutlinePoint end)
        {
            IsCubic = isCubic;
            Control1 = c1;
            Control2 = c2;
            End = end;
        }

        public static GlyphSegment Line(GlyphOutlinePoint end) => new(false, default, default, end);

        public static GlyphSegment Cubic(GlyphOutlinePoint control1, GlyphOutlinePoint control2, GlyphOutlinePoint end)
            => new(true, control1, control2, end);
    }

    /// <summary>A closed contour: a start point plus the ordered segments that return to it.</summary>
    internal sealed class GlyphContour(GlyphOutlinePoint start)
    {
        public GlyphOutlinePoint Start { get; } = start;
        public List<GlyphSegment> Segments { get; } = [];
    }

    /// <summary>A decoded glyph outline: zero or more closed contours.</summary>
    internal sealed class GlyphOutline
    {
        public List<GlyphContour> Contours { get; } = [];
        public bool IsEmpty => Contours.Count == 0;
    }

    /// <summary>
    /// Decodes a glyph index into a <see cref="GlyphOutline"/> from a font's `glyf`/`loca` tables.
    /// </summary>
    internal static class GlyphOutlineDecoder
    {
        // Simple-glyph flag bits (glyf spec).
        private const int OnCurvePoint = 0x01;
        private const int XShortVector = 0x02;
        private const int YShortVector = 0x04;
        private const int RepeatFlag = 0x08;
        private const int XIsSameOrPositive = 0x10;
        private const int YIsSameOrPositive = 0x20;

        // Composite-glyph component flag bits (glyf spec).
        private const int Arg1And2AreWords = 0x0001;
        private const int ArgsAreXyValues = 0x0002;
        private const int WeHaveAScale = 0x0008;
        private const int MoreComponents = 0x0020;
        private const int WeHaveAnXAndYScale = 0x0040;
        private const int WeHaveATwoByTwo = 0x0080;

        private const int MaxCompositeDepth = 8;

        /// <summary>
        /// Attempts to decode the outline of <paramref name="glyphIndex"/>. Returns false (with an
        /// empty <paramref name="outline"/>) for an absent glyph subsystem, an out-of-range index,
        /// or an empty glyph (e.g. the space glyph).
        /// </summary>
        public static bool TryGetGlyphOutline(OpenTypeFontface face, int glyphIndex, out GlyphOutline outline)
        {
            outline = new GlyphOutline();

            if (face?.glyf is null || face.loca?.LocaTable is null)
                return false;

            int[] loca = face.loca.LocaTable;
            if (glyphIndex < 0 || glyphIndex + 1 >= loca.Length)
                return false;

            DecodeInto(face, glyphIndex, outline, 0);
            return !outline.IsEmpty;
        }

        private static void DecodeInto(OpenTypeFontface face, int glyphIndex, GlyphOutline outline, int depth)
        {
            if (depth > MaxCompositeDepth)
                return;

            int[] loca = face.loca.LocaTable;
            if (glyphIndex < 0 || glyphIndex + 1 >= loca.Length)
                return;

            int start = face.glyf.GetOffset(glyphIndex);
            int end = face.glyf.GetOffset(glyphIndex + 1);
            if (start >= end)
                return; // empty glyph (no contours)

            face.Position = start;
            int numberOfContours = face.ReadShort();
            face.SeekOffset(8); // skip xMin/yMin/xMax/yMax

            if (numberOfContours >= 0)
                DecodeSimple(face, numberOfContours, outline);
            else
                DecodeComposite(face, outline, depth);
        }

        private static void DecodeSimple(OpenTypeFontface face, int numberOfContours, GlyphOutline outline)
        {
            if (numberOfContours == 0)
                return;

            var endPtsOfContours = new int[numberOfContours];
            for (int i = 0; i < numberOfContours; i++)
                endPtsOfContours[i] = face.ReadUShort();

            int numPoints = endPtsOfContours[numberOfContours - 1] + 1;
            if (numPoints <= 0)
                return;

            int instructionLength = face.ReadUShort();
            face.SeekOffset(instructionLength);

            // Flags (run-length encoded via the repeat bit).
            var flags = new byte[numPoints];
            for (int i = 0; i < numPoints;)
            {
                byte flag = face.ReadByte();
                flags[i++] = flag;
                if ((flag & RepeatFlag) != 0)
                {
                    int repeat = face.ReadByte();
                    while (repeat-- > 0 && i < numPoints)
                        flags[i++] = flag;
                }
            }

            // X coordinates (delta-encoded).
            var xs = new int[numPoints];
            int x = 0;
            for (int i = 0; i < numPoints; i++)
            {
                int flag = flags[i];
                if ((flag & XShortVector) != 0)
                {
                    int dx = face.ReadByte();
                    x += (flag & XIsSameOrPositive) != 0 ? dx : -dx;
                }
                else if ((flag & XIsSameOrPositive) == 0)
                {
                    x += face.ReadShort();
                }
                xs[i] = x;
            }

            // Y coordinates (delta-encoded).
            var ys = new int[numPoints];
            int y = 0;
            for (int i = 0; i < numPoints; i++)
            {
                int flag = flags[i];
                if ((flag & YShortVector) != 0)
                {
                    int dy = face.ReadByte();
                    y += (flag & YIsSameOrPositive) != 0 ? dy : -dy;
                }
                else if ((flag & YIsSameOrPositive) == 0)
                {
                    y += face.ReadShort();
                }
                ys[i] = y;
            }

            // Split into contours and convert each to segments.
            int pointIndex = 0;
            var contourPoints = new List<RawPoint>();
            for (int c = 0; c < numberOfContours; c++)
            {
                contourPoints.Clear();
                int contourEnd = endPtsOfContours[c];
                for (; pointIndex <= contourEnd; pointIndex++)
                    contourPoints.Add(new RawPoint(xs[pointIndex], ys[pointIndex], (flags[pointIndex] & OnCurvePoint) != 0));

                GlyphContour? contour = BuildContour(contourPoints);
                if (contour is not null)
                    outline.Contours.Add(contour);
            }
        }

        private static void DecodeComposite(OpenTypeFontface face, GlyphOutline outline, int depth)
        {
            while (true)
            {
                int flags = face.ReadUShort();
                int componentGlyph = face.ReadUShort();

                double arg1, arg2;
                if ((flags & Arg1And2AreWords) != 0)
                {
                    arg1 = face.ReadShort();
                    arg2 = face.ReadShort();
                }
                else
                {
                    arg1 = (sbyte)face.ReadByte();
                    arg2 = (sbyte)face.ReadByte();
                }

                double a = 1, b = 0, cc = 0, d = 1;
                if ((flags & WeHaveAScale) != 0)
                {
                    a = d = ReadF2Dot14(face);
                }
                else if ((flags & WeHaveAnXAndYScale) != 0)
                {
                    a = ReadF2Dot14(face);
                    d = ReadF2Dot14(face);
                }
                else if ((flags & WeHaveATwoByTwo) != 0)
                {
                    a = ReadF2Dot14(face);
                    b = ReadF2Dot14(face);
                    cc = ReadF2Dot14(face);
                    d = ReadF2Dot14(face);
                }

                // Point-matching offsets (ARGS_ARE_XY_VALUES clear) are unsupported: treat as (0,0).
                double dx = 0, dy = 0;
                if ((flags & ArgsAreXyValues) != 0)
                {
                    dx = arg1;
                    dy = arg2;
                }

                // Decode the referenced component, transform it, and append its contours.
                int resumePosition = face.Position;
                var child = new GlyphOutline();
                DecodeInto(face, componentGlyph, child, depth + 1);
                face.Position = resumePosition;

                foreach (GlyphContour contour in child.Contours)
                    outline.Contours.Add(TransformContour(contour, a, b, cc, d, dx, dy));

                if ((flags & MoreComponents) == 0)
                    break;
            }
        }

        private static GlyphContour TransformContour(GlyphContour source, double a, double b, double c, double d, double dx, double dy)
        {
            GlyphOutlinePoint Map(GlyphOutlinePoint p)
                => new(a * p.X + c * p.Y + dx, b * p.X + d * p.Y + dy);

            var result = new GlyphContour(Map(source.Start));
            foreach (GlyphSegment segment in source.Segments)
            {
                result.Segments.Add(segment.IsCubic
                    ? GlyphSegment.Cubic(Map(segment.Control1), Map(segment.Control2), Map(segment.End))
                    : GlyphSegment.Line(Map(segment.End)));
            }
            return result;
        }

        /// <summary>
        /// Converts one contour's raw on/off-curve points into a closed sequence of line and cubic
        /// segments, inserting implied on-curve midpoints between consecutive off-curve points and
        /// elevating each quadratic to a cubic.
        /// </summary>
        private static GlyphContour? BuildContour(List<RawPoint> points)
        {
            int n = points.Count;
            if (n == 0)
                return null;

            // Find a start on-curve point; synthesize one (midpoint of the two wrapping off-curve
            // points) if the contour is entirely off-curve.
            int firstOn = -1;
            for (int i = 0; i < n; i++)
            {
                if (points[i].OnCurve)
                {
                    firstOn = i;
                    break;
                }
            }

            GlyphOutlinePoint startPoint;
            var sequence = new List<RawPoint>(n + 1);
            if (firstOn < 0)
            {
                startPoint = Midpoint(points[0], points[n - 1]);
                for (int i = 0; i < n; i++)
                    sequence.Add(points[i]);
                sequence.Add(new RawPoint(startPoint.X, startPoint.Y, true));
            }
            else
            {
                startPoint = new GlyphOutlinePoint(points[firstOn].X, points[firstOn].Y);
                for (int i = 1; i <= n; i++)
                    sequence.Add(points[(firstOn + i) % n]);
            }

            var contour = new GlyphContour(startPoint);
            GlyphOutlinePoint current = startPoint;
            RawPoint? pendingControl = null;

            foreach (RawPoint p in sequence)
            {
                if (p.OnCurve)
                {
                    var end = new GlyphOutlinePoint(p.X, p.Y);
                    if (pendingControl is { } ctrl)
                    {
                        contour.Segments.Add(QuadraticToCubic(current, ctrl, end));
                        pendingControl = null;
                    }
                    else
                    {
                        contour.Segments.Add(GlyphSegment.Line(end));
                    }
                    current = end;
                }
                else if (pendingControl is { } ctrl)
                {
                    // Two consecutive off-curve points: insert the implied on-curve midpoint.
                    GlyphOutlinePoint mid = Midpoint(ctrl, p);
                    contour.Segments.Add(QuadraticToCubic(current, ctrl, mid));
                    current = mid;
                    pendingControl = p;
                }
                else
                {
                    pendingControl = p;
                }
            }

            return contour;
        }

        /// <summary>
        /// Test seam: builds a single contour from raw on/off-curve points (font design units),
        /// exercising the implied-midpoint insertion and quadratic-to-cubic elevation directly.
        /// </summary>
        internal static GlyphContour? BuildContourForTest(IReadOnlyList<(double X, double Y, bool OnCurve)> points)
        {
            var raw = new List<RawPoint>(points.Count);
            foreach ((double x, double y, bool onCurve) in points)
                raw.Add(new RawPoint(x, y, onCurve));
            return BuildContour(raw);
        }

        private static GlyphSegment QuadraticToCubic(GlyphOutlinePoint start, RawPoint control, GlyphOutlinePoint end)
        {
            // Elevate a quadratic (start, control, end) to a cubic:
            //   C1 = start + 2/3 (control - start),  C2 = end + 2/3 (control - end).
            var c1 = new GlyphOutlinePoint(
                start.X + 2.0 / 3.0 * (control.X - start.X),
                start.Y + 2.0 / 3.0 * (control.Y - start.Y));
            var c2 = new GlyphOutlinePoint(
                end.X + 2.0 / 3.0 * (control.X - end.X),
                end.Y + 2.0 / 3.0 * (control.Y - end.Y));
            return GlyphSegment.Cubic(c1, c2, end);
        }

        private static GlyphOutlinePoint Midpoint(RawPoint a, RawPoint b)
            => new((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);

        private static double ReadF2Dot14(OpenTypeFontface face) => face.ReadShort() / 16384.0;

        private readonly record struct RawPoint(double X, double Y, bool OnCurve);
    }
}
