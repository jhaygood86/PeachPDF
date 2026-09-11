using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Svg;
using System;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Layer B of the <c>clip-path</c> pipeline: resolves a <c>clip-path</c> value (already validated and
    /// preserved verbatim by <see cref="ClipPathValueConverter"/> at parse time) into an absolute-coordinate
    /// <see cref="RGraphicsPath"/> that the paint hook can push as a clip region. The grammar itself is parsed
    /// by the shared <see cref="BasicShapeGrammar"/>; only the final numeric resolution against the element's
    /// reference box - selected by an optional <c>&lt;geometry-box&gt;</c> keyword, defaulting to the
    /// border-box - lives here.
    /// </summary>
    internal static class CssClipPathResolver
    {
        /// <summary>
        /// Builds the clip path for <paramref name="value"/> against <paramref name="borderBox"/> (the
        /// absolute border-box rectangle, in paint coordinates) - or whichever other box a
        /// <c>&lt;geometry-box&gt;</c> keyword in <paramref name="value"/> selects instead.
        /// </summary>
        /// <returns>
        /// <c>true</c> with <paramref name="path"/> and <paramref name="useEvenOdd"/> populated when
        /// <paramref name="value"/> is a renderable clip source; <c>false</c> (and a null path) for
        /// <c>none</c>/invalid/an unresolvable <c>url()</c>, in which case the caller skips clipping.
        /// </returns>
        public static bool TryBuildClipPath(RGraphics g, string value, RRect borderBox, CssBox box, out RGraphicsPath? path, out bool useEvenOdd)
        {
            path = null;
            useEvenOdd = false;

            if (string.IsNullOrWhiteSpace(value)) return false;

            using var pooledTokens = CssValueParser.GetCssTokensPooled(value);
            List<Token> tokens = pooledTokens;
            var shape = BasicShapeGrammar.TryParse(tokens);

            if (shape is null) return false;

            // Every coordinate below is resolved against the reference box/box in raw layout-space (the same
            // PixelsPerInch-inflated space CssBox geometry lives in, needed so CssValueParser.ParseLength's
            // own absolute-length PixelsPerPoint catch-up multiply - issue #814 - and the percentage basis
            // stay consistent with each other). Like RenderUtils.GetRoundRect/BordersDrawHandler.GetRoundedBorderPath
            // (issue #812), the path itself has no ambient transform to divide it back down, so every final
            // coordinate is divided by g.PixelsPerPoint right before reaching the path.
            var ppp = g.PixelsPerPoint;

            // url() is its own top-level clip-source alternative (never combined with a <geometry-box>,
            // see BasicShapeGrammar.TryParse's own remarks) and builds its path through a different
            // mechanism entirely (SvgRenderer.BuildClipPath), so it branches before the geometry-box
            // resolution every other shape kind shares below.
            if (shape.Kind == BasicShapeGrammar.BasicShapeKind.Url)
            {
                path = BuildUrl(g, shape, borderBox, box, ppp, out useEvenOdd);
                if (path is null) return false;

                path.FillMode = useEvenOdd ? RFillMode.EvenOdd : RFillMode.Nonzero;
                return true;
            }

            var referenceBox = ReferenceBoxFor(shape.GeometryBox, borderBox, box);

            path = g.GetGraphicsPath();

            switch (shape.Kind)
            {
                case BasicShapeGrammar.BasicShapeKind.None:
                    BuildNone(g, path, shape.GeometryBox, borderBox, referenceBox, box, ppp);
                    break;
                case BasicShapeGrammar.BasicShapeKind.Polygon:
                    useEvenOdd = shape.PolygonFillRule == BasicShapeGrammar.FillRule.EvenOdd;
                    BuildPolygon(path, shape, referenceBox, box, ppp);
                    break;
                case BasicShapeGrammar.BasicShapeKind.Inset:
                    BuildInset(g, path, shape, referenceBox, box, ppp);
                    break;
                case BasicShapeGrammar.BasicShapeKind.Circle:
                    BuildCircle(path, shape, referenceBox, box, ppp);
                    break;
                case BasicShapeGrammar.BasicShapeKind.Ellipse:
                    BuildEllipse(path, shape, referenceBox, box, ppp);
                    break;
                case BasicShapeGrammar.BasicShapeKind.Path:
                    useEvenOdd = shape.PathFillRule == BasicShapeGrammar.FillRule.EvenOdd;
                    BuildPath(path, shape, referenceBox, ppp);
                    break;
                default:
                    path.Dispose();
                    path = null;
                    return false;
            }

            path.FillMode = useEvenOdd ? RFillMode.EvenOdd : RFillMode.Nonzero;
            return true;
        }

        /// <summary>
        /// Resolves the <c>&lt;geometry-box&gt;</c> keyword (CSS Masking Level 1 §6.1) a basic shape
        /// selects, against <paramref name="borderBox"/>. <c>fill-box</c>/<c>stroke-box</c>/<c>view-box</c>
        /// alias the border-box - not a simplification, but CSS Masking 1 §7's own specified fallback for
        /// an element with no associated SVG bounding box, which every <see cref="CssBox"/> is.
        /// </summary>
        private static RRect ReferenceBoxFor(BasicShapeGrammar.GeometryBoxKind kind, RRect borderBox, CssBox box) => kind switch
        {
            BasicShapeGrammar.GeometryBoxKind.ContentBox => new RRect(
                borderBox.X + box.ActualBorderLeftWidth + box.ActualPaddingLeft,
                borderBox.Y + box.ActualBorderTopWidth + box.ActualPaddingTop,
                Math.Max(0, borderBox.Width - box.ActualBorderLeftWidth - box.ActualBorderRightWidth - box.ActualPaddingLeft - box.ActualPaddingRight),
                Math.Max(0, borderBox.Height - box.ActualBorderTopWidth - box.ActualBorderBottomWidth - box.ActualPaddingTop - box.ActualPaddingBottom)),
            BasicShapeGrammar.GeometryBoxKind.PaddingBox => new RRect(
                borderBox.X + box.ActualBorderLeftWidth,
                borderBox.Y + box.ActualBorderTopWidth,
                Math.Max(0, borderBox.Width - box.ActualBorderLeftWidth - box.ActualBorderRightWidth),
                Math.Max(0, borderBox.Height - box.ActualBorderTopWidth - box.ActualBorderBottomWidth)),
            BasicShapeGrammar.GeometryBoxKind.MarginBox => new RRect(
                borderBox.X - box.ActualMarginLeft,
                borderBox.Y - box.ActualMarginTop,
                borderBox.Width + box.ActualMarginLeft + box.ActualMarginRight,
                borderBox.Height + box.ActualMarginTop + box.ActualMarginBottom),
            // BorderBox, FillBox, StrokeBox, ViewBox.
            _ => borderBox,
        };

        /// <summary>
        /// A bare <c>&lt;geometry-box&gt;</c> with no additional shape function clips to that box's own
        /// shape - which, per CSS Backgrounds and Borders' corner-clipping rules, includes the box's own
        /// declared <c>border-radius</c> for border-box (reduced by the border/padding width for
        /// padding-box/content-box, the same as <c>background-clip</c> already does via
        /// <see cref="CssBox.ComputeInnerRadii"/>). <c>margin-box</c> has no CSS-defined radius growth
        /// outward, so it stays a plain rectangle - a narrow, documented simplification for a rare
        /// authoring pattern (a bare geometry-box with no shape at all).
        /// </summary>
        private static void BuildNone(RGraphics g, RGraphicsPath path, BasicShapeGrammar.GeometryBoxKind geometryBox,
            RRect borderBox, RRect referenceBox, CssBox box, double ppp)
        {
            var radii = geometryBox switch
            {
                BasicShapeGrammar.GeometryBoxKind.ContentBox => box.ComputeInnerRadii(borderBox, referenceBox,
                    box.ActualBorderLeftWidth + box.ActualPaddingLeft, box.ActualBorderTopWidth + box.ActualPaddingTop,
                    box.ActualBorderRightWidth + box.ActualPaddingRight, box.ActualBorderBottomWidth + box.ActualPaddingBottom),
                BasicShapeGrammar.GeometryBoxKind.PaddingBox => box.ComputeInnerRadii(borderBox, referenceBox,
                    box.ActualBorderLeftWidth, box.ActualBorderTopWidth, box.ActualBorderRightWidth, box.ActualBorderBottomWidth),
                BasicShapeGrammar.GeometryBoxKind.MarginBox => default,
                // BorderBox, FillBox, StrokeBox, ViewBox: the box's own declared radius, unreduced.
                _ => box.ComputeRadii(borderBox),
            };

            if (radii.IsRounded)
            {
                using var rounded = RenderUtils.GetRoundRect(g, referenceBox,
                    radii.TLX, radii.TLY, radii.TRX, radii.TRY, radii.BRX, radii.BRY, radii.BLX, radii.BLY);
                path.AddPath(rounded);
                return;
            }

            DrawRect(path, referenceBox, ppp);
        }

        /// <summary>Appends a plain axis-aligned rectangle, manually divided by <c>PixelsPerPoint</c> per
        /// this resolver's own ppp-division discipline (see <see cref="TryBuildClipPath"/>'s remarks) -
        /// shared by <see cref="BuildNone"/> (no rounding) and <see cref="BuildInset"/> (no <c>round</c>
        /// clause).</summary>
        private static void DrawRect(RGraphicsPath path, RRect rect, double ppp)
        {
            path.Start(rect.Left / ppp, rect.Top / ppp);
            path.LineTo(rect.Right / ppp, rect.Top / ppp);
            path.LineTo(rect.Right / ppp, rect.Bottom / ppp);
            path.LineTo(rect.Left / ppp, rect.Bottom / ppp);
            path.CloseFigure();
        }

        private static void BuildPolygon(RGraphicsPath path, BasicShapeGrammar.ParsedBasicShape shape, RRect referenceBox, CssBox box, double ppp)
        {
            var points = shape.PolygonPoints;

            for (var i = 0; i < points.Count; i++)
            {
                var x = (referenceBox.X + CssValueParser.ParseLength(points[i].X, referenceBox.Width, box)) / ppp;
                var y = (referenceBox.Y + CssValueParser.ParseLength(points[i].Y, referenceBox.Height, box)) / ppp;

                if (i == 0)
                    path.Start(x, y);
                else
                    path.LineTo(x, y);
            }

            path.CloseFigure();
        }

        private static void BuildInset(RGraphics g, RGraphicsPath path, BasicShapeGrammar.ParsedBasicShape shape, RRect referenceBox, CssBox box, double ppp)
        {
            var edges = shape.InsetEdges;
            var top = CssValueParser.ParseLength(edges[0], referenceBox.Height, box);
            var right = CssValueParser.ParseLength(edges[1], referenceBox.Width, box);
            var bottom = CssValueParser.ParseLength(edges[2], referenceBox.Height, box);
            var left = CssValueParser.ParseLength(edges[3], referenceBox.Width, box);

            // CSS Shapes Level 1 §3.1: offsets that overconstrain a dimension (their sum exceeds the
            // reference box's own extent) are proportionally reduced so the sum exactly matches it,
            // rather than producing an inverted/negative-size rectangle.
            (left, right) = ReduceOverconstrained(left, right, referenceBox.Width);
            (top, bottom) = ReduceOverconstrained(top, bottom, referenceBox.Height);

            var insetRect = new RRect(
                referenceBox.X + left,
                referenceBox.Y + top,
                Math.Max(0, referenceBox.Width - left - right),
                Math.Max(0, referenceBox.Height - top - bottom));

            if (shape.InsetRoundRadii is { } radii)
            {
                // Percentages in an inset()'s own round radius resolve against the inset rectangle
                // itself (CSS Shapes Level 1 §7.1) - not the outer reference box - the same way a box's
                // own border-radius percentages resolve against that box's own dimensions.
                var tlX = CssValueParser.ParseLength(radii.TLX, insetRect.Width, box);
                var tlY = CssValueParser.ParseLength(radii.TLY, insetRect.Height, box);
                var trX = CssValueParser.ParseLength(radii.TRX, insetRect.Width, box);
                var trY = CssValueParser.ParseLength(radii.TRY, insetRect.Height, box);
                var brX = CssValueParser.ParseLength(radii.BRX, insetRect.Width, box);
                var brY = CssValueParser.ParseLength(radii.BRY, insetRect.Height, box);
                var blX = CssValueParser.ParseLength(radii.BLX, insetRect.Width, box);
                var blY = CssValueParser.ParseLength(radii.BLY, insetRect.Height, box);

                // Same CSS Backgrounds 3 §4 corner-overlap reduction a box's own border-radius uses,
                // reused directly rather than re-derived (DerivedStyle.ApplyCornerOverlap).
                var reduced = DerivedStyle.ApplyCornerOverlap(insetRect, tlX, tlY, trX, trY, brX, brY, blX, blY);

                using var rounded = RenderUtils.GetRoundRect(g, insetRect,
                    reduced.TLX, reduced.TLY, reduced.TRX, reduced.TRY,
                    reduced.BRX, reduced.BRY, reduced.BLX, reduced.BLY);
                path.AddPath(rounded);
                return;
            }

            DrawRect(path, insetRect, ppp);
        }

        /// <summary>CSS Shapes Level 1 §3.1's proportional-reduction rule, applied once per axis by
        /// <see cref="BuildInset"/>: if the pair sums past <paramref name="extent"/>, both are scaled
        /// down by the same factor so the sum exactly equals it, preserving their ratio.</summary>
        private static (double, double) ReduceOverconstrained(double a, double b, double extent)
        {
            if (a + b <= extent || a + b <= 0) return (a, b);

            var factor = extent / (a + b);
            return (a * factor, b * factor);
        }

        private static void BuildCircle(RGraphicsPath path, BasicShapeGrammar.ParsedBasicShape shape, RRect referenceBox, CssBox box, double ppp)
        {
            var cx = referenceBox.X + CssValueParser.ParseLength(shape.CenterX, referenceBox.Width, box);
            var cy = referenceBox.Y + CssValueParser.ParseLength(shape.CenterY, referenceBox.Height, box);

            var r = ResolveCircleRadius(shape.RadiusX, cx, cy, referenceBox, box);

            AppendEllipse(path, cx / ppp, cy / ppp, r / ppp, r / ppp);
        }

        private static void BuildEllipse(RGraphicsPath path, BasicShapeGrammar.ParsedBasicShape shape, RRect referenceBox, CssBox box, double ppp)
        {
            var cx = referenceBox.X + CssValueParser.ParseLength(shape.CenterX, referenceBox.Width, box);
            var cy = referenceBox.Y + CssValueParser.ParseLength(shape.CenterY, referenceBox.Height, box);

            var rx = ResolveAxisRadius(shape.RadiusX, cx - referenceBox.X, referenceBox.Width, referenceBox.Width, box);
            var ry = ResolveAxisRadius(shape.RadiusY, cy - referenceBox.Y, referenceBox.Height, referenceBox.Height, box);

            AppendEllipse(path, cx / ppp, cy / ppp, rx / ppp, ry / ppp);
        }

        /// <summary>
        /// Resolves <c>path()</c>'s already-parsed <see cref="PathSegment"/>s (produced up front by
        /// <see cref="BasicShapeGrammar.ParsePath"/> - no re-parsing of path data happens here) into
        /// <paramref name="path"/>. Per CSS Shapes Level 1, a path-data coordinate is a unitless number
        /// interpreted as a CSS pixel, with the path's own (0,0) origin coincident with the reference
        /// box's top-left corner - unlike every other basic shape, there is no percentage/axis scaling
        /// against <paramref name="referenceBox"/>'s width/height, just a translation.
        /// </summary>
        private static void BuildPath(RGraphicsPath path, BasicShapeGrammar.ParsedBasicShape shape, RRect referenceBox, double ppp)
        {
            const double px = Length.PointsPerPx;

            foreach (var segment in shape.PathSegments)
            {
                switch (segment.Kind)
                {
                    case PathSegmentKind.MoveTo:
                        path.AddMove((referenceBox.X + segment.X * px) / ppp, (referenceBox.Y + segment.Y * px) / ppp);
                        break;
                    case PathSegmentKind.LineTo:
                        path.LineTo((referenceBox.X + segment.X * px) / ppp, (referenceBox.Y + segment.Y * px) / ppp);
                        break;
                    case PathSegmentKind.CubicBezierTo:
                        path.AddBezierTo(
                            (referenceBox.X + segment.X1 * px) / ppp, (referenceBox.Y + segment.Y1 * px) / ppp,
                            (referenceBox.X + segment.X2 * px) / ppp, (referenceBox.Y + segment.Y2 * px) / ppp,
                            (referenceBox.X + segment.X * px) / ppp, (referenceBox.Y + segment.Y * px) / ppp);
                        break;
                    case PathSegmentKind.ArcTo:
                        path.AddArc(
                            (referenceBox.X + segment.X * px) / ppp, (referenceBox.Y + segment.Y * px) / ppp,
                            segment.RadiusX * px / ppp, segment.RadiusY * px / ppp,
                            segment.RotationAngle, segment.IsLargeArc, segment.SweepClockwise);
                        break;
                    case PathSegmentKind.ClosePath:
                        path.CloseFigure();
                        break;
                }
            }
        }

        /// <summary>
        /// Resolves a <c>url(#id)</c> clip source: looks up the referenced <c>&lt;clipPath&gt;</c> in the
        /// document-wide <see cref="SvgClipPathRegistry"/> and builds its geometry via
        /// <see cref="SvgRenderer.BuildClipPath"/>, mapped from the clipPath's own coordinate system into
        /// this element's reference box - <c>userSpaceOnUse</c> (the default) with the same "unitless
        /// number = px, origin at the reference box's top-left" convention <see cref="BuildPath"/> already
        /// uses for <c>path()</c>; <c>objectBoundingBox</c> the same way <see cref="SvgRenderer.RenderElement"/>
        /// maps a clipPath's <c>0..1</c> child geometry onto an SVG element's own bounding box, using this
        /// reference box in that role. Returns null (skip clipping) for an unresolvable id, the same
        /// "invalid → no clip" contract as every other unrenderable value.
        /// </summary>
        private static RGraphicsPath? BuildUrl(RGraphics g, BasicShapeGrammar.ParsedBasicShape shape, RRect referenceBox, CssBox box, double ppp, out bool useEvenOdd)
        {
            useEvenOdd = false;

            if (!box.HtmlContainer!.SvgClipPaths.TryGet(shape.UrlId, out var clipDefinition) || clipDefinition is null)
                return null;

            const double px = Length.PointsPerPx;

            var unitsMatrix = clipDefinition.ClipPathUnitsUserSpaceOnUse
                ? new RMatrix(px / ppp, 0, 0, px / ppp, referenceBox.X / ppp, referenceBox.Y / ppp)
                : new RMatrix(referenceBox.Width / ppp, 0, 0, referenceBox.Height / ppp, referenceBox.X / ppp, referenceBox.Y / ppp);

            var path = SvgRenderer.BuildClipPath(g, clipDefinition, unitsMatrix);
            if (path is null) return null;

            useEvenOdd = clipDefinition.ClipRule == RFillMode.EvenOdd;
            return path;
        }

        /// <summary>
        /// Resolves a circle <c>&lt;shape-radius&gt;</c>. A percentage resolves against
        /// <c>sqrt(w² + h²)/sqrt(2)</c> (CSS Shapes Level 1); <c>closest-side</c>/<c>farthest-side</c>
        /// use the min/max distance from the center to the four edges.
        /// </summary>
        private static double ResolveCircleRadius(BasicShapeGrammar.ShapeRadius radius, double cx, double cy, RRect referenceBox, CssBox box)
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
        private static double ResolveAxisRadius(BasicShapeGrammar.ShapeRadius radius, double centerToStart, double axisExtent, double hundredPercent, CssBox box)
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
