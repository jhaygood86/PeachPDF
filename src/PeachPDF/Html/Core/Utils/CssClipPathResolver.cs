using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
using System;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Layer B of the <c>clip-path</c> pipeline: resolves a <c>clip-path</c> value (already validated and
    /// preserved verbatim by <see cref="ClipPathValueConverter"/> at parse time) into an absolute-coordinate
    /// <see cref="RGraphicsPath"/> that the paint hook can push as a clip region. The grammar itself is parsed
    /// by the shared <see cref="BasicShapeGrammar"/>; only the final numeric resolution against the element's
    /// reference box lives here.
    /// </summary>
    internal static class CssClipPathResolver
    {
        /// <summary>
        /// Builds the clip path for <paramref name="value"/> against <paramref name="referenceBox"/> (the
        /// absolute border-box rectangle, in paint coordinates).
        /// </summary>
        /// <returns>
        /// <c>true</c> with <paramref name="path"/> and <paramref name="useEvenOdd"/> populated when
        /// <paramref name="value"/> is a renderable basic shape; <c>false</c> (and a null path) for
        /// <c>none</c>/invalid, in which case the caller skips clipping.
        /// </returns>
        public static bool TryBuildClipPath(RGraphics g, string value, RRect referenceBox, CssBoxProperties box, out RGraphicsPath? path, out bool useEvenOdd)
        {
            path = null;
            useEvenOdd = false;

            if (string.IsNullOrWhiteSpace(value)) return false;

            var tokens = CssValueParser.GetCssTokens(value);
            var shape = BasicShapeGrammar.TryParse(tokens);

            if (shape is null) return false;

            path = g.GetGraphicsPath();

            switch (shape.Kind)
            {
                case BasicShapeGrammar.BasicShapeKind.Polygon:
                    useEvenOdd = shape.PolygonFillRule == BasicShapeGrammar.FillRule.Evenodd;
                    BuildPolygon(path, shape, referenceBox, box);
                    break;
                case BasicShapeGrammar.BasicShapeKind.Inset:
                    BuildInset(path, shape, referenceBox, box);
                    break;
                case BasicShapeGrammar.BasicShapeKind.Circle:
                    BuildCircle(path, shape, referenceBox, box);
                    break;
                case BasicShapeGrammar.BasicShapeKind.Ellipse:
                    BuildEllipse(path, shape, referenceBox, box);
                    break;
                default:
                    path.Dispose();
                    path = null;
                    return false;
            }

            path.FillMode = useEvenOdd ? RFillMode.EvenOdd : RFillMode.Nonzero;
            return true;
        }

        private static void BuildPolygon(RGraphicsPath path, BasicShapeGrammar.ParsedBasicShape shape, RRect referenceBox, CssBoxProperties box)
        {
            var points = shape.PolygonPoints;

            for (var i = 0; i < points.Count; i++)
            {
                var x = referenceBox.X + CssValueParser.ParseLength(points[i].X, referenceBox.Width, box);
                var y = referenceBox.Y + CssValueParser.ParseLength(points[i].Y, referenceBox.Height, box);

                if (i == 0)
                    path.Start(x, y);
                else
                    path.LineTo(x, y);
            }

            path.CloseFigure();
        }

        private static void BuildInset(RGraphicsPath path, BasicShapeGrammar.ParsedBasicShape shape, RRect referenceBox, CssBoxProperties box)
        {
            var edges = shape.InsetEdges;
            var top = CssValueParser.ParseLength(edges[0], referenceBox.Height, box);
            var right = CssValueParser.ParseLength(edges[1], referenceBox.Width, box);
            var bottom = CssValueParser.ParseLength(edges[2], referenceBox.Height, box);
            var left = CssValueParser.ParseLength(edges[3], referenceBox.Width, box);

            var x0 = referenceBox.X + left;
            var y0 = referenceBox.Y + top;
            var x1 = referenceBox.Right - right;
            var y1 = referenceBox.Bottom - bottom;

            // A rounded inset (inset(... round <radius>)) is captured by the grammar but rendered as a
            // plain rectangle here - the corner radius is not applied (documented limitation).
            path.Start(x0, y0);
            path.LineTo(x1, y0);
            path.LineTo(x1, y1);
            path.LineTo(x0, y1);
            path.CloseFigure();
        }

        private static void BuildCircle(RGraphicsPath path, BasicShapeGrammar.ParsedBasicShape shape, RRect referenceBox, CssBoxProperties box)
        {
            var cx = referenceBox.X + CssValueParser.ParseLength(shape.CenterX, referenceBox.Width, box);
            var cy = referenceBox.Y + CssValueParser.ParseLength(shape.CenterY, referenceBox.Height, box);

            var r = ResolveCircleRadius(shape.RadiusX, cx, cy, referenceBox, box);

            AppendEllipse(path, cx, cy, r, r);
        }

        private static void BuildEllipse(RGraphicsPath path, BasicShapeGrammar.ParsedBasicShape shape, RRect referenceBox, CssBoxProperties box)
        {
            var cx = referenceBox.X + CssValueParser.ParseLength(shape.CenterX, referenceBox.Width, box);
            var cy = referenceBox.Y + CssValueParser.ParseLength(shape.CenterY, referenceBox.Height, box);

            var rx = ResolveAxisRadius(shape.RadiusX, cx - referenceBox.X, referenceBox.Width, referenceBox.Width, box);
            var ry = ResolveAxisRadius(shape.RadiusY, cy - referenceBox.Y, referenceBox.Height, referenceBox.Height, box);

            AppendEllipse(path, cx, cy, rx, ry);
        }

        /// <summary>
        /// Resolves a circle <c>&lt;shape-radius&gt;</c>. A percentage resolves against
        /// <c>sqrt(w² + h²)/sqrt(2)</c> (CSS Shapes Level 1); <c>closest-side</c>/<c>farthest-side</c>
        /// use the min/max distance from the center to the four edges.
        /// </summary>
        private static double ResolveCircleRadius(BasicShapeGrammar.ShapeRadius radius, double cx, double cy, RRect referenceBox, CssBoxProperties box)
        {
            var left = cx - referenceBox.X;
            var right = referenceBox.Right - cx;
            var top = cy - referenceBox.Y;
            var bottom = referenceBox.Bottom - cy;

            return radius.Kind switch
            {
                BasicShapeGrammar.ShapeRadiusKind.ClosestSide => Min4(left, right, top, bottom),
                BasicShapeGrammar.ShapeRadiusKind.FarthestSide => Max4(left, right, top, bottom),
                _ => CssValueParser.ParseLength(radius.Length,
                        Math.Sqrt(referenceBox.Width * referenceBox.Width + referenceBox.Height * referenceBox.Height) / Math.Sqrt(2), box),
            };
        }

        /// <summary>
        /// Resolves one axis of an ellipse <c>&lt;shape-radius&gt;</c>. A length-percentage resolves against
        /// that axis' extent (<paramref name="axisExtent"/>); <c>closest-side</c>/<c>farthest-side</c> use the
        /// min/max of the two per-axis center-to-edge distances (<paramref name="centerToStart"/> and
        /// <c>axisExtent - centerToStart</c>).
        /// </summary>
        private static double ResolveAxisRadius(BasicShapeGrammar.ShapeRadius radius, double centerToStart, double axisExtent, double hundredPercent, CssBoxProperties box)
        {
            var toStart = centerToStart;
            var toEnd = axisExtent - centerToStart;

            return radius.Kind switch
            {
                BasicShapeGrammar.ShapeRadiusKind.ClosestSide => Math.Min(toStart, toEnd),
                BasicShapeGrammar.ShapeRadiusKind.FarthestSide => Math.Max(toStart, toEnd),
                _ => CssValueParser.ParseLength(radius.Length, hundredPercent, box),
            };
        }

        /// <summary>Builds a full ellipse (or circle when rx == ry) as four quarter-arc segments, the same
        /// technique <c>SvgRenderer.AppendEllipseGeometry</c> uses.</summary>
        private static void AppendEllipse(RGraphicsPath path, double cx, double cy, double rx, double ry)
        {
            rx = Math.Abs(rx);
            ry = Math.Abs(ry);

            if (rx <= 0 || ry <= 0)
                return;

            path.AddMove(cx + rx, cy);
            path.AddArc(cx, cy + ry, rx, ry, 0, false, true);
            path.AddArc(cx - rx, cy, rx, ry, 0, false, true);
            path.AddArc(cx, cy - ry, rx, ry, 0, false, true);
            path.AddArc(cx + rx, cy, rx, ry, 0, false, true);
            path.CloseFigure();
        }

        private static double Min4(double a, double b, double c, double d) => Math.Min(Math.Min(a, b), Math.Min(c, d));
        private static double Max4(double a, double b, double c, double d) => Math.Max(Math.Max(a, b), Math.Max(c, d));
    }
}
