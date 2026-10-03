using PeachDrawing.Core;
using PeachPDF.CSS;
using PeachPDF.Html.Core.Parse;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Svg
{
    /// <summary>
    /// Resolves the basic shapes of <c>shape-inside</c> and <c>shape-subtract</c> (SVG 2 §11.7.2) into flattened polygons in user space, for the wrapped
    /// text layout to read the stretch of each line box that lies inside them. The grammar is the shared <see cref="BasicShapeGrammar"/>; percentages
    /// resolve against the viewport (the reference box of an SVG shape is the view box). <c>url()</c> references and <c>path()</c> are not resolved.
    /// </summary>
    internal static class SvgTextShapes
    {
        /// <summary>How many segments approximate a circle or an ellipse.</summary>
        private const int CurveSegments = 96;

        /// <summary>
        /// Parses a space-separated list of basic shapes. Returns null for <c>none</c>, an absent value, or when any entry is not a shape this
        /// resolves (so a bad list never half-applies).
        /// </summary>
        public static IReadOnlyList<PaintPoint[]>? Parse(string? value, double width, double height, ISvgLengthBasis? basis)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
                return null;

            using var pooledTokens = CssValueParser.GetCssTokensPooled(value);
            List<Token> tokens = pooledTokens;

            var polygons = new List<PaintPoint[]>();
            foreach (var token in tokens.Where(t => t.Type != TokenType.Whitespace))
            {
                if (token.Type != TokenType.Function || BasicShapeGrammar.TryParse([token]) is not { } shape || Resolve(shape, width, height, basis) is not { } polygon)
                    return null;

                polygons.Add(polygon);
            }

            return polygons.Count == 0 ? null : polygons;
        }

        private static PaintPoint[]? Resolve(BasicShapeGrammar.ParsedBasicShape shape, double width, double height, ISvgLengthBasis? basis)
        {
            double Len(string component, double percentBase) => SvgValueParsers.ParseLength(component, percentBase, basis) ?? 0;

            switch (shape.Kind)
            {
                case BasicShapeGrammar.BasicShapeKind.Polygon:
                    var points = shape.PolygonPoints.Select(p => new PaintPoint(Len(p.X, width), Len(p.Y, height))).ToArray();
                    return points.Length >= 3 ? points : null;

                case BasicShapeGrammar.BasicShapeKind.Inset:
                    var edges = shape.InsetEdges;
                    double top = Len(edges[0], height), right = Len(edges[1], width), bottom = Len(edges[2], height), left = Len(edges[3], width);
                    var w = width - left - right;
                    var h = height - top - bottom;
                    return w > 0 && h > 0
                        ? [new(left, top), new(left + w, top), new(left + w, top + h), new(left, top + h)]
                        : null;

                case BasicShapeGrammar.BasicShapeKind.Circle:
                {
                    double cx = Len(shape.CenterX, width), cy = Len(shape.CenterY, height);
                    var r = Radius(shape.RadiusX, cx, cy, width, height, Math.Sqrt((width * width + height * height) / 2), basis);
                    return Ellipse(cx, cy, r, r);
                }

                case BasicShapeGrammar.BasicShapeKind.Ellipse:
                {
                    double cx = Len(shape.CenterX, width), cy = Len(shape.CenterY, height);
                    var rx = AxisRadius(shape.RadiusX, cx, width, basis);
                    var ry = AxisRadius(shape.RadiusY, cy, height, basis);
                    return Ellipse(cx, cy, rx, ry);
                }

                default:
                    return null;
            }
        }

        private static double Radius(BasicShapeGrammar.ShapeRadius radius, double cx, double cy, double width, double height, double percentBase, ISvgLengthBasis? basis)
        {
            double left = cx, right = width - cx, top = cy, bottom = height - cy;
            return radius.Kind switch
            {
                BasicShapeGrammar.ShapeRadiusKind.ClosestSide => Math.Min(Math.Min(left, right), Math.Min(top, bottom)),
                BasicShapeGrammar.ShapeRadiusKind.FarthestSide => Math.Max(Math.Max(left, right), Math.Max(top, bottom)),
                _ => SvgValueParsers.ParseLength(radius.Length, percentBase, basis) ?? 0,
            };
        }

        private static double AxisRadius(BasicShapeGrammar.ShapeRadius radius, double center, double extent, ISvgLengthBasis? basis) => radius.Kind switch
        {
            BasicShapeGrammar.ShapeRadiusKind.ClosestSide => Math.Min(center, extent - center),
            BasicShapeGrammar.ShapeRadiusKind.FarthestSide => Math.Max(center, extent - center),
            _ => SvgValueParsers.ParseLength(radius.Length, extent, basis) ?? 0,
        };

        private static PaintPoint[]? Ellipse(double cx, double cy, double rx, double ry)
        {
            rx = Math.Abs(rx);
            ry = Math.Abs(ry);
            if (rx <= 0 || ry <= 0)
                return null;

            var points = new PaintPoint[CurveSegments];
            for (var i = 0; i < CurveSegments; i++)
            {
                var angle = 2 * Math.PI * i / CurveSegments;
                points[i] = new PaintPoint(cx + rx * Math.Cos(angle), cy + ry * Math.Sin(angle));
            }

            return points;
        }

        /// <summary>
        /// The stretches of the horizontal band from <paramref name="top"/> to <paramref name="bottom"/> that lie inside <paramref name="polygon"/> for
        /// the whole band: the intersection of the crossings at the band's top, middle and bottom (so a sloping edge never lets a line poke out of
        /// the shape). Sorted left to right.
        /// </summary>
        public static List<(double Left, double Right)> InteriorSpans(PaintPoint[] polygon, double top, double bottom)
        {
            var spans = Crossings(polygon, top);
            foreach (var y in new[] { (top + bottom) / 2, bottom })
                spans = Intersect(spans, Crossings(polygon, y));

            return spans;
        }

        /// <summary>The stretches of the band that <paramref name="polygon"/> covers anywhere in it: the union of the crossings at its top, middle and bottom.</summary>
        public static List<(double Left, double Right)> CoveredSpans(PaintPoint[] polygon, double top, double bottom)
        {
            var all = new List<(double, double)>();
            foreach (var y in new[] { top, (top + bottom) / 2, bottom })
                all.AddRange(Crossings(polygon, y));

            all.Sort();
            var merged = new List<(double Left, double Right)>();
            foreach (var (left, right) in all)
            {
                if (merged.Count > 0 && left <= merged[^1].Right)
                    merged[^1] = (merged[^1].Left, Math.Max(merged[^1].Right, right));
                else
                    merged.Add((left, right));
            }

            return merged;
        }

        /// <summary>The <paramref name="from"/> spans with every <paramref name="cut"/> span removed.</summary>
        public static List<(double Left, double Right)> Subtract(List<(double Left, double Right)> from, List<(double Left, double Right)> cut)
        {
            var result = new List<(double Left, double Right)>();
            foreach (var (left, right) in from)
            {
                var start = left;
                foreach (var (cutLeft, cutRight) in cut)
                {
                    if (cutRight <= start || cutLeft >= right)
                        continue;

                    if (cutLeft > start)
                        result.Add((start, cutLeft));

                    start = Math.Max(start, cutRight);
                }

                if (start < right)
                    result.Add((start, right));
            }

            return result;
        }

        /// <summary>Where the horizontal line at <paramref name="y"/> crosses the polygon: the even-odd inside stretches, left to right.</summary>
        private static List<(double Left, double Right)> Crossings(PaintPoint[] polygon, double y)
        {
            var xs = new List<double>();
            for (var i = 0; i < polygon.Length; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Length];
                if ((a.Y <= y && b.Y > y) || (b.Y <= y && a.Y > y))
                    xs.Add(a.X + (y - a.Y) / (b.Y - a.Y) * (b.X - a.X));
            }

            xs.Sort();
            var spans = new List<(double Left, double Right)>();
            for (var i = 0; i + 1 < xs.Count; i += 2)
                spans.Add((xs[i], xs[i + 1]));

            return spans;
        }

        private static List<(double Left, double Right)> Intersect(List<(double Left, double Right)> a, List<(double Left, double Right)> b)
        {
            var result = new List<(double Left, double Right)>();
            foreach (var (al, ar) in a)
            {
                foreach (var (bl, br) in b)
                {
                    var left = Math.Max(al, bl);
                    var right = Math.Min(ar, br);
                    if (right > left)
                        result.Add((left, right));
                }
            }

            result.Sort();
            return result;
        }
    }
}
