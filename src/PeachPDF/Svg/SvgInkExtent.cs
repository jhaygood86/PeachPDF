using PeachDrawing.Abstractions;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace PeachPDF.Svg
{
    /// <summary>
    /// A conservative bounding box of what a document draws, in the document's own user space: the geometry of every shape with the width its
    /// stroke can reach, through groups, <c>use</c> and transforms. It is what a drawing that is clipped to a canvas (an OpenType SVG glyph)
    /// needs to know so that the canvas holds all of the artwork.
    /// </summary>
    /// <remarks>
    /// It is a box for the shapes, not a measurement of the ink: a curve's control points count, a clip or mask is ignored (they only remove
    /// ink), and what has no static geometry (text, a marker's content, the region a filter paints) contributes nothing. Whoever uses it treats
    /// a missing part as "no more than the canvas it already has".
    /// </remarks>
    internal static class SvgInkExtent
    {
        /// <summary>How far, in half stroke widths, the corner of a square cap reaches.</summary>
        private static readonly double SquareCapReach = Math.Sqrt(2);

        /// <summary>The box of everything <paramref name="document"/> draws, or <see langword="null"/> when it draws nothing with a known extent.</summary>
        internal static Rect? Of(SvgDocument document) => UnionAll(document.Children);

        private static Rect? UnionAll(IEnumerable<SvgElement> elements)
        {
            Rect? result = null;
            foreach (var element in elements)
            {
                result = Union(result, Bounds(element));
            }

            return result;
        }

        /// <summary>The box of <paramref name="element"/> in its parent's space: its own geometry mapped through its <c>transform</c>.</summary>
        private static Rect? Bounds(SvgElement element)
        {
            var local = LocalBounds(element);
            if (local is not { } box || !IsFinite(box))
            {
                return null;
            }

            var mapped = element.Transform is { } transform ? Transformed(box, transform) : box;

            // a transform can overflow what was finite
            return IsFinite(mapped) ? mapped : null;
        }

        private static Rect? LocalBounds(SvgElement element)
        {
            switch (element)
            {
                case SvgGroupElement group:
                    return UnionAll(group.Children);

                case SvgUseElement { Target: { } target } use:
                    return UseBounds(use, target);

                case SvgNestedSvgElement nested:
                    // A nested viewport clips what it draws to itself.
                    return nested is { Width: > 0, Height: > 0 } ? new Rect(nested.X, nested.Y, nested.Width, nested.Height) : null;

                case SvgImageElement image:
                    return image is { Width: > 0, Height: > 0 } ? new Rect(image.X, image.Y, image.Width, image.Height) : null;

                case SvgTextElement:
                    return null;

                case SvgPathElement path:
                    return SvgGeometryBounds.GetBoundingBox(element) is { } outline ? Inflated(Union(outline, ArcAllowance(path.Segments)) ?? outline, element) : null;

                default:
                    return SvgGeometryBounds.GetBoundingBox(element) is { } geometry ? Inflated(geometry, element) : null;
            }
        }

        /// <summary>
        /// A box that holds what the arcs of a path can bulge to. <see cref="SvgGeometryBounds"/> counts only an arc's end point, but an arc stays
        /// within its ellipse, so every point of it is within two of the (scaled up, if it is too small to span the chord) larger radius of its
        /// end point.
        /// </summary>
        private static Rect? ArcAllowance(IReadOnlyList<PathSegment> segments)
        {
            Rect? result = null;
            double currentX = 0, currentY = 0, startX = 0, startY = 0;

            foreach (var segment in segments)
            {
                switch (segment.Kind)
                {
                    case PathSegmentKind.MoveTo:
                        startX = currentX = segment.X;
                        startY = currentY = segment.Y;
                        break;

                    case PathSegmentKind.ClosePath:
                        currentX = startX;
                        currentY = startY;
                        break;

                    case PathSegmentKind.ArcTo:
                    {
                        var reach = ArcReach(currentX, currentY, segment);
                        if (double.IsFinite(reach) && reach > 0)
                        {
                            result = Union(result, new Rect(segment.X - reach, segment.Y - reach, 2 * reach, 2 * reach));
                        }

                        currentX = segment.X;
                        currentY = segment.Y;
                        break;
                    }

                    default:
                        currentX = segment.X;
                        currentY = segment.Y;
                        break;
                }
            }

            return result;
        }

        private static double ArcReach(double fromX, double fromY, PathSegment arc)
        {
            double rx = Math.Abs(arc.RadiusX), ry = Math.Abs(arc.RadiusY);
            if (rx == 0 || ry == 0)
            {
                return 0;    // a straight line
            }

            // The radii of an arc too small to span its chord grow until they can (SVG 1.1 F.6.6).
            var angle = arc.RotationAngle * Math.PI / 180;
            double halfX = (fromX - arc.X) / 2, halfY = (fromY - arc.Y) / 2;
            var x = Math.Cos(angle) * halfX + Math.Sin(angle) * halfY;
            var y = -Math.Sin(angle) * halfX + Math.Cos(angle) * halfY;
            var scale = x * x / (rx * rx) + y * y / (ry * ry);
            if (scale > 1)
            {
                var grow = Math.Sqrt(scale);
                rx *= grow;
                ry *= grow;
            }

            return 2 * Math.Max(rx, ry);
        }

        private static Rect? UseBounds(SvgUseElement use, SvgElement target)
        {
            switch (target)
            {
                // A symbol or a nested svg is drawn in a viewport of the use's size, which is the ambient viewport (the canvas) unless it is given.
                case SvgSymbolElement:
                    return use is { Width: > 0, Height: > 0 } ? new Rect(use.X, use.Y, use.Width.Value, use.Height.Value) : null;

                case SvgNestedSvgElement nested:
                {
                    var width = use.Width ?? nested.Width;
                    var height = use.Height ?? nested.Height;
                    return width > 0 && height > 0 ? new Rect(use.X, use.Y, width, height) : null;
                }

                default:
                    return Bounds(target) is { } box ? new Rect(box.X + use.X, box.Y + use.Y, box.Width, box.Height) : null;
            }
        }

        /// <summary>The geometry grown by how far its stroke can reach: half the width, times the miter limit at a mitered join, or the square cap's diagonal.</summary>
        private static Rect Inflated(Rect geometry, SvgElement element)
        {
            if (element.Stroke.Kind == SvgPaintKind.None || !(element.StrokeWidth > 0))
            {
                return geometry;
            }

            var reach = element.StrokeLineJoin == LineJoin.Miter ? Math.Max(1.0, element.StrokeMiterLimit) : 1.0;
            if (element.StrokeLineCap == LineCap.Square)
            {
                reach = Math.Max(reach, SquareCapReach);
            }

            var grow = element.StrokeWidth / 2 * reach;
            return new Rect(geometry.X - grow, geometry.Y - grow, geometry.Width + 2 * grow, geometry.Height + 2 * grow);
        }

        private static Rect Transformed(Rect box, Matrix3x2 matrix)
        {
            double left = box.X, top = box.Y, right = box.X + box.Width, bottom = box.Y + box.Height;

            // the four corners, mapped
            double x1 = left * matrix.M11 + top * matrix.M21 + matrix.M31, y1 = left * matrix.M12 + top * matrix.M22 + matrix.M32;
            double x2 = right * matrix.M11 + top * matrix.M21 + matrix.M31, y2 = right * matrix.M12 + top * matrix.M22 + matrix.M32;
            double x3 = left * matrix.M11 + bottom * matrix.M21 + matrix.M31, y3 = left * matrix.M12 + bottom * matrix.M22 + matrix.M32;
            double x4 = right * matrix.M11 + bottom * matrix.M21 + matrix.M31, y4 = right * matrix.M12 + bottom * matrix.M22 + matrix.M32;

            var minX = Math.Min(Math.Min(x1, x2), Math.Min(x3, x4));
            var maxX = Math.Max(Math.Max(x1, x2), Math.Max(x3, x4));
            var minY = Math.Min(Math.Min(y1, y2), Math.Min(y3, y4));
            var maxY = Math.Max(Math.Max(y1, y2), Math.Max(y3, y4));
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        private static Rect? Union(Rect? a, Rect? b)
        {
            if (a is not { } first)
            {
                return b;
            }

            if (b is not { } second)
            {
                return first;
            }

            var minX = Math.Min(first.X, second.X);
            var minY = Math.Min(first.Y, second.Y);
            return new Rect(minX, minY, Math.Max(first.X + first.Width, second.X + second.Width) - minX,
                Math.Max(first.Y + first.Height, second.Y + second.Height) - minY);
        }

        private static bool IsFinite(Rect box) =>
            double.IsFinite(box.X) && double.IsFinite(box.Y) && double.IsFinite(box.Width) && double.IsFinite(box.Height);
    }
}
