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

using System.Collections.Generic;

namespace PeachPDF.Svg
{
    /// <summary>
    /// Converts a basic-shape element (<c>rect</c>/<c>line</c>/<c>polyline</c>/<c>polygon</c>/
    /// <c>circle</c>/<c>ellipse</c>) into the normalized <see cref="PathSegment"/> geometry a
    /// <c>&lt;textPath&gt;</c> lays glyphs along, per SVG 2's "the referenced element may be any basic
    /// shape" allowance. Returns null for an unsupported element or one missing its geometry (the
    /// caller then leaves the run on the straight baseline). A <c>rect</c>'s <c>rx</c>/<c>ry</c> rounded
    /// corners are ignored (the text path uses the sharp-cornered outline), matching the same
    /// simplification the shape renderer applies elsewhere for textPath purposes.
    /// </summary>
    internal static class SvgShapeToPath
    {
        public static IReadOnlyList<PathSegment>? Convert(ISvgSourceNode node) => node.Name switch
        {
            "rect" => Rect(node),
            "line" => Line(node),
            "polyline" => Poly(node, close: false),
            "polygon" => Poly(node, close: true),
            "circle" => Circle(node),
            "ellipse" => Ellipse(node),
            _ => null,
        };

        private static double Attr(ISvgSourceNode node, string name) =>
            SvgValueParsers.ParseLength(node.GetAttribute(name)) ?? 0;

        private static IReadOnlyList<PathSegment>? Rect(ISvgSourceNode node)
        {
            var width = Attr(node, "width");
            var height = Attr(node, "height");
            if (width <= 0 || height <= 0)
                return null;

            var x = Attr(node, "x");
            var y = Attr(node, "y");

            return
            [
                PathSegment.MoveTo(x, y),
                PathSegment.LineTo(x + width, y),
                PathSegment.LineTo(x + width, y + height),
                PathSegment.LineTo(x, y + height),
                PathSegment.ClosePath(),
            ];
        }

        private static IReadOnlyList<PathSegment> Line(ISvgSourceNode node) =>
        [
            PathSegment.MoveTo(Attr(node, "x1"), Attr(node, "y1")),
            PathSegment.LineTo(Attr(node, "x2"), Attr(node, "y2")),
        ];

        private static IReadOnlyList<PathSegment>? Poly(ISvgSourceNode node, bool close)
        {
            var points = SvgPointsParser.Parse(node.GetAttribute("points"));
            if (points.Length == 0)
                return null;

            var segments = new List<PathSegment> { PathSegment.MoveTo(points[0].X, points[0].Y) };
            for (var i = 1; i < points.Length; i++)
                segments.Add(PathSegment.LineTo(points[i].X, points[i].Y));
            if (close)
                segments.Add(PathSegment.ClosePath());

            return segments;
        }

        private static IReadOnlyList<PathSegment>? Circle(ISvgSourceNode node)
        {
            var r = Attr(node, "r");
            return r <= 0 ? null : EllipsePath(Attr(node, "cx"), Attr(node, "cy"), r, r);
        }

        private static IReadOnlyList<PathSegment>? Ellipse(ISvgSourceNode node)
        {
            var rx = Attr(node, "rx");
            var ry = Attr(node, "ry");
            return rx <= 0 || ry <= 0 ? null : EllipsePath(Attr(node, "cx"), Attr(node, "cy"), rx, ry);
        }

        // Two 180° arcs (starting at the right vertex, sweeping clockwise) - the same decomposition the
        // path grammar produces, which SvgTextPathGeometry already flattens.
        private static IReadOnlyList<PathSegment> EllipsePath(double cx, double cy, double rx, double ry) =>
        [
            PathSegment.MoveTo(cx + rx, cy),
            PathSegment.ArcTo(rx, ry, 0, isLargeArc: false, sweepClockwise: true, cx - rx, cy),
            PathSegment.ArcTo(rx, ry, 0, isLargeArc: false, sweepClockwise: true, cx + rx, cy),
            PathSegment.ClosePath(),
        ];
    }
}
