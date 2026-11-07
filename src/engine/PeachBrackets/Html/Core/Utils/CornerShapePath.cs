using System;
using System.Collections.Generic;
using PeachBrackets.Html.Dom;
using PeachDrawing.Core;

namespace PeachBrackets.Html.Utils
{
    /// <summary>
    /// The path of a box whose corners carry a CSS Borders 4 <c>corner-shape</c>. Each corner occupies the
    /// rectangle its used radii give it, and its shape is a superellipse <c>|u|^n + |v|^n = 1</c> with
    /// <c>n = 2^K</c> over that rectangle: <c>round</c> is K = 1 (an ellipse, drawn as the same exact arc a plain
    /// <c>border-radius</c> uses), <c>squircle</c> K = 2, <c>bevel</c> K = 0 (a straight cut), <c>square</c>
    /// K = infinity (no cut). A negative K is the mirror image of its positive twin across the corner's chord, so
    /// <c>scoop</c> (K = -1) is a concave ellipse centred on the box's own corner and <c>notch</c> (K = -infinity)
    /// a concave right angle.
    /// </summary>
    internal static class CornerShapePath
    {
        /// <summary>Segments a curved corner other than an exact ellipse is flattened into.</summary>
        private const int Segments = 24;

        /// <summary>Beyond this |K| the curve is indistinguishable from a right angle and is drawn as one.</summary>
        private const double RightAngleK = 30;

        /// <summary>Adds the rectangle with the given radii and shapes; everything is in the path's own units.</summary>
        public static void AddRectangle(GraphicsPath path, Rect rect, BorderRadii r) => AddRectangle(path, rect, r, default);

        /// <summary>
        /// Adds the rectangle with its radii and shapes, where a concave corner (negative K) may be anchored outside the rectangle:
        /// <paramref name="overhang"/> is how far each side of the rectangle sits inside the border box whose corner the cut is centred
        /// on. The border's inner contour needs this: a scoop or notch is offset by growing the cut around the border box's own corner,
        /// not by shrinking one around the inset corner, which would leave the border no thickness along it.
        /// </summary>
        public static void AddRectangle(GraphicsPath path, Rect rect, BorderRadii r, CornerOverhang overhang)
        {
            double left = rect.Left, top = rect.Top, right = rect.Right, bottom = rect.Bottom;

            // The middle of the top edge is on the contour whatever the corners do, so every corner is entered and left the same way.
            path.Start((left + right) / 2, top);

            // Clockwise from the top edge. TR and BL are walked from their top/bottom-edge end towards the side
            // edge (local (1,0) -> (0,1)); BR and TL the other way round.
            AddCorner(path, right, top, -1, +1, r.TRX, r.TRY, r.KTR, false, GraphicsPath.Corner.TopRight, overhang.Right, overhang.Top);
            AddCorner(path, right, bottom, -1, -1, r.BRX, r.BRY, r.KBR, true, GraphicsPath.Corner.BottomRight, overhang.Right, overhang.Bottom);
            AddCorner(path, left, bottom, +1, -1, r.BLX, r.BLY, r.KBL, false, GraphicsPath.Corner.BottomLeft, overhang.Left, overhang.Bottom);
            AddCorner(path, left, top, +1, +1, r.TLX, r.TLY, r.KTL, true, GraphicsPath.Corner.TopLeft, overhang.Left, overhang.Top);

            path.CloseFigure();
        }

        /// <summary>
        /// Appends one corner. <paramref name="cx"/>,<paramref name="cy"/> is the box corner; <paramref name="sx"/>/<paramref name="sy"/>
        /// point into the box. In the corner's own frame (u, v) runs 0..1 across its rx by ry rectangle from the box corner, so
        /// the edge ends are (1,0) on the horizontal edge and (0,1) on the vertical one.
        /// </summary>
        private static void AddCorner(GraphicsPath path, double cx, double cy, double sx, double sy,
            double rx, double ry, double k, bool fromVerticalEdge, GraphicsPath.Corner arcCorner, double overhangX, double overhangY)
        {
            if (k < 0 && k > -RightAngleK && rx > 0 && ry > 0 && (overhangX > 0 || overhangY > 0))
            {
                AddAnchoredConcaveCorner(path, cx, cy, sx, sy, rx, ry, k, fromVerticalEdge, overhangX, overhangY);
                return;
            }

            double X(double u) => cx + sx * rx * u;
            double Y(double v) => cy + sy * ry * v;

            var (entryU, entryV, exitU, exitV) = fromVerticalEdge ? (0d, 1d, 1d, 0d) : (1d, 0d, 0d, 1d);
            path.LineTo(X(entryU), Y(entryV));

            if (rx <= 0 || ry <= 0 || k >= RightAngleK)
            {
                path.LineTo(X(0), Y(0));
                path.LineTo(X(exitU), Y(exitV));
                return;
            }

            if (k <= -RightAngleK)
            {
                path.LineTo(X(1), Y(1));
                path.LineTo(X(exitU), Y(exitV));
                return;
            }

            if (k == 1)
            {
                path.ArcTo(X(exitU), Y(exitV), rx, ry, arcCorner);
                return;
            }

            if (k == 0)
            {
                path.LineTo(X(exitU), Y(exitV));
                return;
            }

            // Sample from the vertical-edge end (0,1) to the horizontal-edge end (1,0), reversing for a corner walked the other way.
            var exponent = Math.Pow(2, Math.Abs(k));
            var concave = k < 0;
            for (var i = 1; i <= Segments; i++)
            {
                var step = fromVerticalEdge ? i : Segments - i;
                var t = step * (Math.PI / 2) / Segments;
                var c = Math.Pow(Math.Cos(t), 2 / exponent);
                var s = Math.Pow(Math.Sin(t), 2 / exponent);
                var (u, v) = concave ? (s, c) : (1 - c, 1 - s);
                path.LineTo(X(u), Y(v));
            }

            // The last sample is the exit within rounding; land on it exactly so neighbouring edges meet.
            path.LineTo(X(exitU), Y(exitV));
        }
        /// <summary>
        /// A concave corner whose cut is centred on the border box's own corner, <paramref name="overhangX"/> by
        /// <paramref name="overhangY"/> outside the inset rectangle being drawn: the cut's extended box is sampled and only the part
        /// inside the rectangle's own edges is kept, entering and leaving along those edges.
        /// </summary>
        private static void AddAnchoredConcaveCorner(GraphicsPath path, double cx, double cy, double sx, double sy,
            double rx, double ry, double k, bool fromVerticalEdge, double overhangX, double overhangY)
        {
            const int Samples = 64;
            double extX = rx + overhangX, extY = ry + overhangY;
            double anchorX = cx - sx * overhangX, anchorY = cy - sy * overhangY;
            var exponent = Math.Pow(2, Math.Abs(k));

            var points = new List<(double X, double Y)>(Samples + 3);
            (double X, double Y, double A, double B) previous = default;
            var started = false;
            for (var i = 0; i <= Samples; i++)
            {
                var t = i * (Math.PI / 2) / Samples;
                var c = Math.Pow(Math.Cos(t), 2 / exponent);
                var s = Math.Pow(Math.Sin(t), 2 / exponent);
                double x = anchorX + sx * extX * s, y = anchorY + sy * extY * c;
                // How far inside the rectangle's own vertical and horizontal edge lines the sample is.
                double a = sx * (x - cx), b = sy * (y - cy);

                if (a >= 0 && b >= 0)
                {
                    if (!started && i > 0)
                    {
                        var f = previous.A / (previous.A - a);
                        points.Add((previous.X + (x - previous.X) * f, previous.Y + (y - previous.Y) * f));
                    }

                    started = true;
                    points.Add((x, y));
                }
                else if (started)
                {
                    // Left through the horizontal edge: land on it.
                    var f = previous.B / (previous.B - b);
                    points.Add((previous.X + (x - previous.X) * f, previous.Y + (y - previous.Y) * f));
                    break;
                }

                previous = (x, y, a, b);
            }

            if (points.Count == 0) return;
            if (!fromVerticalEdge) points.Reverse();
            foreach (var (x, y) in points) path.LineTo(x, y);
        }
    }

    /// <summary>How far each side of a contour's rectangle sits inside the border box (see <see cref="CornerShapePath.AddRectangle(GraphicsPath, Rect, BorderRadii, CornerOverhang)"/>).</summary>
    internal readonly record struct CornerOverhang(double Left, double Top, double Right, double Bottom);
}
