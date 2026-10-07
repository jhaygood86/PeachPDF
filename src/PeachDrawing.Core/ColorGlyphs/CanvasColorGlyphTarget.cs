using PeachDrawing.Text.Outlines;
using System;
using System.Collections.Generic;

namespace PeachDrawing.Core.ColorGlyphs
{
    /// <summary>
    /// An <see cref="IColorGlyphTarget"/> that draws onto any <see cref="Canvas"/> with the canvas's own path, clip, brush and
    /// blend-mode primitives, so every backend gets color glyphs without implementing anything color-font specific.
    /// Coordinates are the canvas's own user units.
    /// </summary>
    public sealed class CanvasColorGlyphTarget : IColorGlyphTarget
    {
        private readonly Canvas _canvas;
        private readonly Stack<GraphicsPath> _clips = new();

        /// <summary>Creates a target that draws onto <paramref name="canvas"/>.</summary>
        /// <param name="canvas">the canvas to draw onto</param>
        public CanvasColorGlyphTarget(Canvas canvas)
        {
            ArgumentNullException.ThrowIfNull(canvas);
            _canvas = canvas;
        }

        /// <inheritdoc/>
        public void FillOutline(GlyphOutline outline, Affine2x3 transform, PaintColor color)
        {
            using GraphicsPath path = BuildPath(outline, transform);
            _canvas.DrawPath(_canvas.GetSolidBrush(color), path);
        }

        /// <summary>Whether the canvas adds <see cref="PaintBlendMode.Plus"/> composites (true for a pixel canvas such as <c>RasterCanvas</c>; default false).</summary>
        public bool SupportsAdditiveComposite { get; init; }

        /// <summary>Whether the canvas's radial brushes repeat between circles with different centers (true for a pixel canvas such as <c>RasterCanvas</c>; default false).</summary>
        public bool SupportsPeriodicConeGradients { get; init; }

        /// <inheritdoc/>
        public void PushOutlineClip(GlyphOutline outline, Affine2x3 transform)
        {
            GraphicsPath path = BuildPath(outline, transform);
            _clips.Push(path);
            _canvas.PushClip(path);
        }

        /// <inheritdoc/>
        public bool PushOutlineComplementClip(GlyphOutline outline, Affine2x3 transform, Rect bounds)
        {
            GraphicsPath path = BuildPath(outline, transform);
            path.FillMode = FillMode.EvenOdd;
            path.AddMove(bounds.X, bounds.Y);
            path.LineTo(bounds.X + bounds.Width, bounds.Y);
            path.LineTo(bounds.X + bounds.Width, bounds.Y + bounds.Height);
            path.LineTo(bounds.X, bounds.Y + bounds.Height);
            path.CloseFigure();
            _clips.Push(path);
            _canvas.PushClip(path);
            return true;
        }

        /// <inheritdoc/>
        public void PopClip()
        {
            _canvas.PopClip();
            _clips.Pop().Dispose();
        }

        /// <inheritdoc/>
        public void FillRegion(Rect region, ColorGlyphPaint paint)
        {
            Brush? brush = paint switch
            {
                SolidColorGlyphPaint solid => _canvas.GetSolidBrush(solid.Color),
                LinearColorGlyphPaint linear => _canvas.GetLinearGradientBrush(linear.Start, linear.End, Stops(linear.Colors, linear.Positions)),
                RadialColorGlyphPaint radial => radial.FocalRadius > 0
                    ? _canvas.GetRadialGradientBrush(radial.Center, radial.Radius, radial.Radius, Stops(radial.Colors, radial.Positions), radial.Repeating, radial.Focal, radial.FocalRadius)
                    : _canvas.GetRadialGradientBrush(radial.Center, radial.Radius, radial.Radius, Stops(radial.Colors, radial.Positions), radial.Repeating, radial.Focal),
                SweepColorGlyphPaint sweep => SweepBrush(sweep),
                _ => null,
            };

            if (brush is not null)
                _canvas.DrawRectangle(brush, region.X, region.Y, region.Width, region.Height);
        }

        /// <inheritdoc/>
        public void PushBlendMode(PaintBlendMode mode) => _canvas.PushBlendMode(mode);

        /// <inheritdoc/>
        public void PopBlendMode() => _canvas.PopBlendMode();

        /// <summary>
        /// A conic brush's stop angles must ascend, and a sweep's do not have to: COLR counts angles counter-clockwise while a conic brush
        /// counts clockwise, so the painter's angles descend. Reversing the stop order turns that into the same picture with ascending angles.
        /// </summary>
        private Brush SweepBrush(SweepColorGlyphPaint sweep)
        {
            PaintColor[] colors = [.. sweep.Colors];
            double[] angles = [.. sweep.AnglesRadians];
            if (angles.Length > 1 && angles[0] > angles[^1])
            {
                Array.Reverse(colors);
                Array.Reverse(angles);
            }

            return _canvas.GetConicGradientBrush(sweep.Center, sweep.Radius, colors, angles);
        }

        private static (PaintColor PaintColor, double Position)[] Stops(IReadOnlyList<PaintColor> colors, IReadOnlyList<double> positions)
        {
            var stops = new (PaintColor, double)[colors.Count];
            for (int i = 0; i < stops.Length; i++)
                stops[i] = (colors[i], positions[i]);
            return stops;
        }

        private GraphicsPath BuildPath(GlyphOutline outline, Affine2x3 t)
        {
            GraphicsPath path = _canvas.GetGraphicsPath();
            path.FillMode = FillMode.Nonzero;

            for (var ci = 0; ci < outline.Contours.Count; ci++)
            {
                OutlineContour contour = outline.Contours[ci];
                path.AddMove(X(t, contour.Start.X, contour.Start.Y), Y(t, contour.Start.X, contour.Start.Y));
                for (var si = 0; si < contour.Segments.Count; si++)
                {
                    OutlineSegment s = contour.Segments[si];
                    if (s.IsCubic)
                    {
                        path.AddBezierTo(
                            X(t, s.Control1.X, s.Control1.Y), Y(t, s.Control1.X, s.Control1.Y),
                            X(t, s.Control2.X, s.Control2.Y), Y(t, s.Control2.X, s.Control2.Y),
                            X(t, s.End.X, s.End.Y), Y(t, s.End.X, s.End.Y));
                    }
                    else
                    {
                        path.LineTo(X(t, s.End.X, s.End.Y), Y(t, s.End.X, s.End.Y));
                    }
                }

                path.CloseFigure();
            }

            return path;
        }

        private static double X(Affine2x3 t, double x, double y) => t.XX * x + t.XY * y + t.DX;

        private static double Y(Affine2x3 t, double x, double y) => t.YX * x + t.YY * y + t.DY;
    }
}
