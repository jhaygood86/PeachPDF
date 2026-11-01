using PeachDrawing.Core;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace PeachDrawing.Skia;

public sealed partial class SkiaCanvas
{
    /// <summary>A Skia paint and anything that had to be made alongside it, released together once the draw that needed it is done.</summary>
    private sealed class PaintLease : IDisposable
    {
        private readonly List<IDisposable> _owned = [];

        public SKPaint? Paint { get; set; }

        public void Own(IDisposable resource) => _owned.Add(resource);

        public void Dispose()
        {
            // Reverse order: the paint before what it was built from.
            Paint?.Dispose();
            Paint = null;
            for (var i = _owned.Count - 1; i >= 0; i--)
                _owned[i].Dispose();

            _owned.Clear();
        }
    }

    private static SKColor ToColor(PaintColor color)
    {
        var (r, g, b) = color.ToRgb();
        return new SKColor(r, g, b, color.A);
    }

    /// <summary>A paint carrying the canvas's anti-aliasing and pushed blend mode, for a fill.</summary>
    private SKPaint NewPaint() => new()
    {
        IsAntialias = _smooth,
        BlendMode = BlendModeFor(_blend),
        Style = SKPaintStyle.Fill,
    };

    /// <summary>A solid paint of <paramref name="color"/>, or <see langword="null"/> when it is fully transparent. The caller disposes it.</summary>
    private SKPaint? SolidPaint(PaintColor color)
    {
        if (color.A == 0)
            return null;

        var paint = NewPaint();
        paint.Color = ToColor(color);
        return paint;
    }

    private PaintLease MakePaint(Brush brush)
    {
        var lease = new PaintLease();
        try
        {
            var paint = NewPaint();
            lease.Paint = paint;

            switch (brush)
            {
                case SolidBrush solid:
                    if (solid.PaintColor.A == 0)
                    {
                        lease.Dispose();
                        break;
                    }

                    paint.Color = ToColor(solid.PaintColor);
                    break;

                case LinearGradientBrush linear:
                    if (linear.Stops.Count == 0)
                    {
                        lease.Dispose();
                        break;
                    }

                    ToStops(linear.Stops, out var linearColors, out var linearPositions);
                    paint.Shader = Owned(lease, SKShader.CreateLinearGradient(
                        new SKPoint((float)linear.Start.X, (float)linear.Start.Y), new SKPoint((float)linear.End.X, (float)linear.End.Y),
                        linearColors, linearPositions, linear.Spread == GradientSpread.Repeat ? SKShaderTileMode.Repeat : SKShaderTileMode.Clamp));
                    break;

                case RadialGradientBrush radial:
                    if (radial.Stops.Count == 0)
                    {
                        lease.Dispose();
                        break;
                    }

                    paint.Shader = Owned(lease, RadialShader(radial));
                    break;

                case ConicGradientBrush conic:
                    if (conic.Stops.Count == 0)
                    {
                        lease.Dispose();
                        break;
                    }

                    paint.Shader = Owned(lease, ConicShader(conic));
                    break;

                case TileBrush tile:
                    paint.Shader = Owned(lease, TileShader(tile));
                    break;

                default:
                    throw new NotSupportedException($"Unknown brush type {brush.GetType()}");
            }

            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    private static T Owned<T>(PaintLease lease, T resource) where T : class, IDisposable
    {
        lease.Own(resource);
        return resource;
    }

    private static void ToStops(IReadOnlyList<GradientStop> stops, out SKColor[] colors, out float[] positions)
    {
        colors = new SKColor[stops.Count];
        positions = new float[stops.Count];
        var previous = 0f;
        for (var i = 0; i < colors.Length; i++)
        {
            // Skia wants positions in [0, 1] that never go backwards; a stop that would is moved up to the one before it, as CSS does.
            var position = Math.Max((float)Math.Clamp(stops[i].Position, 0, 1), previous);
            previous = position;
            colors[i] = ToColor(stops[i].PaintColor);
            positions[i] = position;
        }
    }

    private static SKShader RadialShader(RadialGradientBrush radial)
    {
        ToStops(radial.Stops, out var colors, out var positions);
        var rx = (float)Math.Max(radial.RadiusX, 1e-6);
        var ry = (float)Math.Max(radial.RadiusY, 1e-6);
        var center = new SKPoint((float)radial.Center.X, (float)radial.Center.Y);
        var mode = radial.Spread == GradientSpread.Repeat ? SKShaderTileMode.Repeat : SKShaderTileMode.Clamp;

        // The shader is a circle of the horizontal radius; an ellipse is that circle squeezed about its centre, and the brush's own transform
        // then carries the ellipse where it belongs.
        var local = Matrix3x2.CreateTranslation(-center.X, -center.Y) * Matrix3x2.CreateScale(1f, ry / rx) * Matrix3x2.CreateTranslation(center.X, center.Y);
        if (radial.Transform is { } transform)
            local *= transform;

        var matrix = ToMatrix(local);
        var focus = new SKPoint((float)radial.Focus.X, (float)radial.Focus.Y);
        if (radial.FocusRadius > 0 || focus != center)
        {
            return SKShader.CreateTwoPointConicalGradient(focus, (float)Math.Max(radial.FocusRadius, 0), center, rx, colors, positions, mode, matrix);
        }

        return SKShader.CreateRadialGradient(center, rx, colors, positions, mode, matrix);
    }

    private static SKShader ConicShader(ConicGradientBrush conic)
    {
        // The stops' positions are angles in radians clockwise from straight up. A sweep gradient starts at the +x axis and runs from 0 to 1
        // around the circle, so the first stop's angle is the rotation of the whole sweep and the others are fractions of a turn from it.
        var turn = 2 * Math.PI;
        var start = conic.Stops[0].Position;
        var colors = new SKColor[conic.Stops.Count];
        var positions = new float[conic.Stops.Count];
        var previous = 0f;
        for (var i = 0; i < colors.Length; i++)
        {
            var position = Math.Max((float)Math.Clamp((conic.Stops[i].Position - start) / turn, 0, 1), previous);
            previous = position;
            colors[i] = ToColor(conic.Stops[i].PaintColor);
            positions[i] = position;
        }

        var center = new SKPoint((float)conic.Center.X, (float)conic.Center.Y);
        var rotate = SKMatrix.CreateRotationDegrees((float)((start - Math.PI / 2) * 180 / Math.PI), center.X, center.Y);
        return SKShader.CreateSweepGradient(center, colors, positions, SKShaderTileMode.Clamp, 0f, 360f, rotate);
    }

    private SKShader TileShader(TileBrush tile)
    {
        // The tile is drawn at its own size, then scaled to the cell and repeated by the brush's own transform.
        var width = Math.Max(tile.Tile.Width, 1e-9);
        var height = Math.Max(tile.Tile.Height, 1e-9);
        var scale = Matrix3x2.CreateScale((float)(tile.CellWidth / width), (float)(tile.CellHeight / height));
        var matrix = ToMatrix(scale.Then(tile.Transform));
        var sampling = Sampling(tile.Tile, tile.Sampling);

        SKShader? shader;
        if (tile.Tile is SkiaTileImage vector)
        {
            shader = vector.Content.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat,
                sampling.Filter == SKFilterMode.Nearest ? SKFilterMode.Nearest : SKFilterMode.Linear, matrix, SKRect.Create(0, 0, (float)width, (float)height));
        }
        else
        {
            shader = ImageFor(tile.Tile).GetImage(_context.GpuContext).ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, sampling, matrix);
        }

        return shader ?? throw new InvalidOperationException("Skia could not make a shader of the tile.");
    }

    // ---- filling and stroking -----------------------------------------------------------------------------

    private void Fill(Brush brush, Action<SKPaint> draw)
    {
        SkiaTileOwner? hatchTile = null;
        try
        {
            if (brush is HatchBrush hatch)
            {
                if (hatch.ToTileBrush(this) is not { } tileBrush)
                    return;

                // The tile a hatch makes is a fresh picture nobody else holds; it is released with the draw.
                hatchTile = new SkiaTileOwner(tileBrush.Tile);
                brush = tileBrush;
            }

            using var lease = MakePaint(brush);
            if (lease.Paint is { } paint)
                draw(paint);
        }
        finally
        {
            hatchTile?.Dispose();
        }
    }

    private void Stroke(Pen pen, Action<SKPaint> draw)
    {
        if (pen.Paint is not { } source)
            return;

        SkiaTileOwner? hatchTile = null;
        try
        {
            if (source is HatchBrush hatch)
            {
                if (hatch.ToTileBrush(this) is not { } tileBrush)
                    return;

                hatchTile = new SkiaTileOwner(tileBrush.Tile);
                source = tileBrush;
            }

            using var lease = MakePaint(source);
            if (lease.Paint is not { } paint)
                return;

            ConfigureStroke(lease, paint, pen);
            draw(paint);
        }
        finally
        {
            hatchTile?.Dispose();
        }
    }

    private void ConfigureStroke(PaintLease lease, SKPaint paint, Pen pen)
    {
        paint.Style = SKPaintStyle.Stroke;

        // A pen's width and dash lengths are in points, like a path's coordinates. A zero width is Skia's hairline: one device pixel.
        paint.StrokeWidth = pen.Width > 0 ? (float)(pen.Width * _ppp) : 0f;
        paint.StrokeCap = pen.LineCap switch { LineCap.Round => SKStrokeCap.Round, LineCap.Square => SKStrokeCap.Square, _ => SKStrokeCap.Butt };
        paint.StrokeJoin = pen.LineJoin switch { LineJoin.Round => SKStrokeJoin.Round, LineJoin.Bevel => SKStrokeJoin.Bevel, _ => SKStrokeJoin.Miter };

        // An unset limit (0) is 10 on the PDF and raster canvases.
        paint.StrokeMiter = (float)(pen.MiterLimit >= 1 ? pen.MiterLimit : 10);

        if (ResolveDashes(pen) is { } dashes)
        {
            var pattern = new float[dashes.Length];
            for (var i = 0; i < pattern.Length; i++)
                pattern[i] = (float)(dashes[i] * _ppp);

            var phase = pen.DashStyle == DashStyle.Custom ? (float)(pen.DashOffset * _ppp) : 0f;
            paint.PathEffect = Owned(lease, SKPathEffect.CreateDash(pattern, phase));
        }
    }

    private static double[]? ResolveDashes(Pen pen)
    {
        var w = pen.Width;
        if (!(w > 0))
            return null;

        switch (pen.DashStyle)
        {
            case DashStyle.Dash:
                // A thin dashed line reads better with longer dashes than the standard 3:1 ratio.
                return w < 2 ? [4 * w, 4 * w] : [3 * w, w];
            case DashStyle.Dot: return [w, w];
            case DashStyle.DashDot: return [3 * w, w, w, w];
            case DashStyle.DashDotDot: return [3 * w, w, w, w, w, w];
            case DashStyle.Custom:
                if (pen.DashPattern is not { Length: > 0 } pattern)
                    return null;

                var list = new List<double>(pattern);

                // An odd count is padded the way GDI+ (and the PDF writer) does.
                if (list.Count % 2 == 1)
                    list.Add(0.2 * w);

                return [.. list];
            default:
                return null;
        }
    }
}

/// <summary>Disposes the tile image a hatch brush made for one draw.</summary>
internal readonly struct SkiaTileOwner(PeachDrawing.Core.Image image) : IDisposable
{
    public void Dispose() => image.Dispose();
}
