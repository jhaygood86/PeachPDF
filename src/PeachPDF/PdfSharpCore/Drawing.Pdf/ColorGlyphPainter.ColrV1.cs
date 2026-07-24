#region PeachPDF - A .NET library for rendering HTML to PDF
//
// COLR version 1 paint-graph interpreter for ColorGlyphPainter. Walks the
// ColrPaint tree recursively, mapping onto the PDF backend's existing vector
// primitives: glyph-outline clips, solid fills, axial/radial/sweep gradient
// shadings, affine transforms, and (via Stage 6) blend-mode compositing.
//
// Everything is painted in world space through a ColrAffine that composes the
// glyph placement with the paint graph's own transforms; a "leaf" paint (solid
// or gradient) fills the currently-clipped glyph region.
//
#endregion

using System;
using System.Collections.Generic;
using PeachPDF.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.PdfSharpCore.Drawing.Pdf
{
    internal sealed partial class ColorGlyphPainter
    {
        private const int MaxPaintDepth = 64;

        private void PaintV1(ColrPaint? paint, ColrAffine t, bool hasClip, XRect clip, int depth)
        {
            if (paint is null || depth > MaxPaintDepth)
                return;

            switch (paint)
            {
                case ColrPaintColrLayers layers:
                    for (int i = 0; i < layers.NumLayers; i++)
                        PaintV1(_descriptor.ColorTable.GetLayerPaint(layers.FirstLayerIndex + i), t, hasClip, clip, depth + 1);
                    break;

                case ColrPaintGlyph glyph:
                {
                    if (!_descriptor.TryGetGlyphOutline(glyph.GlyphId, out GlyphOutline outline) || outline.IsEmpty)
                        break;

                    XGraphicsPath clipPath = BuildPath(outline, t);
                    XRect glyphBounds = WorldBounds(outline, t);
                    XRect newClip = hasClip ? Intersect(clip, glyphBounds) : glyphBounds;

                    XGraphicsState state = _gfx.Save();
                    _gfx.IntersectClip(clipPath);
                    PaintV1(glyph.Paint, t, true, newClip, depth + 1);
                    _gfx.Restore(state);
                    break;
                }

                case ColrPaintColrGlyph colrGlyph:
                    PaintV1(_descriptor.ColorTable.GetV1BaseGlyphPaint(colrGlyph.GlyphId), t, hasClip, clip, depth + 1);
                    break;

                case ColrPaintTransform transform:
                    PaintV1(transform.Paint, ColrAffine.Multiply(t, transform.Affine), hasClip, clip, depth + 1);
                    break;

                case ColrPaintSolid solid:
                    FillClip(hasClip, clip, new XSolidBrush(ResolveColor(solid.PaletteIndex, solid.Alpha)));
                    break;

                case ColrPaintLinearGradient linear:
                    FillClip(hasClip, clip, BuildLinearBrush(linear, t));
                    break;

                case ColrPaintRadialGradient radial:
                    FillClip(hasClip, clip, BuildRadialBrush(radial, t));
                    break;

                case ColrPaintSweepGradient sweep:
                    FillClip(hasClip, clip, BuildSweepBrush(sweep, t));
                    break;

                case ColrPaintComposite composite:
                    PaintComposite(composite, t, hasClip, clip, depth);
                    break;
            }
        }

        // Compositing: paint the backdrop, then the source on top. A separable/HSL blend mode is
        // applied to the source via a PDF /BM ExtGState; Porter-Duff-only modes that PDF cannot
        // express degrade to source-over (an accepted gap).
        private void PaintComposite(ColrPaintComposite composite, ColrAffine t, bool hasClip, XRect clip, int depth)
        {
            PaintV1(composite.Backdrop, t, hasClip, clip, depth + 1);

            string? blendMode = BlendModeName(composite.Mode);
            if (blendMode is null)
            {
                PaintV1(composite.Source, t, hasClip, clip, depth + 1); // source-over
                return;
            }

            XGraphicsState state = _gfx.Save();
            _renderer.SetBlendMode(blendMode);
            PaintV1(composite.Source, t, hasClip, clip, depth + 1);
            _gfx.Restore(state);
        }

        /// <summary>
        /// Maps a COLR CompositeMode to a PDF blend-mode name, or null for source-over (the common
        /// SRC_OVER, and the fallback for Porter-Duff modes PDF cannot express natively).
        /// </summary>
        private static string? BlendModeName(int compositeMode) => compositeMode switch
        {
            13 => "Screen",
            14 => "Overlay",
            15 => "Darken",
            16 => "Lighten",
            17 => "ColorDodge",
            18 => "ColorBurn",
            19 => "HardLight",
            20 => "SoftLight",
            21 => "Difference",
            22 => "Exclusion",
            23 => "Multiply",
            24 => "Hue",
            25 => "Saturation",
            26 => "Color",
            27 => "Luminosity",
            _ => null, // SRC_OVER and the non-expressible Porter-Duff modes -> source-over
        };

        /// <summary>Fills the active clip region with a brush (a rectangle over the clip bounds, clipped).</summary>
        private void FillClip(bool hasClip, XRect clip, XBrush? brush)
        {
            if (!hasClip || brush is null || clip.Width <= 0 || clip.Height <= 0)
                return; // a leaf paint with no enclosing glyph clip is degenerate; draw nothing.

            var rect = new XGraphicsPath { FillMode = XFillMode.Winding };
            rect.AddRectangle(clip);
            _gfx.DrawPath(brush, rect);
        }

        // ---- Gradient brushes (built in world space) -------------------------------------------

        private XLinearGradientBrush? BuildLinearBrush(ColrPaintLinearGradient g, ColrAffine t)
        {
            if (!TryBuildStops(g.Line, out XColor[] colors, out double[] positions))
                return null;

            // p2 rotates the gradient; the common (perpendicular) case reduces to the p0->p1 axis.
            XPoint p0 = Map(t, g.X0, g.Y0);
            XPoint p1 = Map(t, g.X1, g.Y1);

            // repeat/reflect: PDF axial shadings only pad, so tile (or mirror) the stops over a few
            // periods and extend the gradient axis to cover them.
            if (g.Line.Extend is ColrExtend.Repeat or ColrExtend.Reflect)
                ExpandLinearExtend(ref p0, ref p1, ref colors, ref positions, g.Line.Extend);

            return new XLinearGradientBrush(p0, p1, colors, positions);
        }

        private XRadialGradientBrush? BuildRadialBrush(ColrPaintRadialGradient g, ColrAffine t)
        {
            if (!TryBuildStops(g.Line, out XColor[] colors, out double[] positions))
                return null;

            double radiusScale = Math.Sqrt(Math.Abs(t.XX * t.YY - t.XY * t.YX));
            XPoint outer = Map(t, g.X1, g.Y1);
            XPoint focal = Map(t, g.X0, g.Y0);
            double r = g.R1 * radiusScale;
            // Radial repeat/reflect extend is not modeled (pad only) - an accepted gap.
            return new XRadialGradientBrush(outer, r, r, colors, positions, focal);
        }

        private XConicGradientBrush? BuildSweepBrush(ColrPaintSweepGradient g, ColrAffine t)
        {
            if (!TryBuildStops(g.Line, out XColor[] colors, out double[] _))
                return null;

            double radiusScale = Math.Sqrt(Math.Abs(t.XX * t.YY - t.XY * t.YX));
            XPoint center = Map(t, g.CenterX, g.CenterY);
            // Give the fan a radius large enough to cover the glyph.
            double radius = System.Math.Max(_scale * _descriptor.UnitsPerEm, radiusScale * _descriptor.UnitsPerEm);

            // Map each stop's parametric offset to a sweep angle, converting the COLR convention
            // (counter-clockwise from the +x axis) to the conic-brush convention (0 = up, clockwise),
            // accounting for the y-down page flip.
            var stops = SortedStops(g.Line);
            var angles = new double[stops.Count];
            for (int i = 0; i < stops.Count; i++)
            {
                double colrAngle = g.StartAngle + stops[i].Offset * (g.EndAngle - g.StartAngle);
                angles[i] = ToConicAngle(colrAngle);
            }
            return new XConicGradientBrush(center, radius, colors, angles);
        }

        private double ToConicAngle(double colrRadians)
        {
            // COLR: ccw from +x. Conic brush: cw from +y (up). On a y-down page the visual sense of
            // "ccw" flips, so cw_from_up = 90deg - colrAngle.
            double deg = 90.0 - colrRadians * 180.0 / Math.PI;
            double rad = deg * Math.PI / 180.0;
            return _pageDownwards ? rad : -rad;
        }

        private bool TryBuildStops(ColrColorLine line, out XColor[] colors, out double[] positions)
        {
            List<ColrColorStop> stops = SortedStops(line);
            if (stops.Count == 0)
            {
                colors = [];
                positions = [];
                return false;
            }
            if (stops.Count == 1)
                stops.Add(stops[0] with { Offset = stops[0].Offset + 1e-4 });

            colors = new XColor[stops.Count];
            positions = new double[stops.Count];
            for (int i = 0; i < stops.Count; i++)
            {
                colors[i] = ResolveColor(stops[i].PaletteIndex, stops[i].Alpha);
                positions[i] = stops[i].Offset;
            }
            return true;
        }

        /// <summary>
        /// Tiles (repeat) or mirrors (reflect) the gradient stops over a few periods either side of
        /// the base [0,1] range and extends the gradient axis to cover them, since PDF axial shadings
        /// can only pad. Colors/positions are rewritten in place; p0/p1 move outward.
        /// </summary>
        private static void ExpandLinearExtend(ref XPoint p0, ref XPoint p1, ref XColor[] colors, ref double[] positions, ColrExtend extend)
        {
            const int periods = 2; // each side
            int total = 2 * periods + 1;

            var samples = new List<(double T, XColor Color)>();
            for (int p = -periods; p <= periods; p++)
            {
                bool mirror = extend == ColrExtend.Reflect && ((p % 2 + 2) % 2 == 1);
                for (int j = 0; j < positions.Length; j++)
                {
                    double local = mirror ? 1.0 - positions[j] : positions[j];
                    samples.Add((p + local, colors[j]));
                }
            }
            samples.Sort((a, b) => a.T.CompareTo(b.T));

            // Extend the axis so the new [0,1] parameter spans original t in [-periods, periods+1].
            XPoint dir = new(p1.X - p0.X, p1.Y - p0.Y);
            XPoint newP0 = new(p0.X - periods * dir.X, p0.Y - periods * dir.Y);
            p1 = new XPoint(p1.X + periods * dir.X, p1.Y + periods * dir.Y);
            p0 = newP0;

            var newColors = new XColor[samples.Count];
            var newPositions = new double[samples.Count];
            double last = -1;
            for (int i = 0; i < samples.Count; i++)
            {
                double pos = (samples[i].T + periods) / total;
                if (pos <= last)
                    pos = last + 1e-6; // keep strictly increasing for the stitching function
                last = pos;
                newColors[i] = samples[i].Color;
                newPositions[i] = pos;
            }
            colors = newColors;
            positions = newPositions;
        }

        private static List<ColrColorStop> SortedStops(ColrColorLine line)
        {
            var stops = new List<ColrColorStop>(line.Stops);
            stops.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            return stops;
        }

        // ---- Geometry helpers ------------------------------------------------------------------

        private static XRect WorldBounds(GlyphOutline outline, ColrAffine t)
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

            foreach (GlyphContour contour in outline.Contours)
            {
                Include(contour.Start.X, contour.Start.Y);
                foreach (GlyphSegment s in contour.Segments)
                {
                    Include(s.End.X, s.End.Y);
                    if (s.IsCubic)
                    {
                        Include(s.Control1.X, s.Control1.Y);
                        Include(s.Control2.X, s.Control2.Y);
                    }
                }
            }

            if (maxX < minX || maxY < minY)
                return new XRect(0, 0, 0, 0);
            return new XRect(minX, minY, maxX - minX, maxY - minY);
        }

        private static XRect Intersect(XRect a, XRect b)
        {
            a.Intersect(b);
            return a;
        }
    }
}
