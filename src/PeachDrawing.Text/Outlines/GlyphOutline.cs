using PeachDrawing.Text.Internal.Fonts.OpenType;
using System.Collections.Generic;

namespace PeachDrawing.Text.Outlines
{
    /// <summary>A point in font design units, with the y axis pointing up.</summary>
    /// <param name="X">The distance to the right of the glyph origin.</param>
    /// <param name="Y">The distance above the baseline.</param>
    public readonly record struct OutlinePoint(double X, double Y);

    /// <summary>One piece of a contour: a straight line, or a cubic Bezier curve.</summary>
    /// <remarks>
    /// TrueType outlines are made of quadratic curves. They are raised to cubic ones on the way out, so a consumer only has two
    /// kinds of segment to draw.
    /// </remarks>
    public readonly struct OutlineSegment
    {
        private OutlineSegment(bool isCubic, OutlinePoint control1, OutlinePoint control2, OutlinePoint end)
        {
            IsCubic = isCubic;
            Control1 = control1;
            Control2 = control2;
            End = end;
        }

        /// <summary>Whether the segment is a cubic Bezier curve and not a straight line.</summary>
        public bool IsCubic { get; }

        /// <summary>The first control point of a cubic curve; zero for a line.</summary>
        public OutlinePoint Control1 { get; }

        /// <summary>The second control point of a cubic curve; zero for a line.</summary>
        public OutlinePoint Control2 { get; }

        /// <summary>The point the segment ends at, which is where the next segment starts.</summary>
        public OutlinePoint End { get; }

        /// <summary>Creates a straight line to a point.</summary>
        /// <param name="end">The end of the line.</param>
        public static OutlineSegment Line(OutlinePoint end) => new(false, default, default, end);

        /// <summary>Creates a cubic Bezier curve.</summary>
        /// <param name="control1">The first control point.</param>
        /// <param name="control2">The second control point.</param>
        /// <param name="end">The end of the curve.</param>
        public static OutlineSegment Cubic(OutlinePoint control1, OutlinePoint control2, OutlinePoint end)
            => new(true, control1, control2, end);
    }

    /// <summary>A closed contour of a glyph: a start point and the segments that lead round to it again.</summary>
    public sealed class OutlineContour
    {
        internal OutlineContour(OutlinePoint start)
        {
            Start = start;
        }

        /// <summary>Where the contour begins.</summary>
        public OutlinePoint Start { get; }

        /// <summary>The segments of the contour, in order.</summary>
        public IReadOnlyList<OutlineSegment> Segments => SegmentList;

        internal List<OutlineSegment> SegmentList { get; } = [];
    }

    /// <summary>
    /// The shape of one glyph as data: zero or more closed contours of lines and cubic curves, in font design units with the
    /// y axis up.
    /// </summary>
    /// <remarks>
    /// A glyph is filled by the nonzero winding rule, which is how a glyph gets its counters (the hole of an <c>o</c>): a
    /// contour that winds the other way subtracts. An empty outline, such as the one of a space, has no contours.
    /// </remarks>
    public sealed class GlyphOutline
    {
        internal GlyphOutline()
        {
        }

        /// <summary>The contours of the glyph.</summary>
        public IReadOnlyList<OutlineContour> Contours => ContourList;

        internal List<OutlineContour> ContourList { get; } = [];

        /// <summary>Whether the glyph has no ink at all.</summary>
        public bool IsEmpty => ContourList.Count == 0;

        /// <summary>
        /// Finds where the glyph's ink lies across a horizontal band: the horizontal ranges that are painted somewhere between
        /// two heights. It is what CSS <c>text-decoration-skip-ink</c> needs to interrupt a line where it would run through a
        /// descender.
        /// </summary>
        /// <remarks>
        /// The outline is flattened and sampled with a handful of scanlines across the band, each resolved by the nonzero winding
        /// rule the glyph is filled with, so the counter of an <c>o</c> comes back as a gap between ranges and not as ink. A
        /// sampled result can miss a sliver thinner than the spacing of the scanlines, which costs a slightly short gap in a
        /// decoration and never a wrong one.
        /// </remarks>
        /// <param name="bandLow">The lower edge of the band, in design units, y up.</param>
        /// <param name="bandHigh">The upper edge of the band; a band that is not above <paramref name="bandLow"/> yields nothing.</param>
        /// <returns>The painted ranges, left to right and merged.</returns>
        public IReadOnlyList<(double Start, double End)> Crossings(double bandLow, double bandHigh)
            => GlyphInkScanner.Crossings(this, bandLow, bandHigh);
    }
}
