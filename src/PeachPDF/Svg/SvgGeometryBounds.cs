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

using PeachPDF.Html.Adapters.Entities;
using System;
using System.Collections.Generic;

namespace PeachPDF.Svg
{
    /// <summary>
    /// Computes an element's local-space bounding box - needed to resolve <c>objectBoundingBox</c>-unit
    /// gradients/patterns/masks (fractions of the referencing shape's own geometry) at paint time. The
    /// box always excludes the element's <em>own</em> <see cref="SvgElement.Transform"/> (that is applied
    /// externally, by whatever pushes the current transform before painting/measuring against it), but
    /// for a group it includes every <em>descendant</em>'s own transform, composed all the way down -
    /// a child's box (in the frame its own content paints in) is mapped through the child's transform
    /// before being folded into the group's box (<see cref="UnionAll"/>/<see cref="TransformBounds"/>).
    /// </summary>
    internal static class SvgGeometryBounds
    {
        public static RRect? GetBoundingBox(SvgElement element) => element switch
        {
            SvgPathElement path => PathBounds(path.Segments),
            SvgCircleElement { R: > 0 } circle => new RRect(circle.Cx - circle.R, circle.Cy - circle.R, circle.R * 2, circle.R * 2),
            SvgEllipseElement { Rx: > 0, Ry: > 0 } ellipse => new RRect(ellipse.Cx - ellipse.Rx, ellipse.Cy - ellipse.Ry, ellipse.Rx * 2, ellipse.Ry * 2),
            SvgRectElement { Width: > 0, Height: > 0 } rect => new RRect(rect.X, rect.Y, rect.Width, rect.Height),
            SvgPolygonElement polygon => PointsBounds(polygon.Points),
            SvgPolylineElement polyline => PointsBounds(polyline.Points),
            SvgLineElement line => PointsBounds([new RPoint(line.X1, line.Y1), new RPoint(line.X2, line.Y2)]),
            SvgUseElement { Target: { } target } use => Offset(GetBoundingBox(target), use.X, use.Y),
            SvgGroupElement group => UnionAll(group.Children),
            _ => null,
        };

        private static RRect? Offset(RRect? rect, double dx, double dy) =>
            rect is { } r ? new RRect(r.X + dx, r.Y + dy, r.Width, r.Height) : null;

        /// <summary>
        /// The box <see cref="SvgRenderer"/>'s context-paint mechanism (<c>ContextBounds</c>) measures a <c>&lt;use&gt;</c>'s
        /// <paramref name="target"/> against, when it is a <see cref="SvgSymbolElement"/> or <see cref="SvgNestedSvgElement"/> -
        /// element types <see cref="GetBoundingBox"/> itself deliberately keeps returning null for (every other
        /// <c>objectBoundingBox</c> gradient/mask/clip-path/filter-region consumer that measures one of these elements
        /// directly - not through a <c>&lt;use&gt;</c>'s context-paint path - still needs that null: unlike a plain shape or
        /// group, a symbol/nested-svg target establishes its own viewBox-to-viewport mapping, and the box <em>those</em>
        /// consumers would need is the mapped, "established viewport rect" one - not this method's raw, pre-mapping union -
        /// which would need the referencing <c>&lt;use&gt;</c>'s own sizing to compute and so can't be produced from the
        /// target element alone).
        /// </summary>
        /// <remarks>
        /// Returns the union of <paramref name="target"/>'s own children (<see cref="UnionAll"/>) - the same, pre-mapping
        /// frame those children's coordinates are defined in (a <c>&lt;symbol&gt;</c>/nested <c>&lt;svg&gt;</c> has no
        /// geometry of its own beyond them). This is exactly the frame <see cref="SvgRenderer"/>'s recorded context frame
        /// for the two symbol/nested-svg <c>RenderElementSwitch</c> arms maps <em>out of</em> (that frame composes the
        /// same viewBox-to-viewport matrix <c>RenderViewport</c> itself pushes before painting those children, with the
        /// ambient transform active where the <c>&lt;use&gt;</c> renders it) - mirroring how the plain-element arm's
        /// recorded frame composes <c>target.Transform</c> with the ambient to match <see cref="GetBoundingBox"/>'s own
        /// (also pre-<c>Transform</c>) convention for every other target type.
        /// </remarks>
        internal static RRect? GetUseTargetBoundingBox(SvgElement target) => target switch
        {
            SvgSymbolElement symbol => UnionAll(symbol.Children),
            SvgNestedSvgElement nestedSvg => UnionAll(nestedSvg.Children),
            _ => GetBoundingBox(target),
        };

        private static RRect? UnionAll(IEnumerable<SvgElement> elements)
        {
            RRect? result = null;

            foreach (var element in elements)
            {
                var bounds = GetBoundingBox(element);
                if (bounds is not { } b)
                    continue;

                // Compose the child's own transform in before unioning - see the class remarks. Skipping
                // this silently contributed a transformed child's untransformed geometry instead, which
                // SvgRenderer's context-paint-through-use fix relies on being correct, not "close enough".
                if (element.Transform is { } transform)
                    b = TransformBounds(b, transform);

                result = result is { } r ? Union(r, b) : b;
            }

            return result;
        }

        /// <summary>
        /// The axis-aligned envelope of <paramref name="rect"/>'s four corners mapped through <paramref name="matrix"/>. Internal
        /// (not just used by <see cref="UnionAll"/>) because <see cref="SvgRenderer"/>'s own parallel "union children's bounds"
        /// pass for an opacity-group tile (<c>UnionOpacityGroupBounds</c>) needs the exact same child-transform composition.
        /// </summary>
        internal static RRect TransformBounds(RRect rect, RMatrix matrix)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;

            Span<(double X, double Y)> corners =
            [
                (rect.X, rect.Y), (rect.X + rect.Width, rect.Y),
                (rect.X, rect.Y + rect.Height), (rect.X + rect.Width, rect.Y + rect.Height),
            ];

            foreach (var (x, y) in corners)
            {
                var px = x * matrix.M11 + y * matrix.M21 + matrix.OffsetX;
                var py = x * matrix.M12 + y * matrix.M22 + matrix.OffsetY;
                minX = Math.Min(minX, px);
                maxX = Math.Max(maxX, px);
                minY = Math.Min(minY, py);
                maxY = Math.Max(maxY, py);
            }

            return new RRect(minX, minY, maxX - minX, maxY - minY);
        }

        private static RRect Union(RRect a, RRect b)
        {
            var minX = Math.Min(a.X, b.X);
            var minY = Math.Min(a.Y, b.Y);
            var maxX = Math.Max(a.X + a.Width, b.X + b.Width);
            var maxY = Math.Max(a.Y + a.Height, b.Y + b.Height);
            return new RRect(minX, minY, maxX - minX, maxY - minY);
        }

        private static RRect? PointsBounds(RPoint[] points)
        {
            if (points.Length == 0)
                return null;

            double minX = points[0].X, maxX = minX;
            double minY = points[0].Y, maxY = minY;

            for (var i = 1; i < points.Length; i++)
            {
                minX = Math.Min(minX, points[i].X);
                maxX = Math.Max(maxX, points[i].X);
                minY = Math.Min(minY, points[i].Y);
                maxY = Math.Max(maxY, points[i].Y);
            }

            return new RRect(minX, minY, maxX - minX, maxY - minY);
        }

        /// <summary>
        /// Includes bezier control points and arc endpoints rather than computing exact curve
        /// extrema - a slight over-estimate for a bezier and (for an arc, whose bulge is not
        /// counted) an under-estimate, adequate for objectBoundingBox gradient/pattern/mask
        /// positioning (minor error just shifts stops/tiles slightly, with no visible artifact).
        /// <see cref="SvgInkExtent"/>, which needs a box that holds the ink, adds an allowance for arcs.
        /// </summary>
        private static RRect? PathBounds(IReadOnlyList<PathSegment> segments)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            var any = false;

            void Include(double x, double y)
            {
                any = true;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }

            foreach (var segment in segments)
            {
                switch (segment.Kind)
                {
                    case PathSegmentKind.MoveTo:
                    case PathSegmentKind.LineTo:
                    case PathSegmentKind.ArcTo:
                        Include(segment.X, segment.Y);
                        break;
                    case PathSegmentKind.CubicBezierTo:
                        Include(segment.X1, segment.Y1);
                        Include(segment.X2, segment.Y2);
                        Include(segment.X, segment.Y);
                        break;
                }
            }

            return any ? new RRect(minX, minY, maxX - minX, maxY - minY) : null;
        }
    }
}
