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
        private static readonly ConditionalWeakTable<CssBox, Outline> Cache = [];

        /// <summary>Whether <paramref name="floatBox"/> declares a shape other than <c>none</c>.</summary>
        public static bool HasShape(CssBox floatBox) =>
            floatBox.ShapeOutside is { Length: > 0 } text && !text.Trim().Equals("none", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The x a line in <c>[top, top + height)</c> must start at to clear the left float <paramref name="floatBox"/>: the
        /// right edge of its margin box, or of its shape when it has one. When the shape does not reach the band the line is
        /// not narrowed, and <paramref name="clearX"/> (the line's own start) is returned.
        /// </summary>
        public static double LeftEdge(Canvas? g, CssBox floatBox, double top, double height, double clearX, CssBox? reader = null)
        {
            // In the frame of the lines of reader, when given: where the float is in flow, not drawn (CSS 2.1 section 9.4.3). A shape is not moved vertically.
            var dx = reader is null ? 0 : floatBox.FrameDeltaFor(reader).X;
            var marginRight = floatBox.FlowRightEdge + dx + floatBox.ActualMarginRight;
            if (g is null || !HasShape(floatBox)) return marginRight;

            // No outline at all (an image that has not loaded, a shape that does not resolve) leaves the rectangle; an outline that
            // misses this band leaves the line unnarrowed.
            if (Resolve(g, floatBox) is not { } outline) return marginRight;
            var edge = outline.Extent(top, Math.Max(height, 0.01), left: true);
            return edge is null ? clearX : Math.Min(edge.Value + dx, marginRight);
        }

        /// <summary>The mirror of <see cref="LeftEdge"/> for a right float: the x a line must end at (<paramref name="clearX"/> when the shape misses the band).</summary>
        public static double RightEdge(Canvas? g, CssBox floatBox, double top, double height, double clearX, CssBox? reader = null)
        {
            var dx = reader is null ? 0 : floatBox.FrameDeltaFor(reader).X;
            var marginLeft = floatBox.FlowLeftEdge + dx - floatBox.ActualMarginLeft;
            if (g is null || !HasShape(floatBox)) return marginLeft;

            if (Resolve(g, floatBox) is not { } outline) return marginLeft;
            var edge = outline.Extent(top, Math.Max(height, 0.01), left: false);
            return edge is null ? clearX : Math.Max(edge.Value + dx, marginLeft);
        }

        private static Outline? Resolve(Canvas g, CssBox floatBox)
        {
            var borderBox = new Rect(floatBox.Location.X, floatBox.Location.Y,
                Math.Max(0, floatBox.ActualRight - floatBox.Location.X), Math.Max(0, floatBox.ActualBottom - floatBox.Location.Y));
            var key = (borderBox, floatBox.ShapeOutside, floatBox.ShapeMargin, floatBox.ShapeImageThreshold, floatBox.ShapeOutsideImage?.Image,
                floatBox.ActualMarginLeft, floatBox.ActualMarginTop, floatBox.ActualMarginRight, floatBox.ActualMarginBottom);

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
            if (text.Contains("url(", StringComparison.OrdinalIgnoreCase)) return BuildImage(floatBox, text, borderBox, key);
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
                var marginLeft = floatBox.FlowLeftEdge - floatBox.ActualMarginLeft;
                var marginTop = floatBox.Location.Y - floatBox.ActualMarginTop;
                var marginBox = new Rect(marginLeft, marginTop,
                    floatBox.ActualRight + floatBox.ActualMarginRight - marginLeft,
                    floatBox.ActualBottom + floatBox.ActualMarginBottom - marginTop);
                return new Outline(key, contours, margin, marginBox, valid: contours.Count > 0);
            }
        }

        /// <summary>
        /// An <c>&lt;image&gt;</c> shape (css-shapes-1 §2.1): the image is stretched over the float's content box and a pixel is part of the
        /// shape when its alpha is above <c>shape-image-threshold</c>. Only raster images are read; one that has not loaded, or an SVG, leaves
        /// the float with its rectangle.
        /// </summary>
        private static Outline BuildImage(CssBox floatBox, string text, Rect borderBox, object key)
        {
            var invalid = new Outline(key, [], 0, default, valid: false);
            if (floatBox.ShapeOutsideImage?.Image?.GetPixels() is not { } pixels || pixels.Width <= 0 || pixels.Height <= 0) return invalid;

            var marginLeft = floatBox.FlowLeftEdge - floatBox.ActualMarginLeft;
            var marginTop = floatBox.Location.Y - floatBox.ActualMarginTop;
            var marginBox = new Rect(marginLeft, marginTop,
                floatBox.ActualRight + floatBox.ActualMarginRight - marginLeft,
                floatBox.ActualBottom + floatBox.ActualMarginBottom - marginTop);

            // §2.1: the image is sized and positioned as a replaced element with the float's used content box size.
            var reference = new Rect(borderBox.X + floatBox.ActualBorderLeftWidth + floatBox.ActualPaddingLeft,
                borderBox.Y + floatBox.ActualBorderTopWidth + floatBox.ActualPaddingTop,
                Math.Max(0, borderBox.Width - floatBox.ActualBorderLeftWidth - floatBox.ActualBorderRightWidth - floatBox.ActualPaddingLeft - floatBox.ActualPaddingRight),
                Math.Max(0, borderBox.Height - floatBox.ActualBorderTopWidth - floatBox.ActualBorderBottomWidth - floatBox.ActualPaddingTop - floatBox.ActualPaddingBottom));

            if (reference.Width <= 0 || reference.Height <= 0) return invalid;

            var threshold = double.TryParse(floatBox.ShapeImageThreshold, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var t)
                ? Math.Clamp(t, 0, 1) : 0;
            var limit = threshold * 255;

            // Per pixel row: the leftmost and rightmost pixel whose alpha is above the threshold, mapped to the reference box.
            var data = pixels.PremultipliedRgba.Span;
            var rows = new (double Min, double Max)?[pixels.Height];
            var any = false;
            for (var y = 0; y < pixels.Height; y++)
            {
                int first = -1, last = -1;
                var rowStart = y * pixels.Width * 4;
                for (var x = 0; x < pixels.Width; x++)
                {
                    if (data[rowStart + (x * 4) + 3] > limit)
                    {
                        if (first < 0) first = x;
                        last = x;
                    }
                }

                if (first < 0) continue;
                rows[y] = (reference.X + (reference.Width * first / pixels.Width), reference.X + (reference.Width * (last + 1) / pixels.Width));
                any = true;
            }

            if (!any) return invalid;

            var margin = string.IsNullOrWhiteSpace(floatBox.ShapeMargin)
                ? 0
                : Math.Max(0, CssValueParser.ParseLength(floatBox.ShapeMargin, floatBox.ContainingBlock?.AvailableWidth ?? 0, floatBox));
            return new Outline(key, [], margin, marginBox, valid: true) { MaskRows = rows, MaskTop = reference.Y, MaskHeight = reference.Height };
        }

        private sealed class Outline(object key, List<(double X, double Y)[]> contours, double margin, Rect marginBox, bool valid)
        {
            /// <summary>For an image shape: per pixel row of the image, the x range of the pixels in the shape (null for an empty row).</summary>
            public (double Min, double Max)?[]? MaskRows { get; init; }

            public double MaskTop { get; init; }

            public double MaskHeight { get; init; }

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
                if (MaskRows is { } mask)
                {
                    var index = (int)Math.Floor((y - MaskTop) / MaskHeight * mask.Length);
                    if (index < 0 || index >= mask.Length || mask[index] is not { } range) return null;
                    return left ? range.Max : range.Min;
                }

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
