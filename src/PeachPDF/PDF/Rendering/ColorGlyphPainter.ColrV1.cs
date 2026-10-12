#region PeachPDF - A .NET library for rendering HTML to PDF
//
// The PDF half of color-glyph painting. Walking a glyph's COLR v0 layers / v1 paint graph (palette
// resolution, gradient stops and extend modes, transform composition, clip bounds, blend modes) is
// backend-neutral and lives in PeachDrawing.Core.ColorGlyphs.ColorGlyphPainter, shared with every other
// backend; this file is only the target that maps what that walk describes onto the PDF backend's
// vector primitives: glyph-outline clips, solid fills, axial/radial/sweep gradient shadings and
// blend-mode graphics states - plus the measure pass, which accumulates ink bounds instead of drawing.
//
#endregion

using System.Linq;
using PeachDrawing.Core;
using PeachDrawing.Core.ColorGlyphs;
using PeachDrawing.Text.Outlines;
using System;
using System.Collections.Generic;
using PeachPDF.PDF.Drawing;

namespace PeachPDF.PDF.Rendering
{
    internal sealed partial class ColorGlyphPainter
    {
        private PeachDrawing.Core.ColorGlyphs.ColorGlyphPainter? _shared;

        /// <summary>The backend-neutral walker for this painter's typeface, palette and text color.</summary>
        private PeachDrawing.Core.ColorGlyphs.ColorGlyphPainter Shared => _shared ??= new PeachDrawing.Core.ColorGlyphs.ColorGlyphPainter(
            _typeface, _scale * _typeface.Metrics.UnitsPerEm, ToPaintColor(_foreground), _pageDownwards, _paletteIndex, ToPaintOverrides(_overrides));

        private static PaintColor ToPaintColor(PaintColor color) => color.ToRgbColor();

        private static PaintColor ToXColor(PaintColor color) => PaintColor.FromArgb(color.A, color.R, color.G, color.B);

        private static Dictionary<int, PaintColor>? ToPaintOverrides(IReadOnlyDictionary<int, PaintColor>? overrides)
        {
            if (overrides is not { Count: > 0 })
                return null;

            var converted = new Dictionary<int, PaintColor>(overrides.Count);
            foreach (var (entry, color) in overrides)
                converted[entry] = ToPaintColor(color);
            return converted;
        }

        private void PaintGlyph(int glyphId, double originX, double originYOffset = 0) =>
            Shared.Paint((ushort)glyphId, Placement(originX, originYOffset), new PdfTarget(this));

        /// <summary>Draws the shared walker's output into this painter's PdfGraphics, or - during the measure pass - only accumulates its bounds.</summary>
        private sealed class PdfTarget(ColorGlyphPainter owner) : IColorGlyphTarget
        {
            // One entry per PushOutlineClip: the graphics state to restore (null in the measure pass, which pushes nothing).
            private readonly Stack<PdfGraphicsStateToken?> _clips = new();
            private readonly Stack<PdfGraphicsStateToken?> _blends = new();

            public void FillOutline(GlyphOutline outline, Affine2x3 transform, PaintColor color)
            {
                if (owner._measuring)
                {
                    owner.IncludeInMeasuredBounds(WorldBounds(outline, transform));
                    return;
                }

                owner._gfx.DrawPath(new SolidBrush(ToXColor(color)), BuildPath(outline, transform));
            }

            public void PushOutlineClip(GlyphOutline outline, Affine2x3 transform)
            {
                // Measuring only needs the fill rectangles the leaves below produce - building and pushing the real clip
                // path would draw nothing and cost the path anyway.
                if (owner._measuring)
                {
                    _clips.Push(null);
                    return;
                }

                GraphicsPath clipPath = BuildPath(outline, transform);
                _clips.Push(owner._gfx.Save());
                owner._gfx.IntersectClip(clipPath);
            }

            public bool PushOutlineComplementClip(GlyphOutline outline, Affine2x3 transform, Rect bounds)
            {
                if (owner._measuring)
                {
                    _clips.Push(null);
                    return true;
                }

                GraphicsPath clipPath = BuildPath(outline, transform);
                clipPath.FillMode = FillMode.EvenOdd;
                clipPath.AddRectangle(new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
                _clips.Push(owner._gfx.Save());
                owner._gfx.IntersectClip(clipPath);
                return true;
            }

            public void PopClip()
            {
                if (_clips.Pop() is { } state)
                    owner._gfx.Restore(state);
            }

            public void FillRegion(Rect region, ColorGlyphPaint paint)
            {
                var rect = new Rect(region.X, region.Y, region.Width, region.Height);

                // Every v1 leaf fill is bounded by its enclosing glyph clip, so accumulating these rectangles bounds all
                // of the glyph's ink - see IncludeInMeasuredBounds.
                if (owner._measuring)
                {
                    owner.IncludeInMeasuredBounds(rect);
                    return;
                }

                var path = new PdfGraphicsPath { FillMode = FillMode.Nonzero };
                path.AddRectangle(rect);
                owner._gfx.DrawPath(ToBrush(paint), path);
            }

            public void PushBlendMode(PaintBlendMode mode)
            {
                if (owner._measuring)
                {
                    _blends.Push(null);
                    return;
                }

                _blends.Push(owner._gfx.Save());
                owner._renderer.SetBlendMode(mode.ToString());
            }

            public void PopBlendMode()
            {
                if (_blends.Pop() is { } state)
                    owner._gfx.Restore(state);
            }

            private static Brush ToBrush(ColorGlyphPaint paint)
            {
                switch (paint)
                {
                    case SolidColorGlyphPaint solid:
                        return new SolidBrush(ToXColor(solid.Color));

                    case LinearColorGlyphPaint linear:
                        return new LinearGradientBrush(new PaintPoint(linear.Start.X, linear.Start.Y), new PaintPoint(linear.End.X, linear.End.Y),
                            ToStops(linear.Colors, linear.Positions), GradientSpread.Pad);

                    case RadialColorGlyphPaint radial:
                        return new RadialGradientBrush(new PaintPoint(radial.Center.X, radial.Center.Y), new PaintPoint(radial.Focal.X, radial.Focal.Y),
                            radial.Radius, radial.Radius, ToStops(radial.Colors, radial.Positions), GradientSpread.Pad)
                        { FocusRadius = radial.FocalRadius };

                    case SweepColorGlyphPaint sweep:
                        return new ConicGradientBrush(new PaintPoint(sweep.Center.X, sweep.Center.Y), sweep.Radius,
                            ToStops(sweep.Colors, sweep.AnglesRadians.Select(angle => angle / (2 * Math.PI)).ToArray()), [.. sweep.AnglesRadians]);

                    default:
                        throw new NotSupportedException("Unknown color glyph paint " + paint.GetType().Name);
                }
            }

            private static GradientStop[] ToStops(IReadOnlyList<PaintColor> colors, IReadOnlyList<double> positions)
            {
                var stops = new GradientStop[colors.Count];
                for (int i = 0; i < stops.Length; i++)
                    stops[i] = new GradientStop(ToXColor(colors[i]), positions[i]);
                return stops;
            }

            private static PaintColor[] ToXColors(IReadOnlyList<PaintColor> colors)
            {
                var converted = new PaintColor[colors.Count];
                for (int i = 0; i < converted.Length; i++)
                    converted[i] = ToXColor(colors[i]);
                return converted;
            }

            private static GraphicsPath BuildPath(GlyphOutline outline, Affine2x3 transform)
            {
                var path = new PdfGraphicsPath { FillMode = FillMode.Nonzero };

                for (var ci = 0; ci < outline.Contours.Count; ci++)
                {
                    OutlineContour contour = outline.Contours[ci];
                    PaintPoint start = Map(transform, contour.Start.X, contour.Start.Y);
                    path.Start(start.X, start.Y);
                    for (var si = 0; si < contour.Segments.Count; si++)
                    {
                        OutlineSegment segment = contour.Segments[si];
                        PaintPoint end = Map(transform, segment.End.X, segment.End.Y);
                        if (segment.IsCubic)
                        {
                            PaintPoint c1 = Map(transform, segment.Control1.X, segment.Control1.Y);
                            PaintPoint c2 = Map(transform, segment.Control2.X, segment.Control2.Y);
                            path.AddBezierTo(c1.X, c1.Y, c2.X, c2.Y, end.X, end.Y);
                        }
                        else
                        {
                            path.LineTo(end.X, end.Y);
                        }
                    }

                    path.CloseFigure();
                }

                return path;
            }

            private static PaintPoint Map(Affine2x3 t, double x, double y) => new(t.XX * x + t.XY * y + t.DX, t.YX * x + t.YY * y + t.DY);

            private static Rect WorldBounds(GlyphOutline outline, Affine2x3 t)
            {
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;

                void Include(double x, double y)
                {
                    PaintPoint p = Map(t, x, y);
                    if (p.X < minX) minX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y > maxY) maxY = p.Y;
                }

                for (var ci = 0; ci < outline.Contours.Count; ci++)
                {
                    OutlineContour contour = outline.Contours[ci];
                    Include(contour.Start.X, contour.Start.Y);
                    for (var si = 0; si < contour.Segments.Count; si++)
                    {
                        OutlineSegment s = contour.Segments[si];
                        Include(s.End.X, s.End.Y);
                        if (s.IsCubic)
                        {
                            Include(s.Control1.X, s.Control1.Y);
                            Include(s.Control2.X, s.Control2.Y);
                        }
                    }
                }

                return maxX < minX || maxY < minY ? new Rect(0, 0, 0, 0) : new Rect(minX, minY, maxX - minX, maxY - minY);
            }
        }
    }
}
