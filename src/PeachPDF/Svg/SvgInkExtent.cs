using PeachPDF.Html.Adapters.Entities;
using System;
using System.Collections.Generic;

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
        /// <summary>The box of everything <paramref name="document"/> draws, or <see langword="null"/> when it draws nothing with a known extent.</summary>
        internal static RRect? Of(SvgDocument document) => UnionAll(document.Children);

        private static RRect? UnionAll(IEnumerable<SvgElement> elements)
        {
            RRect? result = null;
            foreach (var element in elements)
            {
                result = Union(result, Bounds(element));
            }

            return result;
        }

        /// <summary>The box of <paramref name="element"/> in its parent's space: its own geometry mapped through its <c>transform</c>.</summary>
        private static RRect? Bounds(SvgElement element)
        {
            var local = LocalBounds(element);
            if (local is not { } box || !IsFinite(box))
            {
                return null;
            }

            return element.Transform is { } transform ? Transformed(box, transform) : box;
        }

        private static RRect? LocalBounds(SvgElement element)
        {
            switch (element)
            {
                case SvgGroupElement group:
                    return UnionAll(group.Children);

                case SvgUseElement { Target: { } target } use:
                    return UseBounds(use, target);

                case SvgNestedSvgElement nested:
                    // A nested viewport clips what it draws to itself.
                    return nested is { Width: > 0, Height: > 0 } ? new RRect(nested.X, nested.Y, nested.Width, nested.Height) : null;

                case SvgImageElement image:
                    return image is { Width: > 0, Height: > 0 } ? new RRect(image.X, image.Y, image.Width, image.Height) : null;

                case SvgTextElement:
                    return null;

                default:
                    return SvgGeometryBounds.GetBoundingBox(element) is { } geometry ? Inflated(geometry, element) : null;
            }
        }

        private static RRect? UseBounds(SvgUseElement use, SvgElement target)
        {
            switch (target)
            {
                // A symbol or a nested svg is drawn in a viewport of the use's size, which is the ambient viewport (the canvas) unless it is given.
                case SvgSymbolElement:
                    return use is { Width: > 0, Height: > 0 } ? new RRect(use.X, use.Y, use.Width.Value, use.Height.Value) : null;

                case SvgNestedSvgElement nested:
                {
                    var width = use.Width ?? nested.Width;
                    var height = use.Height ?? nested.Height;
                    return width > 0 && height > 0 ? new RRect(use.X, use.Y, width, height) : null;
                }

                default:
                    return Bounds(target) is { } box ? new RRect(box.X + use.X, box.Y + use.Y, box.Width, box.Height) : null;
            }
        }

        /// <summary>The geometry grown by how far its stroke can reach: half the width, times the miter limit at a mitered join, or the square cap's diagonal.</summary>
        private static RRect Inflated(RRect geometry, SvgElement element)
        {
            if (element.Stroke.Kind == SvgPaintKind.None || !(element.StrokeWidth > 0))
            {
                return geometry;
            }

            var reach = element.StrokeLineJoin == RLineJoin.Miter ? Math.Max(1.0, element.StrokeMiterLimit) : 1.0;
            if (element.StrokeLineCap == RLineCap.Square)
            {
                reach = Math.Max(reach, Math.Sqrt(2));
            }

            var grow = element.StrokeWidth / 2 * reach;
            return new RRect(geometry.X - grow, geometry.Y - grow, geometry.Width + 2 * grow, geometry.Height + 2 * grow);
        }

        private static RRect Transformed(RRect box, RMatrix matrix)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var (x, y) in new[] { (box.X, box.Y), (box.X + box.Width, box.Y), (box.X, box.Y + box.Height), (box.X + box.Width, box.Y + box.Height) })
            {
                var mappedX = x * matrix.M11 + y * matrix.M21 + matrix.OffsetX;
                var mappedY = x * matrix.M12 + y * matrix.M22 + matrix.OffsetY;
                minX = Math.Min(minX, mappedX);
                maxX = Math.Max(maxX, mappedX);
                minY = Math.Min(minY, mappedY);
                maxY = Math.Max(maxY, mappedY);
            }

            return new RRect(minX, minY, maxX - minX, maxY - minY);
        }

        private static RRect? Union(RRect? a, RRect? b)
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
            return new RRect(minX, minY, Math.Max(first.X + first.Width, second.X + second.Width) - minX,
                Math.Max(first.Y + first.Height, second.Y + second.Height) - minY);
        }

        private static bool IsFinite(RRect box) =>
            double.IsFinite(box.X) && double.IsFinite(box.Y) && double.IsFinite(box.Width) && double.IsFinite(box.Height);
    }
}
