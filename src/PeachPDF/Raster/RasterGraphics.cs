using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Text;
using System;
using System.Collections.Generic;

namespace PeachPDF.Raster;

/// <summary>
/// An <see cref="RGraphics"/> that paints into a <see cref="RasterSurface"/> instead of a PDF content stream.
/// It exists for the effects PDF cannot express in vector form; the bitmap it produces is embedded back into
/// the PDF by the graphics that asked for it (<see cref="RGraphics.BeginRasterSurface"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Coordinates follow <c>GraphicsAdapter</c> exactly</b>, because the same paint code drives both: rects,
/// lines, polygons, images, strings and clips arrive in layout units and are divided by
/// <see cref="PixelsPerPoint"/> to reach user space (points); paths, pen widths, dash lengths and gradient
/// geometry are already in user space and used as they are. The current transform maps user space onward, and
/// <c>_sx</c>/<c>_sy</c> then map points to surface pixels.
/// </para>
/// <para>
/// Pens, brushes, fonts and paths are the PDF adapter's own objects: they are plain data and this backend runs
/// in the same assembly, so it reads them directly rather than duplicating the adapter layer. That keeps
/// text metrics identical to layout by construction.
/// </para>
/// </remarks>
internal sealed partial class RasterGraphics : RGraphics
{
    /// <summary>Upper bound on the pixels of a tile created through <see cref="CreateTile"/>.</summary>
    internal const int MaxTilePixels = 64_000_000;

    private readonly RasterSurface _surface;
    private readonly double _pixelsPerPoint;
    private readonly double _sx;
    private readonly double _sy;
    private readonly Stack<Affine> _transforms = [];
    private readonly Stack<RBlendMode> _blendModes = [];
    private readonly Stack<ClipState> _clips = [];
    private Affine _ctm = Affine.Identity;
    private readonly Stack<RMatrix> _layoutTransforms = [];
    private RMatrix _layoutCtm = RMatrix.Identity;
    private RBlendMode _blend = RBlendMode.Normal;
    private byte[] _coverageScratch;
    private byte[] _pixelScratch;

    public RasterGraphics(RAdapter adapter, RasterSurface surface, double pixelsPerPoint)
        : base(adapter, surface.LayoutRect)
    {
        _surface = surface;
        _pixelsPerPoint = pixelsPerPoint;

        // pixels per point = pixels per layout unit * layout units per point.
        _sx = surface.PixelsPerUnitX * pixelsPerPoint;
        _sy = surface.PixelsPerUnitY * pixelsPerPoint;

        _clips.Push(new ClipState(surface.Bounds, null));
        _coverageScratch = new byte[surface.Width + 4];
        _pixelScratch = new byte[(surface.Width + 4) * 4];
    }

    public RasterSurface Surface => _surface;

    internal override RMatrix CurrentTransform => _layoutCtm;

    /// <summary>Starts this graphics' transform at <paramref name="requester"/>'s: a raster region paints in its requester's current user space.</summary>
    internal void SeedTransform(RMatrix requester) => _layoutCtm = requester;

    public override double PixelsPerPoint => _pixelsPerPoint;

    public override bool IsOffscreenTile => true;

    internal override bool PrefersRasterGroups => true;

    /// <summary>User space (points) to surface pixels under the current transform.</summary>
    private Affine UserToDevice => Affine.Then(_ctm, new Affine(_sx, 0, 0, _sy, -_surface.GridX, -_surface.GridY));

    private double DeviceScale => Math.Max(UserToDevice.MaxScale, 1e-9);

    // ---- state ------------------------------------------------------------------------------------------

    public override void PopClip()
    {
        _clipStack.Pop();
        if (_clips.Count > 1)
            _clips.Pop();
    }

    public override void PushClip(RRect rect)
    {
        _clipStack.Push(rect);
        var user = new RRect(rect.X / _pixelsPerPoint, rect.Y / _pixelsPerPoint, rect.Width / _pixelsPerPoint, rect.Height / _pixelsPerPoint);
        var toDevice = UserToDevice;
        var current = _clips.Peek();

        if (toDevice.IsAxisAligned && toDevice.M11 > 0 && toDevice.M22 > 0)
        {
            var (l, t) = toDevice.Apply(user.Left, user.Top);
            var (r, b) = toDevice.Apply(user.Right, user.Bottom);
            if (IsWhole(l) && IsWhole(t) && IsWhole(r) && IsWhole(b))
            {
                _clips.Push(current.Intersect(new IntRect((int)Math.Round(l), (int)Math.Round(t), (int)Math.Round(r), (int)Math.Round(b))));
                return;
            }
        }

        var polygon = new PolygonSet();
        AddDeviceRect(polygon, user, toDevice);
        _clips.Push(current.Intersect(polygon, evenOdd: false));
    }

    private static bool IsWhole(double v) => Math.Abs(v - Math.Round(v)) < 1e-3;

    public override void PushClip(RGraphicsPath path)
    {
        _clipStack.Push(_clipStack.Peek());
        var toDevice = UserToDevice;
        var flat = FlatPath.From(((GraphicsPathAdapter)path).GraphicsPath, 0.1 / DeviceScale);
        var polygon = new PolygonSet();
        polygon.AddTransformed(flat.Contours, toDevice);
        var evenOdd = path.FillMode == RFillMode.EvenOdd;
        _clips.Push(_clips.Peek().Intersect(polygon, evenOdd));
    }

    public override void PushClipExclude(RRect rect)
    {
        // Unused by the paint pipeline; the PDF backend leaves it a no-op as well.
    }

    public override void PushTransform(RMatrix matrix)
    {
        _transforms.Push(_ctm);
        _layoutTransforms.Push(_layoutCtm);
        _layoutCtm = matrix.Then(_layoutCtm);

        // Only the translation is divided by PixelsPerPoint, exactly as GraphicsAdapter does: the linear
        // part is already scale-neutral (see RGraphics.PixelsPerPoint).
        var m = new Affine(matrix.M11, matrix.M12, matrix.M21, matrix.M22,
            matrix.OffsetX / _pixelsPerPoint, matrix.OffsetY / _pixelsPerPoint);
        _ctm = Affine.Then(m, _ctm);
    }

    public override void PopTransform()
    {
        if (_transforms.Count > 0)
            _ctm = _transforms.Pop();

        if (_layoutTransforms.Count > 0)
            _layoutCtm = _layoutTransforms.Pop();
    }

    public override void PushBlendMode(RBlendMode mode)
    {
        _blendModes.Push(_blend);
        _blend = mode;
    }

    public override void PopBlendMode()
    {
        if (_blendModes.Count > 0)
            _blend = _blendModes.Pop();
    }

    // Anti-aliasing is always on in this backend.
    public override object SetAntiAliasSmoothingMode() => true;

    public override void ReturnPreviousSmoothingMode(object? prevMode)
    {
    }

    public override RGraphicsPath GetGraphicsPath() => new GraphicsPathAdapter();

    // Tagged PDF structure does not exist in a bitmap.
    public override void BeginMarkedContent(string structureType, int mcid) { }

    public override void EndMarkedContent() { }

    public override void BeginArtifact() { }

    public override void BeginVariableText() { }

    public override void EndVariableText() { }

    public override void Dispose()
    {
    }

    // ---- tiles ------------------------------------------------------------------------------------------

    public override (RGraphics Graphics, RImage Image)? CreateTile(double width, double height)
    {
        if (!(width > 0) || !(height > 0) || double.IsInfinity(width) || double.IsInfinity(height))
            return null;

        var pw = Math.Max(1, (int)Math.Ceiling(width * _surface.PixelsPerUnitX - 1e-9));
        var ph = Math.Max(1, (int)Math.Ceiling(height * _surface.PixelsPerUnitY - 1e-9));
        if ((long)pw * ph > Math.Min(MaxTilePixels, _adapter.MaxRasterPixels))
            return null;

        // The tile's pixel pitch is stretched by less than one pixel across its whole size so it covers
        // exactly `width` x `height`, and drawing it back at that size needs no resampling.
        var tile = new RasterSurface(pw, ph, 0, 0, pw / width, ph / height);
        return (new RasterGraphics(_adapter, tile, _pixelsPerPoint), new RasterImage(tile, width, height));
    }

    // ---- nested raster regions --------------------------------------------------------------------------

    /// <summary>
    /// A raster region inside a raster region (a filtered element within a filtered ancestor). It keeps this surface's
    /// own pixel pitch and grid instead of going back to the document's DPI, so compositing it back is an exact pixel
    /// copy rather than a resample, and it is cut to this surface: nothing outside it was painted to begin with.
    /// </summary>
    internal override RasterSurfaceScope? BeginRasterSurface(RRect layoutBounds, double? dpiOverride = null)
    {
        if (!(layoutBounds.Width > 0) || !(layoutBounds.Height > 0) ||
            double.IsNaN(layoutBounds.X + layoutBounds.Y + layoutBounds.Width + layoutBounds.Height) ||
            double.IsInfinity(layoutBounds.X + layoutBounds.Y + layoutBounds.Width + layoutBounds.Height))
        {
            return null;
        }

        var ppuX = _surface.PixelsPerUnitX;
        var ppuY = _surface.PixelsPerUnitY;
        var left = Math.Max((long)Math.Floor(layoutBounds.Left * ppuX + 1e-9), _surface.GridX);
        var top = Math.Max((long)Math.Floor(layoutBounds.Top * ppuY + 1e-9), _surface.GridY);
        var right = Math.Min((long)Math.Ceiling(layoutBounds.Right * ppuX - 1e-9), _surface.GridX + _surface.Width);
        var bottom = Math.Min((long)Math.Ceiling(layoutBounds.Bottom * ppuY - 1e-9), _surface.GridY + _surface.Height);
        if (right <= left || bottom <= top)
            return null;

        var nested = new RasterSurface((int)(right - left), (int)(bottom - top), (int)left, (int)top, ppuX, ppuY);
        var graphics = new RasterGraphics(_adapter, nested, _pixelsPerPoint);
        graphics.SeedTransform(_layoutCtm);
        return new RasterSurfaceScope(graphics, nested);
    }

    internal override void DrawRaster(RasterSurface surface)
    {
        var bitmap = new Bitmap(surface.Width, surface.Height, surface.Buffer);
        var rect = surface.LayoutRect;
        // Same pitch, on the same grid: nearest-neighbour is an exact pixel copy, where a bilinear tap could pick up a
        // neighbour through floating-point noise in the inverse mapping.
        DrawBitmap(bitmap, rect.Width, rect.Height, rect, null, interpolate: false, opacity: 255, _blend);
    }

    // ---- fills and strokes ------------------------------------------------------------------------------

    public override void DrawRectangle(RBrush brush, double x, double y, double width, double height)
    {
        var paint = CreatePaint(((BrushAdapter)brush).Brush);
        if (paint is null)
            return;

        var polygon = new PolygonSet();
        AddDeviceRect(polygon, ToUser(new RRect(x, y, width, height)), UserToDevice);
        FillPolygons(polygon, evenOdd: false, paint);
    }

    public override void DrawRectangle(RPen pen, double x, double y, double width, double height)
    {
        var r = ToUser(new RRect(x, y, width, height));
        var flat = new FlatPath();
        flat.AddContour([(r.Left, r.Top), (r.Right, r.Top), (r.Right, r.Bottom), (r.Left, r.Bottom)], closed: true);
        StrokeFlat(flat, ((PenAdapter)pen).Pen);
    }

    public override void DrawLine(RPen pen, double x1, double y1, double x2, double y2)
    {
        var flat = new FlatPath();
        flat.AddContour([(x1 / _pixelsPerPoint, y1 / _pixelsPerPoint), (x2 / _pixelsPerPoint, y2 / _pixelsPerPoint)], closed: false);
        StrokeFlat(flat, ((PenAdapter)pen).Pen);
    }

    public override void DrawPolygon(RBrush brush, RPoint[] points)
    {
        if (points is not { Length: > 1 })
            return;

        var paint = CreatePaint(((BrushAdapter)brush).Brush);
        if (paint is null)
            return;

        var toDevice = UserToDevice;
        var polygon = new PolygonSet();
        polygon.BeginContour();
        foreach (var p in points)
        {
            var (dx, dy) = toDevice.Apply(p.X / _pixelsPerPoint, p.Y / _pixelsPerPoint);
            polygon.Add(dx, dy);
        }

        FillPolygons(polygon, evenOdd: false, paint);
    }

    public override void DrawPath(RBrush brush, RGraphicsPath path)
    {
        var paint = CreatePaint(((BrushAdapter)brush).Brush);
        if (paint is null)
            return;

        var flat = FlatPath.From(((GraphicsPathAdapter)path).GraphicsPath, 0.1 / DeviceScale);
        var polygon = new PolygonSet();
        polygon.AddTransformed(flat.Contours, UserToDevice);
        FillPolygons(polygon, path.FillMode == RFillMode.EvenOdd, paint);
    }

    public override void DrawPath(RPen pen, RGraphicsPath path)
    {
        var flat = FlatPath.From(((GraphicsPathAdapter)path).GraphicsPath, 0.1 / DeviceScale);
        StrokeFlat(flat, ((PenAdapter)pen).Pen);
    }

    private RRect ToUser(RRect layout) =>
        new(layout.X / _pixelsPerPoint, layout.Y / _pixelsPerPoint, layout.Width / _pixelsPerPoint, layout.Height / _pixelsPerPoint);

    private static void AddDeviceRect(PolygonSet set, RRect user, in Affine toDevice)
    {
        set.BeginContour();
        foreach (var (ux, uy) in new[] { (user.Left, user.Top), (user.Right, user.Top), (user.Right, user.Bottom), (user.Left, user.Bottom) })
        {
            var (dx, dy) = toDevice.Apply(ux, uy);
            set.Add(dx, dy);
        }
    }

    private PaintSource? CreatePaint(XBrush? brush)
    {
        if (UserToDevice.Invert() is not { } deviceToUser)
            return null;

        return PaintSource.From(brush, deviceToUser);
    }

    private void StrokeFlat(FlatPath flat, XPen pen)
    {
        var toDevice = UserToDevice;
        var scale = Math.Max(toDevice.MaxScale, 1e-9);

        var width = pen._width;
        // A zero (or sub-pixel) width draws the thinnest line the device can: one pixel.
        if (!(width * scale >= 1))
            width = 1 / scale;

        PaintSource? paint;
        if (pen._brush is not null)
        {
            paint = CreatePaint(pen._brush);
        }
        else
        {
            paint = PaintSource.FromColor(pen._color);
        }

        if (paint is null)
            return;

        var style = new StrokeStyle(
            width,
            pen._lineCap switch { XLineCap.Round => StrokeCap.Round, XLineCap.Square => StrokeCap.Square, _ => StrokeCap.Butt },
            pen._lineJoin switch { XLineJoin.Round => StrokeJoin.Round, XLineJoin.Bevel => StrokeJoin.Bevel, _ => StrokeJoin.Miter },
            pen._miterLimit,
            ResolveDashes(pen, width),
            pen._dashStyle == XDashStyle.Custom ? pen._dashOffset * pen._width : 0);

        var polygons = new PolygonSet();
        Stroker.Stroke(flat, style, toDevice, polygons);
        polygons.NormalizeWinding();
        FillPolygons(polygons, evenOdd: false, paint);
    }

    /// <summary>The dash lengths in user units, matching what <c>PdfGraphicsState.RealizePen</c> writes.</summary>
    private static double[]? ResolveDashes(XPen pen, double width)
    {
        // Presets are multiples of the pen's own width; a zero width never dashes.
        var w = pen._width;
        if (!(w > 0))
            return null;

        var dot = w;
        var dash = 3 * w;
        switch (pen._dashStyle)
        {
            case XDashStyle.Dash: return [dash, dot];
            case XDashStyle.Dot: return [dot];
            case XDashStyle.DashDot: return [dash, dot, dot, dot];
            case XDashStyle.DashDotDot: return [dash, dot, dot, dot, dot, dot];
            case XDashStyle.Custom:
                if (pen._dashPattern is not { Length: > 0 } pattern)
                    return null;

                var list = new List<double>(pattern.Length + 1);
                foreach (var v in pattern)
                    list.Add(v * w);

                // An odd count is padded the way GDI+ (and the PDF writer) does.
                if (list.Count % 2 == 1)
                    list.Add(0.2 * w);

                return [.. list];
            default:
                return null;
        }
    }

    // ---- compositing ------------------------------------------------------------------------------------

    private void FillPolygons(PolygonSet polygons, bool evenOdd, PaintSource paint, int opacity = 255, RBlendMode? mode = null)
    {
        var clip = _clips.Peek();
        if (clip.Bounds.IsEmpty || opacity <= 0)
            return;

        var sink = new PaintSink(this, paint, clip, mode ?? _blend, opacity);
        ScanlineRasterizer.Fill(polygons, evenOdd, clip.Bounds, ref sink);
    }

    private readonly struct PaintSink(RasterGraphics owner, PaintSource paint, ClipState clip, RBlendMode mode, int opacity) : ICoverageSink
    {
        public void Span(int y, int x0, ReadOnlySpan<byte> coverage) =>
            owner.CompositeRow(y, x0, coverage, paint, clip, mode, opacity);
    }

    private void CompositeRow(int y, int x0, ReadOnlySpan<byte> coverage, PaintSource paint, ClipState clip, RBlendMode mode, int opacity)
    {
        var bounds = clip.Bounds;
        if (y < bounds.Top || y >= bounds.Bottom)
            return;

        var start = Math.Max(x0, bounds.Left);
        var end = Math.Min(x0 + coverage.Length, bounds.Right);
        var count = end - start;
        if (count <= 0)
            return;

        if (_coverageScratch.Length < count)
        {
            _coverageScratch = new byte[count + 16];
            _pixelScratch = new byte[(count + 16) * 4];
        }

        var work = _coverageScratch.AsSpan(0, count);
        coverage.Slice(start - x0, count).CopyTo(work);

        if (clip.Mask is { } mask)
            PixelKernels.MultiplyCoverage(work, mask.AsSpan((y - bounds.Top) * bounds.Width + (start - bounds.Left), count));

        if (opacity < 255)
        {
            for (var i = 0; i < work.Length; i++)
                work[i] = (byte)PixelKernels.Div255(work[i] * opacity);
        }

        var dst = _surface.Row(y).Slice(start * 4, count * 4);

        if (mode == RBlendMode.Normal)
        {
            if (paint.IsSolid)
            {
                PixelKernels.BlendSolid(dst, work, paint.SolidColor);
                return;
            }

            var pixels = _pixelScratch.AsSpan(0, count * 4);
            paint.FillSpan(start, y, count, pixels);
            PixelKernels.BlendSpan(dst, pixels, work);
            return;
        }

        var source = _pixelScratch.AsSpan(0, count * 4);
        paint.FillSpan(start, y, count, source);
        for (var i = 0; i < count; i++)
        {
            int c = work[i];
            if (c == 0)
                continue;

            var p = i * 4;
            int sr = source[p], sg = source[p + 1], sb = source[p + 2], sa = source[p + 3];
            if (c != 255)
            {
                sr = PixelKernels.Div255(sr * c);
                sg = PixelKernels.Div255(sg * c);
                sb = PixelKernels.Div255(sb * c);
                sa = PixelKernels.Div255(sa * c);
            }

            BlendModes.Blend(mode, dst.Slice(p, 4), sr, sg, sb, sa);
        }
    }

    // ---- images -----------------------------------------------------------------------------------------

    public override void DrawImage(RImage image, RRect destRect) => DrawImageCore(image, destRect, null, 255, _blend);

    public override void DrawImage(RImage image, RRect destRect, RRect srcRect) => DrawImageCore(image, destRect, srcRect, 255, _blend);

    public override void DrawImageWithOpacity(RImage image, RRect destRect, double opacity, RBlendMode blendMode = RBlendMode.Normal) =>
        DrawImageCore(image, destRect, null, (int)Math.Round(Math.Clamp(opacity, 0, 1) * 255), blendMode);

    public override void DrawImageBlendedOver(RImage top, RImage bottom, RRect destRect, RBlendMode blendMode)
    {
        DrawImageCore(bottom, destRect, null, 255, RBlendMode.Normal);
        DrawImageCore(top, destRect, null, 255, blendMode);
    }

    public override void DrawImageMasked(RImage image, RImage maskImage, RRect destRect)
    {
        if (!TryGetBitmap(image, out var bitmap, out _, out _) || !TryGetBitmap(maskImage, out var mask, out _, out _))
            return;

        var result = Combine(bitmap, mask, static (int r, int g, int b, int a, int mr, int mg, int mb, int ma) =>
        {
            // Luminosity of the mask composited over black: premultiplied colour is already that.
            var lum = (77 * mr + 151 * mg + 28 * mb + 128) >> 8;
            return (PixelKernels.Div255(r * lum), PixelKernels.Div255(g * lum), PixelKernels.Div255(b * lum), PixelKernels.Div255(a * lum));
        });

        DrawBitmap(result, image.Width, image.Height, destRect, null, image.Interpolate, 255, _blend);
    }

    public override void DrawImageAlphaMasked(RImage image, RImage maskImage, RRect destRect, bool invert = false)
    {
        if (!TryGetBitmap(image, out var bitmap, out _, out _) || !TryGetBitmap(maskImage, out var mask, out _, out _))
            return;

        var result = Combine(bitmap, mask, (int r, int g, int b, int a, int mr, int mg, int mb, int ma) =>
        {
            var k = invert ? 255 - ma : ma;
            return (PixelKernels.Div255(r * k), PixelKernels.Div255(g * k), PixelKernels.Div255(b * k), PixelKernels.Div255(a * k));
        });

        DrawBitmap(result, image.Width, image.Height, destRect, null, image.Interpolate, 255, _blend);
    }

    public override void DrawImageWithColorMatrix(RImage image, RRect destRect, ColorMatrix matrix)
    {
        if (!TryGetBitmap(image, out var bitmap, out _, out _))
            return;

        DrawBitmap(ColorMatrixFilter.Apply(bitmap, matrix), image.Width, image.Height, destRect, null, image.Interpolate, 255, _blend);
    }

    private void DrawImageCore(RImage image, RRect destRect, RRect? srcRect, int opacity, RBlendMode mode)
    {
        if (!TryGetBitmap(image, out var bitmap, out var naturalWidth, out var naturalHeight))
            return;

        DrawBitmap(bitmap, naturalWidth, naturalHeight, destRect, srcRect, image.Interpolate, opacity, mode);
    }

    private static bool TryGetBitmap(RImage image, out Bitmap bitmap, out double naturalWidth, out double naturalHeight)
    {
        switch (image)
        {
            case RasterImage raster:
                bitmap = raster.GetBitmap();
                naturalWidth = raster.Width;
                naturalHeight = raster.Height;
                return true;

            case ImageAdapter { Image: not XForm } adapter when ImageBitmaps.Get(adapter.Image) is { } decoded:
                bitmap = decoded;
                naturalWidth = image.Width;
                naturalHeight = image.Height;
                return true;

            default:
                bitmap = null!;
                naturalWidth = naturalHeight = 0;
                return false;
        }
    }

    /// <summary>
    /// Draws <paramref name="bitmap"/> into <paramref name="destRect"/> (layout units). <paramref name="srcRect"/>
    /// selects a sub-rectangle in the image's natural units (whatever <see cref="RImage.Width"/> reports), or
    /// null for all of it.
    /// </summary>
    private void DrawBitmap(Bitmap bitmap, double naturalWidth, double naturalHeight, RRect destRect, RRect? srcRect,
        bool interpolate, int opacity, RBlendMode mode)
    {
        if (naturalWidth <= 0 || naturalHeight <= 0 || destRect.Width <= 0 || destRect.Height <= 0)
            return;

        var src = srcRect ?? new RRect(0, 0, naturalWidth, naturalHeight);
        if (src.Width <= 0 || src.Height <= 0)
            return;

        var dest = ToUser(destRect);
        var toDevice = UserToDevice;
        if (toDevice.Invert() is not { } deviceToUser)
            return;

        var kx = bitmap.Width / naturalWidth;
        var ky = bitmap.Height / naturalHeight;
        var m11 = src.Width * kx / dest.Width;
        var m22 = src.Height * ky / dest.Height;
        var userToBitmap = new Affine(m11, 0, 0, m22, -dest.Left * m11 + src.X * kx, -dest.Top * m22 + src.Y * ky);
        var deviceToBitmap = Affine.Then(deviceToUser, userToBitmap);

        // Smooth unless the image asked for crisp pixels and is being magnified.
        var scale = Math.Sqrt(Math.Abs(deviceToBitmap.Determinant));
        var smooth = interpolate || scale > 1 + 1e-6;

        var polygon = new PolygonSet();
        AddDeviceRect(polygon, dest, toDevice);
        FillPolygons(polygon, evenOdd: false, new BitmapPaint(bitmap, deviceToBitmap, smooth), opacity, mode);
    }

    /// <summary>Builds a new bitmap the size of <paramref name="a"/>, combining each pixel with the (nearest) pixel of <paramref name="b"/>.</summary>
    private static Bitmap Combine(Bitmap a, Bitmap b,
        Func<int, int, int, int, int, int, int, int, (int R, int G, int B, int A)> combine)
    {
        var result = new byte[a.Width * a.Height * 4];
        for (var y = 0; y < a.Height; y++)
        {
            var by = Math.Min(b.Height - 1, y * b.Height / a.Height);
            for (var x = 0; x < a.Width; x++)
            {
                var bx = Math.Min(b.Width - 1, x * b.Width / a.Width);
                var p = (y * a.Width + x) * 4;
                var q = (by * b.Width + bx) * 4;
                var (r, g, bl, al) = combine(a.Pixels[p], a.Pixels[p + 1], a.Pixels[p + 2], a.Pixels[p + 3],
                    b.Pixels[q], b.Pixels[q + 1], b.Pixels[q + 2], b.Pixels[q + 3]);
                result[p] = (byte)Math.Clamp(r, 0, 255);
                result[p + 1] = (byte)Math.Clamp(g, 0, 255);
                result[p + 2] = (byte)Math.Clamp(bl, 0, 255);
                result[p + 3] = (byte)Math.Clamp(al, 0, 255);
            }
        }

        return new Bitmap(a.Width, a.Height, result);
    }
}
