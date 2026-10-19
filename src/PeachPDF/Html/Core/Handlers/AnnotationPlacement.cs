using PeachDrawing;
using PeachDrawing.Core;
using System.Numerics;
using System;

namespace PeachPDF.Html.Core.Handlers
{
    /// <summary>
    /// How a point in a page's fragmentainer-local layout space (the space the painter works in, in layout pixels) lands on the PDF page, in
    /// default user space: points, y up. The same arithmetic <c>PdfGenerator.HandleLinks</c> does inline for an untransformed rectangle
    /// (<c>ContentLeftPt + (x - MarginLeft) / ppp</c>, y flipped against the page height); a change to the page geometry there changes this too.
    /// </summary>
    /// <param name="ContentLeftPt">where this page's content box starts from the left edge, in points</param>
    /// <param name="ContentTopPt">where this page's content box starts from the top edge, in points</param>
    /// <param name="OriginX">the layout x that sits at the content box's left edge (the container's left margin)</param>
    /// <param name="OriginY">the layout y that sits at the content box's top edge (the container's top margin)</param>
    /// <param name="PixelsPerPoint">layout pixels per point</param>
    /// <param name="PageHeightPt">the page's height in points, which the y flip is against</param>
    internal readonly record struct PageFrame(double ContentLeftPt, double ContentTopPt, double OriginX, double OriginY, double PixelsPerPoint, double PageHeightPt)
    {
        /// <summary>Where a layout point lands on the page measured from its top-left corner, y down, in points.</summary>
        internal PaintPoint ToPage(double x, double y) =>
            new(ContentLeftPt + (x - OriginX) / PixelsPerPoint, ContentTopPt + (y - OriginY) / PixelsPerPoint);

        /// <summary>Where a layout point lands in PDF default user space: <see cref="ToPage"/> with y flipped against the page height.</summary>
        internal PaintPoint ToPdf(double x, double y) =>
            new(ContentLeftPt + (x - OriginX) / PixelsPerPoint, PageHeightPt - (ContentTopPt + (y - OriginY) / PixelsPerPoint));

        internal PaintPoint To(double x, double y, bool yUp) => yUp ? ToPdf(x, y) : ToPage(x, y);
    }

    /// <summary>
    /// Where an annotation over a box that a <c>transform</c> moved goes: the box's layout rectangle carried through the map the painter drew
    /// it with (<see cref="BoxPlacements"/>) and onto the page.
    /// </summary>
    /// <remarks>
    /// A PDF <c>/Rect</c> is axis-aligned, so it is the bounding box of what was drawn. A link also carries the drawn quadrilateral as
    /// <c>/QuadPoints</c> when it is not that box (ISO 32000 §12.5.6.5), which is how a reader tells the corner of a rotated link from the
    /// empty paper beside it. A widget has no such entry, so its appearance stream is given the map's linear part as <c>/Matrix</c> instead
    /// (§12.5.5, "appearance streams": a reader fits the matrix-transformed <c>/BBox</c> to <c>/Rect</c>), which makes a rotated, scaled or
    /// skewed control draw as the page content does. Only an affine map can be carried by that matrix; under perspective the widget is the
    /// upright bounding box.
    /// </remarks>
    internal readonly record struct AnnotationPlacement(Rect Bounds, PaintPoint[]? Quad, Matrix3x2? AppearanceMatrix)
    {
        /// <summary>The corners smaller than this are the same corner.</summary>
        private const double Tolerance = 1e-6;

        /// <summary>
        /// Maps <paramref name="local"/>, a rectangle in fragmentainer-local layout space, through <paramref name="map"/> onto the page.
        /// </summary>
        /// <param name="map">the map the painter drew the box with</param>
        /// <param name="local">the rectangle, in fragmentainer-local layout space</param>
        /// <param name="frame">how layout space lands on the page</param>
        /// <param name="placement">where it lands</param>
        /// <param name="yUp">whether the result is in PDF default user space (y up from the page bottom) rather than measured down from the page top</param>
        /// <returns>false when nothing of the rectangle is in front of the viewer (a box wholly behind the eye under perspective)</returns>
        internal static bool TryMap(in Homography map, Rect local, in PageFrame frame, out AnnotationPlacement placement, bool yUp = true)
        {
            placement = default;

            var polygon = map.ProjectRectangle(local.Left, local.Top, local.Right, local.Bottom);
            if (polygon.Count < 3)
                return false;

            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            var points = new PaintPoint[polygon.Count];
            for (var i = 0; i < polygon.Count; i++)
            {
                var p = frame.To(polygon[i].X, polygon[i].Y, yUp);
                points[i] = p;
                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
            }

            var bounds = new Rect(minX, minY, maxX - minX, maxY - minY);

            // Four corners that do not fill their own bounding box: the box is the click target and the quadrilateral the shape. Written in
            // the order readers expect, top-left, top-right, bottom-left, bottom-right of the rectangle they started as, rather than the
            // polygon's winding - and only when nothing was cut off behind the eye, since two clipped corners also leave four points, in an
            // order that remap would tangle.
            PaintPoint[]? quad = null;
            if (points.Length == 4 && AllInFront(map, local) && Math.Abs(ShoelaceArea(points) - bounds.Width * bounds.Height) > Tolerance * Math.Max(1, bounds.Width * bounds.Height))
                quad = [points[0], points[1], points[3], points[2]];

            placement = new AnnotationPlacement(bounds, quad, map.IsAffine ? LinearPart(map, local, frame, yUp) : null);
            return true;
        }

        /// <summary>
        /// <see cref="TryMap"/> for a link, measured down from the page's top-left corner: where its box goes, and the quadrilateral
        /// (null when the box says it all).
        /// </summary>
        /// <returns>false when none of it is on the sheet - moved off it, or wholly behind the viewer - so nothing drawn is there to click</returns>
        internal static bool TryPlaceLink(in Placement placement, Rect local, in PageFrame frame, double sheetWidthPt, double sheetHeightPt,
            out Rect bounds, out PaintPoint[]? quad)
        {
            bounds = default;
            quad = null;

            if (placement.Hidden || !TryMap(placement.Map, local, frame, out var placed, yUp: false) || !placed.Bounds.IntersectsWith(new Rect(0, 0, sheetWidthPt, sheetHeightPt)))
                return false;

            bounds = placed.Bounds;
            quad = placed.Quad;
            return true;
        }

        /// <summary>Whether every corner of <paramref name="local"/> is in front of the viewer, the clip <see cref="Homography.ProjectRectangle"/> makes.</summary>
        private static bool AllInFront(in Homography map, Rect local) =>
            InFront(map, local.Left, local.Top) && InFront(map, local.Right, local.Top) &&
            InFront(map, local.Right, local.Bottom) && InFront(map, local.Left, local.Bottom);

        /// <summary>Whether (<paramref name="x"/>, <paramref name="y"/>) survives <see cref="Homography.ProjectRectangle"/>'s clip, whose default epsilon this repeats.</summary>
        private static bool InFront(in Homography map, double x, double y) => map.ApplyHomogeneous(x, y).W > ProjectionEpsilon;

        /// <summary>The divisor at or below which <see cref="Homography.ProjectRectangle"/> cuts a corner off (its default <c>epsilon</c>).</summary>
        private const double ProjectionEpsilon = 1e-4;

        private static Matrix3x2 Matrix(double m11, double m12, double m21, double m22, double dx, double dy) =>
            new((float)m11, (float)m12, (float)m21, (float)m22, (float)dx, (float)dy);

        /// <summary>The area of the polygon <paramref name="points"/>, whichever way it winds.</summary>
        private static double ShoelaceArea(PaintPoint[] points)
        {
            double twice = 0;
            for (var i = 0; i < points.Length; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Length];
                twice += a.X * b.Y - b.X * a.Y;
            }

            return Math.Abs(twice) / 2;
        }

        /// <summary>
        /// The matrix that takes an appearance drawn upright in <c>(0, 0)</c> to <c>(width, height)</c> points (y up, or y down when
        /// <paramref name="yUp"/> is false) to where the rectangle it belongs to was drawn: the images of the rectangle's own axes, which is
        /// the map's linear part.
        /// </summary>
        private static Matrix3x2? LinearPart(in Homography map, Rect local, in PageFrame frame, bool yUp)
        {
            if (!(local.Width > 0) || !(local.Height > 0) ||
                map.Apply(local.Left, local.Top) is not { } topLeft ||
                map.Apply(local.Right, local.Top) is not { } topRight ||
                map.Apply(local.Left, local.Bottom) is not { } bottomLeft)
                return null;

            var bottomLeftPdf = frame.To(bottomLeft.X, bottomLeft.Y, yUp);
            var topRightPdf = frame.To(topRight.X, topRight.Y, yUp);
            var topLeftPdf = frame.To(topLeft.X, topLeft.Y, yUp);
            var widthPt = local.Width / frame.PixelsPerPoint;
            var heightPt = local.Height / frame.PixelsPerPoint;

            // Measured down from the page top, the appearance's y runs down the left edge from the top-left corner to the bottom-left one,
            // so an untransformed control maps to the identity here too.
            if (!yUp)
            {
                return Matrix(
                    (topRightPdf.X - topLeftPdf.X) / widthPt, (topRightPdf.Y - topLeftPdf.Y) / widthPt,
                    (bottomLeftPdf.X - topLeftPdf.X) / heightPt, (bottomLeftPdf.Y - topLeftPdf.Y) / heightPt,
                    0, 0);
            }

            // The appearance's x runs along the top edge, from the top-left corner to the top-right one; its y runs up the left edge,
            // from the bottom-left corner to the top-left one. There is no translation: the reader fits the transformed box to /Rect, so one
            // would only be cancelled, and a plain move is then the identity, which is left out of the stream altogether.
            return Matrix(
                (topRightPdf.X - topLeftPdf.X) / widthPt, (topRightPdf.Y - topLeftPdf.Y) / widthPt,
                (topLeftPdf.X - bottomLeftPdf.X) / heightPt, (topLeftPdf.Y - bottomLeftPdf.Y) / heightPt,
                0, 0);
        }
    }
}
