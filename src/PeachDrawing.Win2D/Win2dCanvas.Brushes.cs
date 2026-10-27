using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace PeachDrawing.Win2D;

public sealed partial class Win2dCanvas
{
    /// <summary>A Win2D brush and anything that had to be made alongside it, released together once the draw that needed it is done.</summary>
    private sealed class BrushLease : IDisposable
    {
        private readonly List<IDisposable> _owned = [];

        public ICanvasBrush? Brush { get; set; }

        public void Own(IDisposable resource) => _owned.Add(resource);

        public void Dispose()
        {
            // Reverse order: the brush before what it was built from.
            for (var i = _owned.Count - 1; i >= 0; i--)
                _owned[i].Dispose();

            _owned.Clear();
        }
    }

    private static Windows.UI.Color ToColor(PaintColor color)
    {
        var (r, g, b) = color.ToRgb();
        return Windows.UI.Color.FromArgb(color.A, r, g, b);
    }

    private BrushLease MakeBrush(Brush brush)
    {
        var lease = new BrushLease();
        try
        {
            switch (brush)
            {
                case SolidBrush solid:
                    if (solid.PaintColor.A == 0)
                        break;

                    lease.Brush = Owned(lease, new CanvasSolidColorBrush(_session, ToColor(solid.PaintColor)));
                    break;

                case LinearGradientBrush linear:
                    if (linear.Stops.Count == 0)
                        break;

                    lease.Brush = Owned(lease, new CanvasLinearGradientBrush(_session, ToStops(linear.Stops), Edge(linear.Spread), CanvasAlphaMode.Straight)
                    {
                        StartPoint = new Vector2((float)linear.Start.X, (float)linear.Start.Y),
                        EndPoint = new Vector2((float)linear.End.X, (float)linear.End.Y),
                    });
                    break;

                case RadialGradientBrush radial:
                    if (radial.Stops.Count == 0)
                        break;

                    // The focus is stated as an offset from the centre; an ellipse is the radii being unequal. FocusRadius (a two-circle
                    // gradient) has no Win2D form, so the gradient starts from a point focus.
                    lease.Brush = Owned(lease, new CanvasRadialGradientBrush(_session, ToStops(radial.Stops), Edge(radial.Spread), CanvasAlphaMode.Straight)
                    {
                        Center = new Vector2((float)radial.Center.X, (float)radial.Center.Y),
                        OriginOffset = new Vector2((float)(radial.Focus.X - radial.Center.X), (float)(radial.Focus.Y - radial.Center.Y)),
                        RadiusX = (float)Math.Max(radial.RadiusX, 1e-6),
                        RadiusY = (float)Math.Max(radial.RadiusY, 1e-6),
                        Transform = radial.Transform ?? Matrix3x2.Identity,
                    });
                    break;

                case TileBrush tile:
                    lease.Brush = TileImageBrush(lease, tile);
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

    private static T Owned<T>(BrushLease lease, T resource) where T : class, IDisposable
    {
        lease.Own(resource);
        return resource;
    }

    private static CanvasEdgeBehavior Edge(GradientSpread spread) => spread == GradientSpread.Repeat ? CanvasEdgeBehavior.Wrap : CanvasEdgeBehavior.Clamp;

    private static CanvasGradientStop[] ToStops(IReadOnlyList<GradientStop> stops)
    {
        var result = new CanvasGradientStop[stops.Count];
        var previous = 0f;
        for (var i = 0; i < result.Length; i++)
        {
            // Win2D wants positions in [0, 1] that never go backwards; a stop that would is moved up to the one before it, as CSS does.
            var position = Math.Max((float)Math.Clamp(stops[i].Position, 0, 1), previous);
            previous = position;
            result[i] = new CanvasGradientStop { Position = position, Color = ToColor(stops[i].PaintColor) };
        }

        return result;
    }

    private ICanvasBrush? TileImageBrush(BrushLease lease, TileBrush tile)
    {
        // The tile is drawn at its own size, then scaled to the cell and repeated by the brush's own transform.
        var width = Math.Max(tile.Tile.Width, 1e-9);
        var height = Math.Max(tile.Tile.Height, 1e-9);
        var scale = Matrix3x2.CreateScale((float)(tile.CellWidth / width), (float)(tile.CellHeight / height));
        var interpolation = Interpolation(tile.Tile, tile.Sampling);

        ICanvasImage source;
        switch (tile.Tile)
        {
            case Win2dTileImage vector:
                source = vector.Content;
                break;
            default:
                source = ImageFor(tile.Tile).GetBitmap(_session);
                break;
        }

        return Owned(lease, new CanvasImageBrush(_session, source)
        {
            ExtendX = CanvasEdgeBehavior.Wrap,
            ExtendY = CanvasEdgeBehavior.Wrap,
            SourceRectangle = new Windows.Foundation.Rect(0, 0, width, height),
            Interpolation = interpolation,
            Transform = scale.Then(tile.Transform),
        });
    }

    // ---- conic gradients: no Win2D brush, so wedges ---------------------------------------------------------

    private const int ConicWedges = 360;

    private void FillConic(ConicGradientBrush conic, CanvasGeometry region)
    {
        if (conic.Stops.Count == 0)
            return;

        // The wedges are painted inside the shape's outline.
        using var clip = _session.CreateLayer(1f, region);

        // The wedges meet at the centre and along each shared edge; anti-aliased, every one of those seams would show as a faint line, so the
        // wedges are drawn aliased (the clip above still gives the shape's own edge its smoothing).
        var smoothing = _session.Antialiasing;
        _session.Antialiasing = CanvasAntialiasing.Aliased;
        try
        {
            FillWedges(conic);
        }
        finally
        {
            _session.Antialiasing = smoothing;
        }
    }

    private void FillWedges(ConicGradientBrush conic)
    {
        var start = conic.Stops[0].Position;
        var step = 2 * Math.PI / ConicWedges;
        var radius = conic.OuterRadius / Math.Cos(step);
        var vertices = new Vector2[3];
        for (var i = 0; i < ConicWedges; i++)
        {
            var a0 = start + i * step;
            var a1 = a0 + step;
            var color = ConicColorAt(conic, (a0 + a1) / 2);
            if (color.A == 0)
                continue;

            var p0 = Polar(conic.Center, radius, a0 - step * 0.05);
            var p1 = Polar(conic.Center, radius, a1 + step * 0.05);
            vertices[0] = new Vector2((float)conic.Center.X, (float)conic.Center.Y);
            vertices[1] = new Vector2((float)p0.X, (float)p0.Y);
            vertices[2] = new Vector2((float)p1.X, (float)p1.Y);
            using var wedge = CanvasGeometry.CreatePolygon(_session, vertices);
            _session.FillGeometry(wedge, ToColor(color));
        }
    }

    private static PaintPoint Polar(PaintPoint center, double radius, double angle) =>
        new(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));

    private static PaintColor ConicColorAt(ConicGradientBrush conic, double angle)
    {
        var stops = conic.Stops;
        if (stops.Count == 0)
            return PaintColor.Transparent;

        var turn = 2 * Math.PI;
        var start = stops[0].Position;
        angle = start + (angle - start - turn * Math.Floor((angle - start) / turn));

        if (angle <= stops[0].Position)
            return stops[0].PaintColor;

        for (var i = 1; i < stops.Count; i++)
        {
            if (angle > stops[i].Position)
                continue;

            var a = stops[i - 1];
            var b = stops[i];
            var span = b.Position - a.Position;
            var t = span <= 0 ? 1 : (angle - a.Position) / span;
            var (ar, ag, ab) = a.PaintColor.ToRgb();
            var (br, bg, bb) = b.PaintColor.ToRgb();
            return PaintColor.FromArgb(Lerp(a.PaintColor.A, b.PaintColor.A, t), Lerp(ar, br, t), Lerp(ag, bg, t), Lerp(ab, bb, t));
        }

        return stops[^1].PaintColor;
    }

    private static int Lerp(int a, int b, double t) => (int)Math.Round(a + (b - a) * t);
}
