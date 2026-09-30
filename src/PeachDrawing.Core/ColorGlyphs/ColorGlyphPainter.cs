using PeachDrawing.Text;
using PeachDrawing.Text.Outlines;
using System;
using System.Collections.Generic;

namespace PeachDrawing.Core.ColorGlyphs
{
    /// <summary>
    /// Walks the color artwork of one glyph - a COLR v1 paint graph, else COLR v0 layers, else the plain outline in the
    /// foreground color - and describes it to an <see cref="IColorGlyphTarget"/> as clipped fills. The walk itself is
    /// backend-neutral: palette and <c>font-palette</c> override resolution, the "use the text color" sentinel, gradient stop
    /// handling, transform composition, clip bounds and blend modes live here once, so every backend paints color fonts the
    /// same way.
    /// </summary>
    public sealed class ColorGlyphPainter
    {
        private const int UseForegroundColor = 0xFFFF;
        private const int MaxPaintDepth = 64;

        private readonly Typeface _typeface;
        private readonly PaintColor _foreground;
        private readonly int _paletteIndex;
        private readonly IReadOnlyDictionary<int, PaintColor>? _overrides;
        private readonly double _fontSize;
        private readonly bool _yDown;

        /// <summary>Creates a painter for glyphs of <paramref name="typeface"/>.</summary>
        /// <param name="typeface">the font whose glyphs are painted</param>
        /// <param name="fontSize">the font size the placements passed to <see cref="Paint"/> scale design units to, in target units</param>
        /// <param name="foreground">the text color, used for layers that ask for it and for glyphs with no color artwork</param>
        /// <param name="yDown">whether the target's y axis points down (a page or bitmap), which decides the direction of sweep gradients</param>
        /// <param name="paletteIndex">the CPAL palette to take colors from</param>
        /// <param name="overrides">CPAL entry index to replacement color, or null for none</param>
        public ColorGlyphPainter(Typeface typeface, double fontSize, PaintColor foreground, bool yDown = true,
            int paletteIndex = 0, IReadOnlyDictionary<int, PaintColor>? overrides = null)
        {
            ArgumentNullException.ThrowIfNull(typeface);
            _typeface = typeface;
            _fontSize = fontSize;
            _foreground = foreground;
            _yDown = yDown;
            _paletteIndex = paletteIndex;
            _overrides = overrides is { Count: > 0 } ? overrides : null;
        }

        /// <summary>
        /// The placement that maps a glyph's design units into a target whose baseline origin for the glyph is (<paramref name="originX"/>,
        /// <paramref name="originY"/>): scaled to the font size, with y flipped when the target's y axis points down.
        /// </summary>
        /// <param name="originX">the glyph origin's x in target space</param>
        /// <param name="originY">the glyph origin's y (on the baseline) in target space</param>
        /// <returns>the design-units-to-target affine to pass to <see cref="Paint"/></returns>
        public Affine2x3 Placement(double originX, double originY)
        {
            double scale = _fontSize / _typeface.Metrics.UnitsPerEm;
            return new Affine2x3(scale, 0, 0, _yDown ? -scale : scale, originX, originY);
        }

        /// <summary>Paints glyph <paramref name="glyphId"/>, placed by <paramref name="placement"/> (see <see cref="Placement"/>), to <paramref name="target"/>.</summary>
        /// <param name="glyphId">the glyph to paint</param>
        /// <param name="placement">design units to target space</param>
        /// <param name="target">where the artwork is drawn</param>
        public void Paint(ushort glyphId, Affine2x3 placement, IColorGlyphTarget target)
        {
            ArgumentNullException.ThrowIfNull(target);

            // Per the COLR processing model a v1-aware renderer resolves the v1 BaseGlyphList first,
            // falling back to the v0 layer records only when the glyph has no v1 paint.
            if (_typeface.GetColorPaint(glyphId) is { } paint)
            {
                PaintV1(paint, placement, hasClip: false, clip: default, depth: 0, target);
                return;
            }

            if (_typeface.TryGetColorLayers(glyphId, out var layers))
            {
                for (int i = 0; i < layers.Count; i++)
                    FillOutline(layers[i].GlyphId, placement, ResolveColor(layers[i].PaletteIndex, 1.0), target);
                return;
            }

            // A glyph with no color record inside a color font (space, digits): its plain outline in the text color.
            FillOutline(glyphId, placement, _foreground, target);
        }

        private void FillOutline(int glyphId, Affine2x3 transform, PaintColor color, IColorGlyphTarget target)
        {
            if (_typeface.TryGetOutline((ushort)glyphId, out GlyphOutline outline) && !outline.IsEmpty)
                target.FillOutline(outline, transform, color);
        }

        private void PaintV1(ColorPaint? paint, Affine2x3 t, bool hasClip, Rect clip, int depth, IColorGlyphTarget target)
        {
            if (paint is null || depth > MaxPaintDepth)
                return;

            switch (paint)
            {
                case PaintColrLayers layers:
                    for (int i = 0; i < layers.NumLayers; i++)
                        PaintV1(_typeface.GetColorLayerPaint(layers.FirstLayerIndex + i), t, hasClip, clip, depth + 1, target);
                    break;

                case PaintGlyph glyph:
                {
                    if (!_typeface.TryGetOutline((ushort)glyph.GlyphId, out GlyphOutline outline) || outline.IsEmpty)
                        break;

                    Rect glyphBounds = WorldBounds(outline, t);
                    Rect newClip = hasClip ? Rect.Intersect(clip, glyphBounds) : glyphBounds;
                    target.PushOutlineClip(outline, t);
                    PaintV1(glyph.Paint, t, true, newClip, depth + 1, target);
                    target.PopClip();
                    break;
                }

                case PaintColrGlyph colrGlyph:
                    PaintV1(_typeface.GetColorPaint((ushort)colrGlyph.GlyphId), t, hasClip, clip, depth + 1, target);
                    break;

                case PaintTransform transform:
                    PaintV1(transform.Paint, Affine2x3.Multiply(t, transform.Affine), hasClip, clip, depth + 1, target);
                    break;

                case PaintSolid solid:
                    FillClip(hasClip, clip, new SolidColorGlyphPaint(ResolveColor(solid.PaletteIndex, solid.Alpha)), target);
                    break;

                case PaintLinearGradient linear:
                    FillClip(hasClip, clip, BuildLinear(linear, t), target);
                    break;

                case PaintRadialGradient radial:
                    FillClip(hasClip, clip, BuildRadial(radial, t), target);
                    break;

                case PaintSweepGradient sweep:
                    FillClip(hasClip, clip, BuildSweep(sweep, t), target);
                    break;

                case PaintComposite composite:
                    PaintCompositeNode(composite, t, hasClip, clip, depth, target);
                    break;
            }
        }

        // Compositing: paint the backdrop, then the source on top. A separable/HSL blend mode is applied to the
        // source; Porter-Duff-only modes the targets cannot express degrade to source-over.
        private void PaintCompositeNode(PaintComposite composite, Affine2x3 t, bool hasClip, Rect clip, int depth, IColorGlyphTarget target)
        {
            PaintV1(composite.Backdrop, t, hasClip, clip, depth + 1, target);

            if (BlendModeFor(composite.Mode) is not { } mode)
            {
                PaintV1(composite.Source, t, hasClip, clip, depth + 1, target); // source-over
                return;
            }

            target.PushBlendMode(mode);
            PaintV1(composite.Source, t, hasClip, clip, depth + 1, target);
            target.PopBlendMode();
        }

        /// <summary>A COLR CompositeMode as a blend mode, or null for source-over (SRC_OVER, and the Porter-Duff modes nothing expresses).</summary>
        private static PaintBlendMode? BlendModeFor(int compositeMode) => compositeMode switch
        {
            13 => PaintBlendMode.Screen,
            14 => PaintBlendMode.Overlay,
            15 => PaintBlendMode.Darken,
            16 => PaintBlendMode.Lighten,
            17 => PaintBlendMode.ColorDodge,
            18 => PaintBlendMode.ColorBurn,
            19 => PaintBlendMode.HardLight,
            20 => PaintBlendMode.SoftLight,
            21 => PaintBlendMode.Difference,
            22 => PaintBlendMode.Exclusion,
            23 => PaintBlendMode.Multiply,
            24 => PaintBlendMode.Hue,
            25 => PaintBlendMode.Saturation,
            26 => PaintBlendMode.Color,
            27 => PaintBlendMode.Luminosity,
            _ => null,
        };

        private static void FillClip(bool hasClip, Rect clip, ColorGlyphPaint? paint, IColorGlyphTarget target)
        {
            // A leaf paint with no enclosing glyph clip is degenerate; draw nothing.
            if (!hasClip || paint is null || clip.Width <= 0 || clip.Height <= 0)
                return;

            target.FillRegion(clip, paint);
        }

        // ---- Gradient paints (in target space) -------------------------------------------------

        private LinearColorGlyphPaint? BuildLinear(PaintLinearGradient g, Affine2x3 t)
        {
            if (!TryBuildStops(g.Line, out PaintColor[] colors, out double[] positions))
                return null;

            // p2 rotates the gradient; the common (perpendicular) case reduces to the p0->p1 axis.
            PaintPoint p0 = Map(t, g.X0, g.Y0);
            PaintPoint p1 = Map(t, g.X1, g.Y1);

            // repeat/reflect: an axial gradient only pads, so tile (or mirror) the stops over a few periods and extend
            // the gradient axis to cover them.
            if (g.Line.Extend is ColorExtend.Repeat or ColorExtend.Reflect)
                ExpandLinearExtend(ref p0, ref p1, ref colors, ref positions, g.Line.Extend);

            return new LinearColorGlyphPaint(p0, p1, colors, positions);
        }

        private RadialColorGlyphPaint? BuildRadial(PaintRadialGradient g, Affine2x3 t)
        {
            if (!TryBuildStops(g.Line, out PaintColor[] colors, out double[] positions))
                return null;

            double radiusScale = Math.Sqrt(Math.Abs(t.XX * t.YY - t.XY * t.YX));
            // Radial repeat/reflect extend is not modeled (pad only).
            return new RadialColorGlyphPaint(Map(t, g.X1, g.Y1), Map(t, g.X0, g.Y0), g.R1 * radiusScale, colors, positions);
        }

        private SweepColorGlyphPaint? BuildSweep(PaintSweepGradient g, Affine2x3 t)
        {
            if (!TryBuildStops(g.Line, out PaintColor[] colors, out double[] _))
                return null;

            double radiusScale = Math.Sqrt(Math.Abs(t.XX * t.YY - t.XY * t.YX));
            double unitsPerEm = _typeface.Metrics.UnitsPerEm;
            // Give the fan a radius large enough to cover the glyph.
            double radius = Math.Max(_fontSize, radiusScale * unitsPerEm);

            // Each stop's parametric offset becomes a sweep angle, converted from the COLR convention (counter-clockwise
            // from +x) to clockwise from up, accounting for the target's y direction.
            var stops = SortedStops(g.Line);
            var angles = new double[stops.Count];
            for (int i = 0; i < stops.Count; i++)
                angles[i] = ToConicAngle(g.StartAngle + stops[i].Offset * (g.EndAngle - g.StartAngle));

            return new SweepColorGlyphPaint(Map(t, g.CenterX, g.CenterY), radius, colors, angles);
        }

        private double ToConicAngle(double colrRadians)
        {
            // COLR: ccw from +x. Conic: cw from up. With y down the visual sense of "ccw" flips, so cw_from_up = 90deg - colrAngle.
            double rad = (90.0 - colrRadians * 180.0 / Math.PI) * Math.PI / 180.0;
            return _yDown ? rad : -rad;
        }

        private bool TryBuildStops(ColorLine line, out PaintColor[] colors, out double[] positions)
        {
            List<ColorStop> stops = SortedStops(line);
            if (stops.Count == 0)
            {
                colors = [];
                positions = [];
                return false;
            }

            if (stops.Count == 1)
                stops.Add(stops[0] with { Offset = stops[0].Offset + 1e-4 });

            colors = new PaintColor[stops.Count];
            positions = new double[stops.Count];
            for (int i = 0; i < stops.Count; i++)
            {
                colors[i] = ResolveColor(stops[i].PaletteIndex, stops[i].Alpha);
                positions[i] = stops[i].Offset;
            }

            return true;
        }

        /// <summary>
        /// Tiles (repeat) or mirrors (reflect) the gradient stops over a few periods either side of the base [0,1] range and
        /// extends the gradient axis to cover them, since an axial gradient can only pad.
        /// </summary>
        private static void ExpandLinearExtend(ref PaintPoint p0, ref PaintPoint p1, ref PaintColor[] colors, ref double[] positions, ColorExtend extend)
        {
            const int periods = 2; // each side
            int total = 2 * periods + 1;

            var samples = new List<(double T, PaintColor Color)>();
            for (int p = -periods; p <= periods; p++)
            {
                bool mirror = extend == ColorExtend.Reflect && ((p % 2 + 2) % 2 == 1);
                for (int j = 0; j < positions.Length; j++)
                {
                    double local = mirror ? 1.0 - positions[j] : positions[j];
                    samples.Add((p + local, colors[j]));
                }
            }

            samples.Sort((a, b) => a.T.CompareTo(b.T));

            // Extend the axis so the new [0,1] parameter spans original t in [-periods, periods+1].
            double dx = p1.X - p0.X;
            double dy = p1.Y - p0.Y;
            var newP0 = new PaintPoint(p0.X - periods * dx, p0.Y - periods * dy);
            p1 = new PaintPoint(p1.X + periods * dx, p1.Y + periods * dy);
            p0 = newP0;

            var newColors = new PaintColor[samples.Count];
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

        private static List<ColorStop> SortedStops(ColorLine line)
        {
            var stops = new List<ColorStop>(line.Stops);
            stops.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            return stops;
        }

        private PaintColor ResolveColor(int paletteIndex, double alpha)
        {
            PaintColor color;
            if (paletteIndex == UseForegroundColor)
            {
                // The COLR "use text color" sentinel is a paint reference, not a real CPAL entry index, so it
                // resolves to the text color regardless of any font-palette override-colors.
                color = _foreground;
            }
            else if (_overrides is not null && _overrides.TryGetValue(paletteIndex, out var over))
            {
                color = over;
            }
            else if (_typeface.ColorPalette is { } palette && palette.TryGetColor(_paletteIndex, paletteIndex, out var c))
            {
                color = PaintColor.FromArgb(c.A, c.R, c.G, c.B);
            }
            else
            {
                color = _foreground;
            }

            if (alpha < 1.0)
            {
                int scaledAlpha = (int)Math.Round(color.A * alpha, MidpointRounding.AwayFromZero);
                color = PaintColor.FromArgb(Math.Clamp(scaledAlpha, 0, 255), color.R, color.G, color.B);
            }

            return color;
        }

        // ---- Geometry helpers ------------------------------------------------------------------

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
