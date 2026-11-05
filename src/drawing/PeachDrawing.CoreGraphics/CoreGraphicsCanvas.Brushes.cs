using CoreGraphics;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace PeachDrawing.CoreGraphics;

public sealed partial class CoreGraphicsCanvas
{
    private const int MaxRepeats = 256;

    internal static CGColorSpace Rgb { get; } = CGColorSpace.CreateSrgb()!;

    internal static CGColor ToColor(PaintColor color, double alphaScale = 1)
    {
        var (r, g, b) = color.ToRgb();
        return new CGColor(Rgb, [(nfloat)(r / 255.0), (nfloat)(g / 255.0), (nfloat)(b / 255.0), (nfloat)(color.A / 255.0 * alphaScale)]);
    }

    // ---- filling and stroking -----------------------------------------------------------------------------

    private void Fill(Brush brush, Action addShape, bool evenOdd)
    {
        using var hatch = HatchTile.Of(this, ref brush, out var skip);
        if (skip)
            return;

        if (brush is SolidBrush solid)
        {
            if (solid.PaintColor.A == 0)
                return;

            using var color = ToColor(solid.PaintColor);
            _cg.SetFillColor(color);
            _cg.BeginPath();
            addShape();
            _cg.DrawPath(evenOdd ? CGPathDrawingMode.EOFill : CGPathDrawingMode.Fill);
            return;
        }

        var depth = _depth;
        Save();
        try
        {
            _cg.BeginPath();
            addShape();
            if (evenOdd)
                _cg.EOClip();
            else
                _cg.Clip();

            PaintShader(brush);
        }
        finally
        {
            RestoreTo(depth);
        }
    }

    private void Stroke(Pen pen, Action addShape)
    {
        if (pen.Paint is not { } source)
            return;

        using var hatch = HatchTile.Of(this, ref source, out var skip);
        if (skip)
            return;

        if (source is SolidBrush solid)
        {
            if (solid.PaintColor.A == 0)
                return;

            var depth = _depth;
            Save();
            try
            {
                using var color = ToColor(solid.PaintColor);
                ConfigureStroke(pen);
                _cg.SetStrokeColor(color);
                _cg.BeginPath();
                addShape();
                _cg.StrokePath();
            }
            finally
            {
                RestoreTo(depth);
            }

            return;
        }

        // A gradient or pattern stroke is the fill of the outline the stroke covers.
        var saved = _depth;
        Save();
        try
        {
            ConfigureStroke(pen);
            _cg.BeginPath();
            addShape();
            _cg.ReplacePathWithStrokedPath();
            _cg.Clip();
            PaintShader(source);
        }
        finally
        {
            RestoreTo(saved);
        }
    }

    private void ConfigureStroke(Pen pen)
    {
        // A pen's width and dash lengths are in points, like a path's coordinates. A zero width is a hairline: one device pixel.
        _cg.SetLineWidth(pen.Width > 0 ? (nfloat)(pen.Width * _ppp) : (nfloat)(1.0 / DeviceScale));
        _cg.SetLineCap(pen.LineCap switch { LineCap.Round => CGLineCap.Round, LineCap.Square => CGLineCap.Square, _ => CGLineCap.Butt });
        _cg.SetLineJoin(pen.LineJoin switch { LineJoin.Round => CGLineJoin.Round, LineJoin.Bevel => CGLineJoin.Bevel, _ => CGLineJoin.Miter });

        // An unset limit (0) is 10 on the PDF and raster canvases.
        _cg.SetMiterLimit((nfloat)(pen.MiterLimit >= 1 ? pen.MiterLimit : 10));

        if (ResolveDashes(pen) is { } dashes)
        {
            var pattern = new nfloat[dashes.Length];
            for (var i = 0; i < pattern.Length; i++)
                pattern[i] = (nfloat)(dashes[i] * _ppp);

            _cg.SetLineDash(pen.DashStyle == DashStyle.Custom ? (nfloat)(pen.DashOffset * _ppp) : 0, pattern);
        }
    }

    internal static double[]? ResolveDashes(Pen pen)
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

    /// <summary>The tile a hatch brush makes for one draw; released with the draw.</summary>
    private readonly struct HatchTile : IDisposable
    {
        private readonly PeachDrawing.Core.Image? _image;

        private HatchTile(PeachDrawing.Core.Image image) => _image = image;

        public static HatchTile Of(CoreGraphicsCanvas canvas, ref Brush brush, out bool skip)
        {
            skip = false;
            if (brush is not HatchBrush hatch)
                return default;

            if (hatch.ToTileBrush(canvas) is not { } tileBrush)
            {
                skip = true;
                return default;
            }

            brush = tileBrush;
            return new HatchTile(tileBrush.Tile);
        }

        public void Dispose() => _image?.Dispose();
    }

    // ---- shaders: painting a non-solid brush over the current clip ---------------------------------------

    private void PaintShader(Brush brush)
    {
        switch (brush)
        {
            case LinearGradientBrush { Stops.Count: > 0 } linear:
                PaintLinear(linear);
                break;

            case RadialGradientBrush { Stops.Count: > 0 } radial:
                PaintRadial(radial);
                break;

            case ConicGradientBrush { Stops.Count: > 0 } conic:
                PaintConic(conic);
                break;

            case TileBrush tile:
                PaintTile(tile);
                break;

            case LinearGradientBrush or RadialGradientBrush or ConicGradientBrush:
                break;

            default:
                throw new NotSupportedException($"Unknown brush type {brush.GetType()}");
        }
    }

    /// <summary>Stop positions clamped to [0, 1] that never go backwards - a stop that would is moved up to the one before it, as CSS does.</summary>
    private static (double Position, PaintColor Color)[] NormalizeStops(IReadOnlyList<GradientStop> stops)
    {
        var result = new (double, PaintColor)[stops.Count];
        var previous = 0.0;
        for (var i = 0; i < result.Length; i++)
        {
            var position = Math.Max(Math.Clamp(stops[i].Position, 0, 1), previous);
            previous = position;
            result[i] = (position, stops[i].PaintColor);
        }

        return result;
    }

    /// <summary>Builds a CoreGraphics gradient from <paramref name="stops"/> repeated <paramref name="repeats"/> times across [0, 1].</summary>
    private static CGGradient MakeGradient((double Position, PaintColor Color)[] stops, int repeats)
    {
        var count = stops.Length * repeats;
        var components = new nfloat[count * 4];
        var locations = new nfloat[count];
        var i = 0;
        for (var k = 0; k < repeats; k++)
        {
            foreach (var (position, color) in stops)
            {
                var (r, g, b) = color.ToRgb();
                components[i * 4] = (nfloat)(r / 255.0);
                components[i * 4 + 1] = (nfloat)(g / 255.0);
                components[i * 4 + 2] = (nfloat)(b / 255.0);
                components[i * 4 + 3] = (nfloat)(color.A / 255.0);
                locations[i] = (nfloat)((k + position) / repeats);
                i++;
            }
        }

        return new CGGradient(Rgb, components, locations);
    }

    private void PaintLinear(LinearGradientBrush linear)
    {
        var stops = NormalizeStops(linear.Stops);
        var start = new CGPoint(linear.Start.X, linear.Start.Y);
        var end = new CGPoint(linear.End.X, linear.End.Y);
        var options = CGGradientDrawingOptions.DrawsBeforeStartLocation | CGGradientDrawingOptions.DrawsAfterEndLocation;
        var repeats = 1;

        if (linear.Spread == GradientSpread.Repeat)
        {
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var length2 = dx * dx + dy * dy;
            if (length2 > 1e-12)
            {
                // The whole clip has to be covered by whole periods of the axis, so the gradient is drawn across as many as reach the clip's corners.
                var box = _cg.GetClipBoundingBox();
                double lo = double.MaxValue, hi = double.MinValue;
                foreach (var (cx, cy) in Corners(box))
                {
                    var t = ((cx - start.X) * dx + (cy - start.Y) * dy) / length2;
                    lo = Math.Min(lo, t);
                    hi = Math.Max(hi, t);
                }

                var n0 = (int)Math.Floor(lo);
                var n1 = Math.Max((int)Math.Ceiling(hi), n0 + 1);
                if (n1 - n0 > MaxRepeats)
                    n1 = n0 + MaxRepeats;

                repeats = n1 - n0;
                (start, end) = (new CGPoint(start.X + n0 * dx, start.Y + n0 * dy), new CGPoint(start.X + n1 * dx, start.Y + n1 * dy));
                options = 0;
            }
        }

        using var gradient = MakeGradient(stops, repeats);
        _cg.DrawLinearGradient(gradient, start, end, options);
    }

    private static IEnumerable<(double X, double Y)> Corners(CGRect box)
    {
        yield return (box.Left, box.Top);
        yield return (box.Right, box.Top);
        yield return (box.Left, box.Bottom);
        yield return (box.Right, box.Bottom);
    }

    private void PaintRadial(RadialGradientBrush radial)
    {
        var stops = NormalizeStops(radial.Stops);
        var rx = Math.Max(radial.RadiusX, 1e-6);
        var ry = Math.Max(radial.RadiusY, 1e-6);
        var center = new CGPoint(radial.Center.X, radial.Center.Y);
        var focus = new CGPoint(radial.Focus.X, radial.Focus.Y);

        // The gradient is drawn as circles of the horizontal radius; an ellipse is that circle squeezed about its centre, and the brush's own
        // transform then carries the ellipse where it belongs. A concatenated matrix is applied to points before the ones concatenated earlier, so
        // the squeeze (applied to the gradient first) is concatenated after the brush transform.
        var depth = _depth;
        Save();
        try
        {
            if (radial.Transform is { } brushTransform)
                _cg.ConcatCTM(ToTransform(brushTransform));

            _cg.ConcatCTM(ToTransform(Matrix3x2.CreateTranslation((float)-center.X, (float)-center.Y) * Matrix3x2.CreateScale(1f, (float)(ry / rx))
                                      * Matrix3x2.CreateTranslation((float)center.X, (float)center.Y)));

            var options = CGGradientDrawingOptions.DrawsBeforeStartLocation | CGGradientDrawingOptions.DrawsAfterEndLocation;
            var repeats = 1;
            var endRadius = rx;
            var hasFocus = radial.FocusRadius > 0 || focus.X != center.X || focus.Y != center.Y;
            if (radial.Spread == GradientSpread.Repeat && !hasFocus)
            {
                var far = 0.0;
                foreach (var (cx, cy) in Corners(_cg.GetClipBoundingBox()))
                    far = Math.Max(far, Math.Sqrt((cx - center.X) * (cx - center.X) + (cy - center.Y) * (cy - center.Y)));

                repeats = Math.Clamp((int)Math.Ceiling(far / rx), 1, MaxRepeats);
                endRadius = rx * repeats;
                options = 0;
            }

            using var gradient = MakeGradient(stops, repeats);

            // The frame is squeezed vertically about the centre, so the focus (which is given in the unsqueezed frame) is stretched back out by the same factor.
            var drawnFocus = new CGPoint(focus.X, center.Y + (focus.Y - center.Y) * rx / ry);
            _cg.DrawRadialGradient(gradient, drawnFocus, (nfloat)Math.Max(radial.FocusRadius, 0), center, (nfloat)endRadius, options);
        }
        finally
        {
            RestoreTo(depth);
        }
    }

    private void PaintConic(ConicGradientBrush conic)
    {
        // CoreGraphics has no angular gradient, so the fill is computed per pixel on the CPU at the device's resolution over the clip's bounds and drawn
        // as an image; the clip the caller set shapes it.
        var box = _cg.GetClipBoundingBox();
        if (box.IsNull() || box.IsInfinite() || box.Width <= 0 || box.Height <= 0)
            return;

        var scale = DeviceScale;
        var width = (int)Math.Ceiling(box.Width * scale);
        var height = (int)Math.Ceiling(box.Height * scale);
        var capped = Math.Sqrt((double)_context.MaxRasterPixels / Math.Max(1.0, (double)width * height));
        if (capped < 1)
        {
            width = Math.Max(1, (int)(width * capped));
            height = Math.Max(1, (int)(height * capped));
        }

        var pixels = ConicPixels.Render(conic, box.X, box.Y, box.Width, box.Height, width, height);
        using var image = CoreGraphicsImage.FromPixels(new PixelBuffer(width, height, pixels), _context.Counts);
        DrawCgImage(image.GetCgImage(), new Rect(box.X, box.Y, box.Width, box.Height), ImageSampling.Bilinear);
    }

    private void PaintTile(TileBrush tile)
    {
        var cell = new CGRect(0, 0, Math.Max(tile.CellWidth, 1e-9), Math.Max(tile.CellHeight, 1e-9));
        var cg = CgImageOf(tile.Tile);

        var depth = _depth;
        Save();
        try
        {
            _cg.ConcatCTM(ToTransform(tile.Transform));

            // The canvas is y-down and CoreGraphics draws an image upright in y-up space, so the tiles are drawn in a flipped frame; the pitch is the
            // same either way, which is why the cell can sit at the origin of the flipped frame without moving the lattice.
            _cg.ConcatCTM(new CGAffineTransform(1, 0, 0, -1, 0, 0));
            _cg.InterpolationQuality = QualityFor(tile.Tile, tile.Sampling);
            _cg.DrawTiledImage(cell, cg);
        }
        finally
        {
            RestoreTo(depth);
        }
    }
}
