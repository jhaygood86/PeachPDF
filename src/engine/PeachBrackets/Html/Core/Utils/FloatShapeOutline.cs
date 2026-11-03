using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Parse;
using PeachDrawing.Core;
using PeachDrawing.Core.Geometry;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace PeachBrackets.Html.Utils
{
    /// <summary>
    /// <c>shape-outside</c> and <c>shape-margin</c> (css-shapes-1 §2): the horizontal extent a float's shape occupies in a
    /// band of lines. The shape is resolved by <see cref="CssClipPathResolver"/> (the one basic-shape grammar
    /// <c>clip-path</c> also uses) against the float's margin box unless a <c>&lt;shape-box&gt;</c> says otherwise, flattened
    /// to polygons once per float geometry, grown by <c>shape-margin</c>, and clipped to the margin box (§2.1). Line layout
    /// asks for the edge a line band must clear; a float with <c>shape-outside: none</c> answers with its margin box, so the
    /// callers read one value either way.
    /// </summary>
    internal static class FloatShapeOutline
    {
        private static readonly ConditionalWeakTable<CssBox, Outline> Cache = new();

        /// <summary>Whether <paramref name="floatBox"/> declares a shape other than <c>none</c>.</summary>
        public static bool HasShape(CssBox floatBox) =>
            floatBox.ShapeOutside is { Length: > 0 } text && !text.Trim().Equals("none", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The x a line in <c>[top, top + height)</c> must start at to clear the left float <paramref name="floatBox"/>: the
        /// right edge of its margin box, or of its shape when it has one. When the shape does not reach the band the line is
        /// not narrowed, and <paramref name="clearX"/> (the line's own start) is returned.
        /// </summary>
        public static double LeftEdge(Canvas? g, CssBox floatBox, double top, double height, double clearX)
        {
            var marginRight = floatBox.ActualRight + floatBox.ActualMarginRight;
            if (g is null || !HasShape(floatBox)) return marginRight;

            var edge = Resolve(g, floatBox)?.Extent(top, Math.Max(height, 0.01), left: true);
            return edge is null ? clearX : Math.Min(edge.Value, marginRight);
        }

        /// <summary>The mirror of <see cref="LeftEdge"/> for a right float: the x a line must end at (<paramref name="clearX"/> when the shape misses the band).</summary>
        public static double RightEdge(Canvas? g, CssBox floatBox, double top, double height, double clearX)
        {
            var marginLeft = floatBox.Location.X - floatBox.ActualMarginLeft;
            if (g is null || !HasShape(floatBox)) return marginLeft;

            var edge = Resolve(g, floatBox)?.Extent(top, Math.Max(height, 0.01), left: false);
            return edge is null ? clearX : Math.Max(edge.Value, marginLeft);
        }

        private static Outline? Resolve(Canvas g, CssBox floatBox)
        {
            var borderBox = new Rect(floatBox.Location.X, floatBox.Location.Y,
                Math.Max(0, floatBox.ActualRight - floatBox.Location.X), Math.Max(0, floatBox.ActualBottom - floatBox.Location.Y));
            var key = (borderBox, floatBox.ShapeOutside, floatBox.ShapeMargin, floatBox.ActualMarginLeft, floatBox.ActualMarginTop,
                floatBox.ActualMarginRight, floatBox.ActualMarginBottom);

            if (Cache.TryGetValue(floatBox, out var cached) && cached.Key.Equals(key)) return cached.Valid ? cached : null;

            var outline = Build(g, floatBox, borderBox, key);
            Cache.AddOrUpdate(floatBox, outline);
            return outline.Valid ? outline : null;
        }

        private static Outline Build(Canvas g, CssBox floatBox, Rect borderBox, object key)
        {
            var text = floatBox.ShapeOutside!;
            // §2.1: the reference box defaults to margin-box, where clip-path defaults to border-box.
            if (!text.Contains("-box", StringComparison.OrdinalIgnoreCase)) text += " margin-box";

            var invalid = new Outline(key, [], 0, default, valid: false);
            if (!CssClipPathResolver.TryBuildClipPath(g, text, borderBox, floatBox, out var path, out _) || path is null) return invalid;

            using (path)
            {
                var ppp = g.PixelsPerPoint;
                var contours = new List<(double X, double Y)[]>();
                foreach (var contour in path.Flatten(0.1))
                {
                    if (contour.Points.Count < 2) continue;
                    var pts = new (double X, double Y)[contour.Points.Count];
                    for (var i = 0; i < pts.Length; i++) pts[i] = (contour.Points[i].X * ppp, contour.Points[i].Y * ppp);
                    contours.Add(pts);
                }

                var margin = string.IsNullOrWhiteSpace(floatBox.ShapeMargin)
                    ? 0
                    : Math.Max(0, CssValueParser.ParseLength(floatBox.ShapeMargin, floatBox.ContainingBlock?.AvailableWidth ?? 0, floatBox));
                var marginLeft = floatBox.Location.X - floatBox.ActualMarginLeft;
                var marginTop = floatBox.Location.Y - floatBox.ActualMarginTop;
                var marginBox = new Rect(marginLeft, marginTop,
                    floatBox.ActualRight + floatBox.ActualMarginRight - marginLeft,
                    floatBox.ActualBottom + floatBox.ActualMarginBottom - marginTop);
                return new Outline(key, contours, margin, marginBox, valid: contours.Count > 0);
            }
        }

        private sealed class Outline(object key, List<(double X, double Y)[]> contours, double margin, Rect marginBox, bool valid)
        {
            private readonly Dictionary<(double, double, bool), double?> _bands = [];

            public object Key { get; } = key;

            public bool Valid { get; } = valid;

            /// <summary>The outermost x the grown shape reaches over rows in the band, towards the line (rightmost for a left float), or null when no row of the band meets it.</summary>
            public double? Extent(double top, double height, bool left)
            {
                var bandKey = (Math.Round(top, 3), Math.Round(height, 3), left);
                if (_bands.TryGetValue(bandKey, out var known)) return known;

                double? best = null;
                var steps = (int)Math.Clamp(Math.Ceiling(height / 0.5), 1, 48);
                for (var i = 0; i <= steps; i++)
                {
                    var y = top + (height * i / steps);
                    var row = GrownRow(y, left);
                    if (row is not null) best = best is null ? row : left ? Math.Max(best.Value, row.Value) : Math.Min(best.Value, row.Value);
                }

                // Clip to the margin box (§2.1): the shape never narrows a line beyond the float's own margin edge.
                if (best is not null && marginBox.Width > 0)
                {
                    best = left ? Math.Min(best.Value, marginBox.Right) : Math.Max(best.Value, marginBox.Left);
                }

                _bands[bandKey] = best;
                return best;
            }

            /// <summary>The shape grown by <c>shape-margin</c> (a disc swept along it), read at row y.</summary>
            private double? GrownRow(double y, bool left)
            {
                if (margin <= 0) return Row(y, left);

                double? best = null;
                const int samples = 32;
                for (var i = 0; i <= samples; i++)
                {
                    var dy = -margin + (2 * margin * i / samples);
                    var row = Row(y + dy, left);
                    if (row is null) continue;
                    var reach = Math.Sqrt(Math.Max(0, (margin * margin) - (dy * dy)));
                    var x = left ? row.Value + reach : row.Value - reach;
                    best = best is null ? x : left ? Math.Max(best.Value, x) : Math.Min(best.Value, x);
                }

                return best;
            }

            /// <summary>The rightmost (or leftmost) x where the shape's outline crosses the horizontal line at <paramref name="y"/>.</summary>
            private double? Row(double y, bool left)
            {
                double? best = null;
                foreach (var pts in contours)
                {
                    for (var i = 0; i < pts.Length; i++)
                    {
                        var (ax, ay) = pts[i];
                        var (bx, by) = pts[(i + 1) % pts.Length];
                        if (y < Math.Min(ay, by) || y > Math.Max(ay, by)) continue;

                        double x;
                        if (Math.Abs(by - ay) < 1e-9)
                        {
                            // A horizontal edge on the row: its far end counts.
                            x = left ? Math.Max(ax, bx) : Math.Min(ax, bx);
                        }
                        else
                        {
                            x = ax + ((y - ay) * (bx - ax) / (by - ay));
                        }

                        best = best is null ? x : left ? Math.Max(best.Value, x) : Math.Min(best.Value, x);
                    }
                }

                return best;
            }
        }
    }
}
