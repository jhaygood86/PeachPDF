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

using PeachDrawing.Text.Internal.Fonts.OpenType.Variations;
using PeachDrawing.Text.Outlines;
using System;
using System.Collections.Generic;

namespace PeachDrawing.Text.Internal.Fonts.OpenType
{
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
        /// How many glyphs one outline may read, components included. A composite may list tens of thousands of components, each of them
        /// a composite, so a font of a few kilobytes could otherwise make one outline take for ever; real glyphs use a handful.
        /// </summary>
        private const int MaxGlyphsPerOutline = 1024;

        private sealed class Budget(int glyphs = MaxGlyphsPerOutline, long work = long.MaxValue)
        {
            public int GlyphsLeft = glyphs;

            /// <summary>Points still to be read, each counted as much as the work of reading it takes (more with variation data).</summary>
            public long WorkLeft = work;
        }

        /// <summary>
        /// How many glyphs reading the bounds of a whole font may visit, components included: a few times the most glyphs a font can have, so
        /// that a font whose composites name each other over and over cannot make it take for ever.
        /// </summary>
        private const int MaxGlyphsForFontBounds = 1 << 18;

        /// <summary>
        /// How much reading the points of a whole font for its bounds may cost: a point counts once, and 64 times where a <c>gvar</c> table
        /// works out its deltas (a glyph can have 65,536 points and thousands of tuples). Real fonts use a small part of it; a hostile one whose
        /// glyphs each declare the most points in a few hundred bytes cannot make the scan, which holds the face's lock, run for long.
        /// </summary>
        private const long MaxWorkForFontBounds = 1L << 29;

        /// <summary>
        /// Attempts to decode the outline of <paramref name="glyphIndex"/>. Returns false (with an
        /// empty <paramref name="outline"/>) for an absent glyph subsystem, an out-of-range index,
        /// or an empty glyph (e.g. the space glyph).
        /// </summary>
        public static bool TryGetGlyphOutline(OpenTypeFontface face, int glyphIndex, out GlyphOutline outline, VariationCoordinates? variation = null)
        {
            outline = new GlyphOutline();

            if (face?.glyf is null || face.loca?.LocaTable is null)
                return false;

            int[] loca = face.loca.LocaTable;
            if (glyphIndex < 0 || glyphIndex + 1 >= loca.Length)
                return false;

            // Every call decodes fresh through this fontface's single shared read cursor (no per-glyph
            // cache) - see OpenTypeFontface.SyncRoot. Locked at this entry point, not per DecodeInto
            // recursion step, so one whole (possibly multi-component composite) glyph read is atomic.
            lock (face.SyncRoot)
            {
                try
                {
                    DecodeInto(face, glyphIndex, outline, 0, variation, new Budget());
                }
                catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException or InvalidOperationException or OverflowException)
                {
                    // Glyph data that reaches past the end of the font (the reads go through the shared cursor, which is bounded
                    // only by the font's bytes), or is otherwise nonsense: the font is damaged, and the glyph has no outline.
                    outline = new GlyphOutline();
                }
            }
            return !outline.IsEmpty;
        }

        /// <summary>
        /// The number of points a glyph has as <c>gvar</c> counts them, phantom points included: a simple glyph's outline points, a
        /// composite glyph's components, and four more.
        /// </summary>
        public static int GetVariationPointCount(OpenTypeFontface face, int glyphIndex)
        {
            if (face?.glyf is null || face.loca?.LocaTable is null)
                return 4;

            int[] loca = face.loca.LocaTable;
            if (glyphIndex < 0 || glyphIndex + 1 >= loca.Length)
                return 4;

            lock (face.SyncRoot)
            {
                int start = face.glyf.GetOffset(glyphIndex);
                int end = face.glyf.GetOffset(glyphIndex + 1);
                if (start >= end)
                    return 4;

                face.Position = start;
                int numberOfContours = face.ReadShort();
                face.SeekOffset(8);
                if (numberOfContours > 0)
                {
                    int last = 0;
                    for (int i = 0; i < numberOfContours; i++)
                        last = face.ReadUShort();
                    return last + 1 + 4;
                }

                if (numberOfContours == 0)
                    return 4;

                int components = 0;
                while (true)
                {
                    int flags = face.ReadUShort();
                    face.ReadUShort();
                    face.SeekOffset((flags & Arg1And2AreWords) != 0 ? 4 : 2);
                    if ((flags & WeHaveAScale) != 0)
                        face.SeekOffset(2);
                    else if ((flags & WeHaveAnXAndYScale) != 0)
                        face.SeekOffset(4);
                    else if ((flags & WeHaveATwoByTwo) != 0)
                        face.SeekOffset(8);
                    components++;
                    if ((flags & MoreComponents) == 0)
                        break;
                }

                return components + 4;
            }
        }

        /// <summary>
        /// The box that holds every point, on-curve and off-curve, of every glyph at <paramref name="variation"/>, rounded as the glyph bounds
        /// of a font are: the box a font-editing tool would write into <c>head</c> for the instance. <see langword="false"/> when the font has
        /// no <c>glyf</c> outlines, has no ink, or is too large or damaged to read them all within the budget.
        /// </summary>
        public static bool TryGetControlBounds(OpenTypeFontface face, VariationCoordinates variation, out (int XMin, int YMin, int XMax, int YMax) box)
        {
            box = default;
            if (face?.glyf is null || face.loca?.LocaTable is null)
                return false;

            int glyphCount = face.loca.LocaTable.Length - 1;
            var gvar = variation.IsDefault ? null : face.Variations?.Gvar;
            var budget = new Budget(MaxGlyphsForFontBounds, MaxWorkForFontBounds);
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;

            lock (face.SyncRoot)
            {
                for (int glyph = 0; glyph < glyphCount; glyph++)
                {
                    var points = new ControlBounds();
                    try
                    {
                        AccumulateControlPoints(face, glyph, Affine.Identity, 0, gvar, variation, budget, ref points);
                    }
                    catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException or InvalidOperationException or OverflowException)
                    {
                        // A glyph that cannot be read has no points; the others still count.
                        continue;
                    }

                    if (budget.GlyphsLeft < 0 || budget.WorkLeft < 0)
                        return false;

                    if (points.Any)
                    {
                        minX = Math.Min(minX, points.MinX);
                        minY = Math.Min(minY, points.MinY);
                        maxX = Math.Max(maxX, points.MaxX);
                        maxY = Math.Max(maxY, points.MaxY);
                    }
                }
            }

            if (minX > maxX)
                return false;

            box = (RoundToShort(minX), RoundToShort(minY), RoundToShort(maxX), RoundToShort(maxY));
            return true;
        }

        private static int RoundToShort(double value) => (int)Math.Clamp(Math.Floor(value + 0.5), short.MinValue, short.MaxValue);

        private struct ControlBounds
        {
            public bool Any;
            public double MinX, MinY, MaxX, MaxY;

            public void Add(double x, double y)
            {
                if (!Any)
                {
                    (MinX, MaxX, MinY, MaxY, Any) = (x, x, y, y, true);
                    return;
                }

                MinX = Math.Min(MinX, x);
                MaxX = Math.Max(MaxX, x);
                MinY = Math.Min(MinY, y);
                MaxY = Math.Max(MaxY, y);
            }
        }

        private readonly record struct Affine(double A, double B, double C, double D, double E, double F)
        {
            public static Affine Identity => new(1, 0, 0, 1, 0, 0);

            /// <summary>The transform that applies <paramref name="component"/>'s own transform and then this one.</summary>
            public Affine Then(Component component) => new(
                A * component.A + C * component.B, B * component.A + D * component.B,
                A * component.C + C * component.D, B * component.C + D * component.D,
                A * component.Dx + C * component.Dy + E, B * component.Dx + D * component.Dy + F);
        }

        private static void AccumulateControlPoints(OpenTypeFontface face, int glyphIndex, Affine transform, int depth, GvarTable? gvar,
            VariationCoordinates variation, Budget budget, ref ControlBounds bounds)
        {
            if (depth > MaxCompositeDepth || --budget.GlyphsLeft < 0)
                return;

            int[] loca = face.loca.LocaTable;
            if (glyphIndex < 0 || glyphIndex + 1 >= loca.Length)
                return;

            int start = face.glyf.GetOffset(glyphIndex);
            int end = face.glyf.GetOffset(glyphIndex + 1);
            if (start >= end)
                return;

            face.Position = start;
            int numberOfContours = face.ReadShort();
            face.SeekOffset(8);

            if (numberOfContours >= 0)
            {
                if (!TryReadSimplePoints(face, numberOfContours, glyphIndex, gvar, variation,
                        out _, out _, out var xs, out var ys, out var moveX, out var moveY, budget))
                    return;

                for (int i = 0; i < xs.Length; i++)
                {
                    double x = xs[i] + (moveX is null ? 0 : moveX[i]);
                    double y = ys[i] + (moveY is null ? 0 : moveY[i]);
                    bounds.Add(transform.A * x + transform.C * y + transform.E, transform.B * x + transform.D * y + transform.F);
                }

                return;
            }

            var components = ReadComponents(face);
            MoveComponents(components, glyphIndex, gvar, variation);
            foreach (var component in components)
                AccumulateControlPoints(face, component.Glyph, transform.Then(component), depth + 1, gvar, variation, budget, ref bounds);
        }

        private static void DecodeInto(OpenTypeFontface face, int glyphIndex, GlyphOutline outline, int depth, VariationCoordinates? variation, Budget budget)
        {
            if (depth > MaxCompositeDepth || --budget.GlyphsLeft < 0)
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

            // Only a location away from the defaults changes anything, and only a font with gvar has anything to change it with.
            var gvar = variation is { IsDefault: false } ? face.Variations?.Gvar : null;

            if (numberOfContours >= 0)
                DecodeSimple(face, numberOfContours, outline, glyphIndex, gvar, variation);
            else
                DecodeComposite(face, outline, depth, glyphIndex, gvar, variation, budget);
        }

        private static void DecodeSimple(OpenTypeFontface face, int numberOfContours, GlyphOutline outline, int glyphIndex,
            GvarTable? gvar, VariationCoordinates? variation)
        {
            if (!TryReadSimplePoints(face, numberOfContours, glyphIndex, gvar, variation,
                    out var endPtsOfContours, out var flags, out var xs, out var ys, out var moveX, out var moveY))
                return;

            int numPoints = xs.Length;

            // Split into contours and convert each to segments. numPoints was sized from the LAST
            // entry of endPtsOfContours; a malformed or corrupted glyph whose entries aren't
            // monotonically increasing could otherwise walk pointIndex past xs/ys's bounds here, so
            // the loop is capped at numPoints regardless of what an earlier, out-of-order entry claims.
            int pointIndex = 0;
            var contourPoints = new List<RawPoint>();
            for (int c = 0; c < numberOfContours; c++)
            {
                contourPoints.Clear();
                int contourEnd = endPtsOfContours[c];
                for (; pointIndex <= contourEnd && pointIndex < numPoints; pointIndex++)
                    contourPoints.Add(new RawPoint(
                        xs[pointIndex] + (moveX is null ? 0 : moveX[pointIndex]),
                        ys[pointIndex] + (moveY is null ? 0 : moveY[pointIndex]),
                        (flags[pointIndex] & OnCurvePoint) != 0));

                OutlineContour? contour = BuildContour(contourPoints);
                if (contour is not null)
                    outline.ContourList.Add(contour);
            }
        }

        /// <summary>
        /// Reads the points of a simple glyph as stored, and (at a location of a variable font) the deltas <c>gvar</c> gives them: the point
        /// at index <c>i</c> is at <c>xs[i] + moveX[i]</c>. The cursor of <paramref name="face"/> must be at the glyph's contour count's successor.
        /// </summary>
        private static bool TryReadSimplePoints(OpenTypeFontface face, int numberOfContours, int glyphIndex, GvarTable? gvar, VariationCoordinates? variation,
            out int[] endPtsOfContours, out byte[] flags, out int[] xs, out int[] ys, out double[]? moveX, out double[]? moveY, Budget? budget = null)
        {
            endPtsOfContours = [];
            flags = [];
            xs = [];
            ys = [];
            moveX = null;
            moveY = null;
            if (numberOfContours == 0)
                return false;

            endPtsOfContours = new int[numberOfContours];
            for (int i = 0; i < numberOfContours; i++)
                endPtsOfContours[i] = face.ReadUShort();

            int numPoints = endPtsOfContours[numberOfContours - 1] + 1;
            if (numPoints <= 0)
                return false;

            // A caller reading a whole font pays for the points before anything is allocated for them.
            if (budget is not null && (budget.WorkLeft -= (long)numPoints * (gvar is null ? 1 : 64)) < 0)
                return false;

            int instructionLength = face.ReadUShort();
            face.SeekOffset(instructionLength);

            // Flags (run-length encoded via the repeat bit).
            flags = new byte[numPoints];
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
            xs = new int[numPoints];
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
            ys = new int[numPoints];
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

            // A variable font moves the points: the deltas gvar gives for this location, added to the coordinates as read.
            if (gvar is not null)
            {
                int total = numPoints + 4;
                var originalX = new double[total];
                var originalY = new double[total];
                for (int i = 0; i < numPoints; i++)
                {
                    originalX[i] = xs[i];
                    originalY[i] = ys[i];
                }

                var dx = new double[total];
                var dy = new double[total];
                if (gvar.TryAddDeltas(glyphIndex, variation!.Normalized, total, originalX, originalY, endPtsOfContours, dx, dy))
                {
                    moveX = dx;
                    moveY = dy;
                }
            }

            return true;
        }

        private readonly record struct Component(int Glyph, double A, double B, double C, double D, double Dx, double Dy, bool IsOffset);

        /// <summary>Reads the components of a composite glyph, leaving the cursor after the last.</summary>
        private static List<Component> ReadComponents(OpenTypeFontface face)
        {
            var components = new List<Component>();
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

                components.Add(new Component(componentGlyph, a, b, cc, d, dx, dy, (flags & ArgsAreXyValues) != 0));

                if ((flags & MoreComponents) == 0)
                    break;
            }

            return components;
        }

        /// <summary>The offsets of a composite glyph's components moved by the deltas <c>gvar</c> gives them at a location, or the components as read when there are none.</summary>
        private static void MoveComponents(List<Component> components, int glyphIndex, GvarTable? gvar, VariationCoordinates? variation)
        {
            if (gvar is null)
                return;

            int total = components.Count + 4;
            var dx = new double[total];
            var dy = new double[total];
            if (!gvar.TryAddDeltas(glyphIndex, variation!.Normalized, total, null, null, null, dx, dy))
                return;

            for (int i = 0; i < components.Count; i++)
            {
                if (components[i].IsOffset)
                    components[i] = components[i] with { Dx = components[i].Dx + dx[i], Dy = components[i].Dy + dy[i] };
            }
        }

        private static void DecodeComposite(OpenTypeFontface face, GlyphOutline outline, int depth, int glyphIndex,
            GvarTable? gvar, VariationCoordinates? variation, Budget budget)
        {
            // Read every component before decoding any: the decoding moves the shared cursor, and a variable font needs all the
            // offsets to apply its deltas (gvar has one point for the offset of each component).
            var components = ReadComponents(face);
            MoveComponents(components, glyphIndex, gvar, variation);

            // Decode the referenced components, transform them, and append their contours. Decoding moves the shared cursor,
            // which nothing after this point needs.
            foreach (var component in components)
            {
                var child = new GlyphOutline();
                DecodeInto(face, component.Glyph, child, depth + 1, variation, budget);

                foreach (OutlineContour contour in child.ContourList)
                    outline.ContourList.Add(TransformContour(contour, component.A, component.B, component.C, component.D, component.Dx, component.Dy));
            }
        }

        private static OutlineContour TransformContour(OutlineContour source, double a, double b, double c, double d, double dx, double dy)
        {
            OutlinePoint Map(OutlinePoint p)
                => new(a * p.X + c * p.Y + dx, b * p.X + d * p.Y + dy);

            var result = new OutlineContour(Map(source.Start));
            foreach (OutlineSegment segment in source.SegmentList)
            {
                result.SegmentList.Add(segment.IsCubic
                    ? OutlineSegment.Cubic(Map(segment.Control1), Map(segment.Control2), Map(segment.End))
                    : OutlineSegment.Line(Map(segment.End)));
            }
            return result;
        }

        /// <summary>
        /// Converts one contour's raw on/off-curve points into a closed sequence of line and cubic
        /// segments, inserting implied on-curve midpoints between consecutive off-curve points and
        /// elevating each quadratic to a cubic.
        /// </summary>
        internal static OutlineContour? BuildContour(List<RawPoint> points)
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

            // The points to walk: all of them once more round, from the one after the first on-curve point and ending on it; or, when there is no
            // on-curve point, all of them and then the one that is made for the start, the midpoint of the last and the first.
            OutlinePoint startPoint;
            int total;
            if (firstOn < 0)
            {
                startPoint = Midpoint(points[0], points[n - 1]);
                total = n + 1;
            }
            else
            {
                startPoint = new OutlinePoint(points[firstOn].X, points[firstOn].Y);
                total = n;
            }

            RawPoint At(int k) => firstOn < 0
                ? (k < n ? points[k] : new RawPoint(startPoint.X, startPoint.Y, true))
                : points[(firstOn + 1 + k) % n];

            // a segment ends at each on-curve point, and at each off-curve point that follows another (where the implied on-curve point is)
            int segments = 0;
            for (int k = 0; k < total; k++)
            {
                if (At(k).OnCurve || (k > 0 && !At(k - 1).OnCurve))
                    segments++;
            }

            var contour = new OutlineContour(startPoint, segments);
            OutlinePoint current = startPoint;
            RawPoint? pendingControl = null;

            for (int k = 0; k < total; k++)
            {
                RawPoint p = At(k);
                if (p.OnCurve)
                {
                    var end = new OutlinePoint(p.X, p.Y);
                    if (pendingControl is { } ctrl)
                    {
                        contour.SegmentList.Add(QuadraticToCubic(current, ctrl, end));
                        pendingControl = null;
                    }
                    else
                    {
                        contour.SegmentList.Add(OutlineSegment.Line(end));
                    }
                    current = end;
                }
                else if (pendingControl is { } ctrl)
                {
                    // Two consecutive off-curve points: insert the implied on-curve midpoint.
                    OutlinePoint mid = Midpoint(ctrl, p);
                    contour.SegmentList.Add(QuadraticToCubic(current, ctrl, mid));
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
        internal static OutlineContour? BuildContourForTest(IReadOnlyList<(double X, double Y, bool OnCurve)> points)
        {
            var raw = new List<RawPoint>(points.Count);
            foreach ((double x, double y, bool onCurve) in points)
                raw.Add(new RawPoint(x, y, onCurve));
            return BuildContour(raw);
        }

        private static OutlineSegment QuadraticToCubic(OutlinePoint start, RawPoint control, OutlinePoint end)
        {
            // Elevate a quadratic (start, control, end) to a cubic:
            //   C1 = start + 2/3 (control - start),  C2 = end + 2/3 (control - end).
            var c1 = new OutlinePoint(
                start.X + 2.0 / 3.0 * (control.X - start.X),
                start.Y + 2.0 / 3.0 * (control.Y - start.Y));
            var c2 = new OutlinePoint(
                end.X + 2.0 / 3.0 * (control.X - end.X),
                end.Y + 2.0 / 3.0 * (control.Y - end.Y));
            return OutlineSegment.Cubic(c1, c2, end);
        }

        private static OutlinePoint Midpoint(RawPoint a, RawPoint b)
            => new((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);

        private static double ReadF2Dot14(OpenTypeFontface face) => face.ReadShort() / 16384.0;

        internal readonly record struct RawPoint(double X, double Y, bool OnCurve);
    }
}
