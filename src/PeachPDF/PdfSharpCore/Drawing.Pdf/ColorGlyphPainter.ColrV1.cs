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

using PeachDrawing.Core;
using PeachDrawing.Core.ColorGlyphs;
using PeachDrawing.Text.Outlines;
using System;
using System.Collections.Generic;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.PdfSharpCore.Drawing.Pdf
{
    internal sealed partial class ColorGlyphPainter
    {
        private PeachDrawing.Core.ColorGlyphs.ColorGlyphPainter? _shared;

        /// <summary>The backend-neutral walker for this painter's typeface, palette and text color.</summary>
        private PeachDrawing.Core.ColorGlyphs.ColorGlyphPainter Shared => _shared ??= new PeachDrawing.Core.ColorGlyphs.ColorGlyphPainter(
            _typeface, _scale * _typeface.Metrics.UnitsPerEm, ToPaintColor(_foreground), _pageDownwards, _paletteIndex, ToPaintOverrides(_overrides));

        private static PaintColor ToPaintColor(XColor color) =>
            PaintColor.FromArgb((int)Math.Round(color.A * 255), color.R, color.G, color.B);

        private static XColor ToXColor(PaintColor color) => XColor.FromArgb(color.A, color.R, color.G, color.B);

        private static Dictionary<int, PaintColor>? ToPaintOverrides(IReadOnlyDictionary<int, XColor>? overrides)
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

        /// <summary>Draws the shared walker's output into this painter's XGraphics, or - during the measure pass - only accumulates its bounds.</summary>
        private sealed class PdfTarget(ColorGlyphPainter owner) : IColorGlyphTarget
        {
            // One entry per PushOutlineClip: the graphics state to restore (null in the measure pass, which pushes nothing).
            private readonly Stack<XGraphicsState?> _clips = new();
            private readonly Stack<XGraphicsState?> _blends = new();

            public void FillOutline(GlyphOutline outline, Affine2x3 transform, PaintColor color)
            {
                if (owner._measuring)
                {
                    owner.IncludeInMeasuredBounds(WorldBounds(outline, transform));
                    return;
                }

                owner._gfx.DrawPath(new XSolidBrush(ToXColor(color)), BuildPath(outline, transform));
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

                XGraphicsPath clipPath = BuildPath(outline, transform);
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

                XGraphicsPath clipPath = BuildPath(outline, transform);
                clipPath.FillMode = XFillMode.Alternate;
                clipPath.AddRectangle(new XRect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
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
                var rect = new XRect(region.X, region.Y, region.Width, region.Height);

                // Every v1 leaf fill is bounded by its enclosing glyph clip, so accumulating these rectangles bounds all
                // of the glyph's ink - see IncludeInMeasuredBounds.
                if (owner._measuring)
                {
                    owner.IncludeInMeasuredBounds(rect);
                    return;
                }

                var path = new XGraphicsPath { FillMode = XFillMode.Winding };
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

            private static XBrush ToBrush(ColorGlyphPaint paint)
            {
                switch (paint)
                {
                    case SolidColorGlyphPaint solid:
                        return new XSolidBrush(ToXColor(solid.Color));

                    case LinearColorGlyphPaint linear:
                        return new XLinearGradientBrush(new XPoint(linear.Start.X, linear.Start.Y), new XPoint(linear.End.X, linear.End.Y),
                            ToXColors(linear.Colors), [.. linear.Positions]);

                    case RadialColorGlyphPaint radial:
                        return new XRadialGradientBrush(new XPoint(radial.Center.X, radial.Center.Y), radial.Radius, radial.Radius,
                            ToXColors(radial.Colors), [.. radial.Positions], new XPoint(radial.Focal.X, radial.Focal.Y))
                        { FocalRadius = radial.FocalRadius };

                    case SweepColorGlyphPaint sweep:
                        return new XConicGradientBrush(new XPoint(sweep.Center.X, sweep.Center.Y), sweep.Radius,
                            ToXColors(sweep.Colors), [.. sweep.AnglesRadians]);

                    default:
                        throw new NotSupportedException("Unknown color glyph paint " + paint.GetType().Name);
                }
            }

            private static XColor[] ToXColors(IReadOnlyList<PaintColor> colors)
            {
                var converted = new XColor[colors.Count];
                for (int i = 0; i < converted.Length; i++)
                    converted[i] = ToXColor(colors[i]);
                return converted;
            }

            private static XGraphicsPath BuildPath(GlyphOutline outline, Affine2x3 transform)
            {
                int pointCount = outline.Contours.Count;
                for (var ci = 0; ci < outline.Contours.Count; ci++)
                {
                    OutlineContour contour = outline.Contours[ci];
                    for (var si = 0; si < contour.Segments.Count; si++)
                        pointCount += contour.Segments[si].IsCubic ? 3 : 1;
                }

                var path = new XGraphicsPath(pointCount) { FillMode = XFillMode.Winding };

                for (var ci = 0; ci < outline.Contours.Count; ci++)
                {
                    OutlineContour contour = outline.Contours[ci];
                    XPoint current = Map(transform, contour.Start.X, contour.Start.Y);
                    for (var si = 0; si < contour.Segments.Count; si++)
                    {
                        OutlineSegment segment = contour.Segments[si];
                        XPoint end = Map(transform, segment.End.X, segment.End.Y);
                        if (segment.IsCubic)
                        {
                            XPoint c1 = Map(transform, segment.Control1.X, segment.Control1.Y);
                            XPoint c2 = Map(transform, segment.Control2.X, segment.Control2.Y);
                            path.AddBezier(current.X, current.Y, c1.X, c1.Y, c2.X, c2.Y, end.X, end.Y);
                        }
                        else
                        {
                            path.AddLine(current.X, current.Y, end.X, end.Y);
                        }

                        current = end;
                    }

                    path.CloseFigure();
                }

                return path;
            }

            private static XPoint Map(Affine2x3 t, double x, double y) => new(t.XX * x + t.XY * y + t.DX, t.YX * x + t.YY * y + t.DY);

            private static XRect WorldBounds(GlyphOutline outline, Affine2x3 t)
            {
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;

                void Include(double x, double y)
                {
                    XPoint p = Map(t, x, y);
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

                return maxX < minX || maxY < minY ? new XRect(0, 0, 0, 0) : new XRect(minX, minY, maxX - minX, maxY - minY);
            }
        }
    }
}
