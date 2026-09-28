using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace PeachDrawing;

/// <summary>
/// An <see cref="Canvas"/> that paints into a <see cref="RasterSurface"/> instead of a PDF content stream.
/// It exists for the effects PDF cannot express in vector form; the bitmap it produces is embedded back into
/// the PDF by the graphics that asked for it (<see cref="Canvas.BeginRasterSurface"/>).
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
public sealed partial class RasterCanvas : Canvas
{
    /// <summary>Upper bound on the pixels of a tile created through <see cref="CreateTile"/>.</summary>
    internal const int MaxTilePixels = 64_000_000;

    private readonly RasterSurface _surface;
    private readonly double _pixelsPerPoint;
    private readonly double _sx;
    private readonly double _sy;
    private readonly Stack<Affine> _transforms = [];
    private readonly Stack<PaintBlendMode> _blendModes = [];
    private readonly Stack<ClipState> _clips = [];
    private Affine _ctm = Affine.Identity;
    private readonly Stack<Matrix3x2> _layoutTransforms = [];
    private Matrix3x2 _layoutCtm = Matrix3x2.Identity;
    private PaintBlendMode _blend = PaintBlendMode.Normal;
    private byte[] _coverageScratch;
    private byte[] _pixelScratch;

    /// <summary>Builds a canvas that paints into <paramref name="surface"/>.</summary>
    /// <param name="adapter">the render context this canvas resolves colors, fonts and paint objects from</param>
    /// <param name="surface">the pixel buffer this canvas paints into</param>
    /// <param name="pixelsPerPoint">the scale between this canvas's own coordinate space and true PDF points - see <see cref="Canvas.PixelsPerPoint"/>; <c>1</c> for a standalone canvas, where a layout unit is a device pixel directly</param>
    public RasterCanvas(RenderContext adapter, RasterSurface surface, double pixelsPerPoint)
        : base(adapter, surface.LayoutRect)
    {
        _surface = surface;
        _pixelsPerPoint = pixelsPerPoint;

        // pixels per point = pixels per layout unit * layout units per point.
        _sx = surface.PixelsPerUnitX * pixelsPerPoint;
        _sy = surface.PixelsPerUnitY * pixelsPerPoint;

        _clips.Push(new ClipState(new IntRect(0, 0, surface.Width, surface.Height), null));
        _coverageScratch = new byte[surface.Width + 4];
        _pixelScratch = new byte[(surface.Width + 4) * 4];
    }

    /// <summary>The pixel buffer this canvas paints into.</summary>
    public RasterSurface Surface => _surface;

    /// <summary>
    /// This canvas's pixels as a premultiplied-RGBA8 <see cref="PixelBuffer"/> - the same contract
    /// <see cref="Image.GetPixels"/> uses, so a caller can sample a finished standalone canvas exactly the
    /// way it would sample any other <see cref="Image"/>. No copy: it wraps the surface's own backing array.
    /// </summary>
    public PixelBuffer ToPixelBuffer() => new(_surface.Width, _surface.Height, _surface.Buffer.AsMemory(0, _surface.Width * _surface.Height * 4));

    /// <summary>
    /// Encodes this canvas's pixels into <paramref name="stream"/> in any format PeachImage supports (PNG,
    /// JPEG, WebP, ...), for a standalone canvas built through <see cref="RasterRenderContext.CreateCanvas"/>.
    /// </summary>
    public void Save(System.IO.Stream stream, string formatName, PeachImage.EncoderOptions options) =>
        RasterSurfaceEncoding.Save(_surface, stream, formatName, options);

    /// <inheritdoc cref="Save"/>
    public System.Threading.Tasks.Task SaveAsync(System.IO.Stream stream, string formatName, PeachImage.EncoderOptions options, System.Threading.CancellationToken cancellationToken = default) =>
        RasterSurfaceEncoding.SaveAsync(_surface, stream, formatName, options, cancellationToken);

    /// <inheritdoc/>
    public override Matrix3x2 CurrentTransform => _layoutCtm;

    /// <summary>Starts this graphics' transform at <paramref name="requester"/>'s: a raster region paints in its requester's current user space.</summary>
    internal void SeedTransform(Matrix3x2 requester) => _layoutCtm = requester;

    /// <inheritdoc/>
    public override double PixelsPerPoint => _pixelsPerPoint;

    /// <inheritdoc/>
    public override bool IsOffscreenTile => true;

    /// <inheritdoc/>
    public override bool PrefersRasterGroups => true;

    /// <inheritdoc/>
    protected override bool SupportsLayerEffects => true;

    /// <inheritdoc/>
    protected override void ApplyLayerEffects(RasterSurface surface, IReadOnlyList<LayerEffect> effects) =>
        RasterLayerEffects.Apply(surface, effects);

    /// <summary>User space (points) to surface pixels under the current transform.</summary>
    private Affine UserToDevice => Affine.Then(_ctm, new Affine(_sx, 0, 0, _sy, -_surface.GridX, -_surface.GridY));

    private double DeviceScale => Math.Max(UserToDevice.MaxScale, 1e-9);

    // ---- state ------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public override void PopClip()
    {
        _clipStack.Pop();
        if (_clips.Count > 1)
            _clips.Pop();
    }

    /// <inheritdoc/>
    public override void PushClip(Rect rect)
    {
        _clipStack.Push(rect);
        var user = new Rect(rect.X / _pixelsPerPoint, rect.Y / _pixelsPerPoint, rect.Width / _pixelsPerPoint, rect.Height / _pixelsPerPoint);
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
        _clips.Push(current.Intersect(polygon, evenOdd: false, _adapter.RasterAntiAliasing));
    }

    private static bool IsWhole(double v) => Math.Abs(v - Math.Round(v)) < 1e-3;

    /// <inheritdoc/>
    public override void PushClip(GraphicsPath path)
    {
        _clipStack.Push(_clipStack.Peek());
        var toDevice = UserToDevice;
        var flat = FlatPath.From(path, 0.1 / DeviceScale);
        var polygon = new PolygonSet();
        polygon.AddTransformed(flat.Contours, toDevice);
        var evenOdd = path.FillMode == FillMode.EvenOdd;
        _clips.Push(_clips.Peek().Intersect(polygon, evenOdd, _adapter.RasterAntiAliasing));
    }

    /// <inheritdoc/>
    public override void PushClipExclude(Rect rect)
    {
        // Unused by the paint pipeline; the PDF backend leaves it a no-op as well.
    }

    /// <inheritdoc/>
    public override void PushTransform(Matrix3x2 matrix)
    {
        _transforms.Push(_ctm);
        _layoutTransforms.Push(_layoutCtm);
        _layoutCtm = matrix.Then(_layoutCtm);

        // Only the translation is divided by PixelsPerPoint, exactly as GraphicsAdapter does: the linear
        // part is already scale-neutral (see Canvas.PixelsPerPoint).
        var m = new Affine(matrix.M11, matrix.M12, matrix.M21, matrix.M22,
            matrix.M31 / _pixelsPerPoint, matrix.M32 / _pixelsPerPoint);
        _ctm = Affine.Then(m, _ctm);
    }

    /// <inheritdoc/>
    public override void PopTransform()
    {
        if (_transforms.Count > 0)
            _ctm = _transforms.Pop();

        if (_layoutTransforms.Count > 0)
            _layoutCtm = _layoutTransforms.Pop();
    }

    /// <inheritdoc/>
    public override void PushBlendMode(PaintBlendMode mode)
    {
        _blendModes.Push(_blend);
        _blend = mode;
    }

    /// <inheritdoc/>
    public override void PopBlendMode()
    {
        if (_blendModes.Count > 0)
            _blend = _blendModes.Pop();
    }

    // This backend has one anti-aliasing setting for the whole render (PdfGenerateConfig.RasterAntiAliasing,
    // read here as _adapter.RasterAntiAliasing), applied uniformly by ScanlineRasterizer to every fill, stroke,
    // image and glyph. There is no separate per-call smoothing mode to switch here, so this stays a no-op.
    /// <inheritdoc/>
    public override object SetAntiAliasSmoothingMode() => true;

    /// <inheritdoc/>
    public override void ReturnPreviousSmoothingMode(object? prevMode)
    {
    }

    /// <inheritdoc/>
    public override GraphicsPath GetGraphicsPath() => new RasterGraphicsPath();

    // Tagged PDF structure does not exist in a bitmap.
    /// <inheritdoc/>
    public override void BeginMarkedContent(string structureType, int mcid) { }

    /// <inheritdoc/>
    public override void EndMarkedContent() { }

    /// <inheritdoc/>
    public override void BeginArtifact() { }

    /// <inheritdoc/>
    public override void BeginVariableText() { }

    /// <inheritdoc/>
    public override void EndVariableText() { }

    /// <inheritdoc/>
    public override void Dispose()
    {
    }

    // ---- tiles ------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public override (Canvas Graphics, Image Image)? CreateTile(double width, double height)
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
        var tileGraphics = new RasterCanvas(_adapter, tile, _pixelsPerPoint);
        // See Canvas.CreateTile's own doc remarks: seeding CurrentTransform (bookkeeping only - the
        // tile's actual pixel geometry is governed by `tile`'s own grid/pitch above, untouched by this)
        // lets a reader inside the tile relate its coordinate space back to whatever space content
        // outside the tile is measured in, same as BeginRasterSurface already does below.
        tileGraphics.SeedTransform(_layoutCtm);
        return (tileGraphics, new RasterImage(tile, width, height));
    }

    // ---- nested raster regions --------------------------------------------------------------------------

    /// <summary>
    /// A raster region inside a raster region (a filtered element within a filtered ancestor). It keeps this surface's
    /// own pixel pitch and grid instead of going back to the document's DPI, so compositing it back is an exact pixel
    /// copy rather than a resample, and it is cut to this surface: nothing outside it was painted to begin with.
    /// </summary>
    public override RasterRegion? BeginRasterSurface(Rect layoutBounds, double? dpiOverride = null)
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
        var graphics = new RasterCanvas(_adapter, nested, _pixelsPerPoint);
        graphics.SeedTransform(_layoutCtm);
        return new RasterRegion(graphics, nested);
    }

        /// <inheritdoc/>
    public override void DrawRaster(RasterSurface surface)
    {
        var bitmap = new Bitmap(surface.Width, surface.Height, surface.Buffer);
        var rect = surface.LayoutRect;
        // Same pitch, on the same grid: nearest-neighbour is an exact pixel copy, where a bilinear tap could pick up a
        // neighbour through floating-point noise in the inverse mapping.
        DrawBitmap(bitmap, rect.Width, rect.Height, rect, null, interpolate: false, opacity: 255, _blend);
    }

    // ---- fills and strokes ------------------------------------------------------------------------------

    /// <inheritdoc/>
    public override void DrawRectangle(Brush brush, double x, double y, double width, double height)
    {
        var paint = CreatePaint(brush);
        if (paint is null)
            return;

        var polygon = new PolygonSet();
        AddDeviceRect(polygon, ToUser(new Rect(x, y, width, height)), UserToDevice);
        FillPolygons(polygon, evenOdd: false, paint);
    }

    /// <inheritdoc/>
    public override void DrawRectangle(Pen pen, double x, double y, double width, double height)
    {
        var r = ToUser(new Rect(x, y, width, height));
        var flat = new FlatPath();
        flat.AddContour([(r.Left, r.Top), (r.Right, r.Top), (r.Right, r.Bottom), (r.Left, r.Bottom)], closed: true);
        StrokeFlat(flat, pen);
    }

    /// <inheritdoc/>
    public override void DrawLine(Pen pen, double x1, double y1, double x2, double y2)
    {
        var flat = new FlatPath();
        flat.AddContour([(x1 / _pixelsPerPoint, y1 / _pixelsPerPoint), (x2 / _pixelsPerPoint, y2 / _pixelsPerPoint)], closed: false);
        StrokeFlat(flat, pen);
    }

    /// <inheritdoc/>
    public override void DrawPolygon(Brush brush, PaintPoint[] points)
    {
        if (points is not { Length: > 1 })
            return;

        var paint = CreatePaint(brush);
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

    /// <inheritdoc/>
    public override void DrawPath(Brush brush, GraphicsPath path)
    {
        var paint = CreatePaint(brush);
        if (paint is null)
            return;

        var flat = FlatPath.From(path, 0.1 / DeviceScale);
        var polygon = new PolygonSet();
        polygon.AddTransformed(flat.Contours, UserToDevice);
        FillPolygons(polygon, path.FillMode == FillMode.EvenOdd, paint);
    }

    /// <inheritdoc/>
    public override void DrawPath(Pen pen, GraphicsPath path)
    {
        var flat = FlatPath.From(path, 0.1 / DeviceScale);
        StrokeFlat(flat, pen);
    }

    private Rect ToUser(Rect layout) =>
        new(layout.X / _pixelsPerPoint, layout.Y / _pixelsPerPoint, layout.Width / _pixelsPerPoint, layout.Height / _pixelsPerPoint);

    private static void AddDeviceRect(PolygonSet set, Rect user, in Affine toDevice)
    {
        set.BeginContour();
        foreach (var (ux, uy) in new[] { (user.Left, user.Top), (user.Right, user.Top), (user.Right, user.Bottom), (user.Left, user.Bottom) })
        {
            var (dx, dy) = toDevice.Apply(ux, uy);
            set.Add(dx, dy);
        }
    }

    private PaintSource? CreatePaint(Brush? brush)
    {
        if (UserToDevice.Invert() is not { } deviceToUser)
            return null;

        return PaintSource.From(brush, deviceToUser);
    }

    private void StrokeFlat(FlatPath flat, Pen pen)
    {
        var toDevice = UserToDevice;
        var scale = Math.Max(toDevice.MaxScale, 1e-9);

        var width = pen.Width;
        // A zero (or sub-pixel) width draws the thinnest line the device can: one pixel.
        if (!(width * scale >= 1))
            width = 1 / scale;

        var paint = CreatePaint(pen.Paint);
        if (paint is null)
            return;

        var style = new StrokeStyle(
            width,
            pen.LineCap switch { LineCap.Round => StrokeCap.Round, LineCap.Square => StrokeCap.Square, _ => StrokeCap.Butt },
            pen.LineJoin switch { LineJoin.Round => StrokeJoin.Round, LineJoin.Bevel => StrokeJoin.Bevel, _ => StrokeJoin.Miter },
            pen.MiterLimit,
            ResolveDashes(pen, width),
            pen.DashStyle == DashStyle.Custom ? pen.DashOffset : 0);

        var polygons = new PolygonSet();
        Stroker.Stroke(flat, style, toDevice, polygons);
        polygons.NormalizeWinding();
        FillPolygons(polygons, evenOdd: false, paint);
    }

    /// <summary>The dash lengths in user units, matching what <c>PdfGraphicsState.RealizePen</c> writes.</summary>
    internal static double[]? ResolveDashes(Pen pen, double width)
    {
        // Presets are multiples of the pen's own width; a zero width never dashes.
        var w = pen.Width;
        if (!(w > 0))
            return null;

        var dot = w;
        var dash = 3 * w;
        switch (pen.DashStyle)
        {
            case DashStyle.Dash: return [dash, dot];
            case DashStyle.Dot: return [dot];
            case DashStyle.DashDot: return [dash, dot, dot, dot];
            case DashStyle.DashDotDot: return [dash, dot, dot, dot, dot, dot];
            case DashStyle.Custom:
                if (pen.DashPattern is not { Length: > 0 } pattern)
                    return null;

                var list = new List<double>(pattern.Length + 1);
                foreach (var v in pattern)
                    list.Add(v);

                // An odd count is padded the way GDI+ (and the PDF writer) does.
                if (list.Count % 2 == 1)
                    list.Add(0.2 * w);

                return [.. list];
            default:
                return null;
        }
    }

    // ---- compositing ------------------------------------------------------------------------------------

    private void FillPolygons(PolygonSet polygons, bool evenOdd, PaintSource paint, int opacity = 255, PaintBlendMode? mode = null)
    {
        var clip = _clips.Peek();
        if (clip.Bounds.IsEmpty || opacity <= 0)
            return;

        var sink = new PaintSink(this, paint, clip, mode ?? _blend, opacity);
        ScanlineRasterizer.Fill(polygons, evenOdd, clip.Bounds, ref sink, _adapter.RasterAntiAliasing);
    }

    private readonly struct PaintSink(RasterCanvas owner, PaintSource paint, ClipState clip, PaintBlendMode mode, int opacity) : ICoverageSink
    {
        public void Span(int y, int x0, ReadOnlySpan<byte> coverage) =>
            owner.CompositeRow(y, x0, coverage, paint, clip, mode, opacity);
    }

    private void CompositeRow(int y, int x0, ReadOnlySpan<byte> coverage, PaintSource paint, ClipState clip, PaintBlendMode mode, int opacity)
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

        if (mode == PaintBlendMode.Normal)
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

    /// <inheritdoc/>
    public override void DrawImage(Image image, Rect destRect) => DrawImageCore(image, destRect, null, 255, _blend);

    /// <inheritdoc/>
    public override void DrawImage(Image image, Rect destRect, Rect srcRect) => DrawImageCore(image, destRect, srcRect, 255, _blend);

    /// <inheritdoc/>
    public override void DrawImageWithOpacity(Image image, Rect destRect, double opacity, PaintBlendMode blendMode = PaintBlendMode.Normal) =>
        DrawImageCore(image, destRect, null, (int)Math.Round(Math.Clamp(opacity, 0, 1) * 255), blendMode);

    /// <inheritdoc/>
    public override void DrawImageBlendedOver(Image top, Image bottom, Rect destRect, PaintBlendMode blendMode)
    {
        DrawImageCore(bottom, destRect, null, 255, PaintBlendMode.Normal);
        DrawImageCore(top, destRect, null, 255, blendMode);
    }

    /// <inheritdoc/>
    public override void DrawImageMasked(Image image, Image maskImage, Rect destRect)
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

    /// <inheritdoc/>
    public override void DrawImageAlphaMasked(Image image, Image maskImage, Rect destRect, bool invert = false)
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

    /// <inheritdoc/>
    public override void DrawImageWithColorMatrix(Image image, Rect destRect, ColorMatrix matrix)
    {
        if (!TryGetBitmap(image, out var bitmap, out _, out _))
            return;

        DrawBitmap(ColorMatrixFilter.Apply(bitmap, matrix), image.Width, image.Height, destRect, null, image.Interpolate, 255, _blend);
    }

    private void DrawImageCore(Image image, Rect destRect, Rect? srcRect, int opacity, PaintBlendMode mode)
    {
        if (!TryGetBitmap(image, out var bitmap, out var naturalWidth, out var naturalHeight))
            return;

        DrawBitmap(bitmap, naturalWidth, naturalHeight, destRect, srcRect, image.Interpolate, opacity, mode);
    }

    private static bool TryGetBitmap(Image image, out Bitmap bitmap, out double naturalWidth, out double naturalHeight)
    {
        // RasterImage already keeps its pixels as a Bitmap (its own tile surface's own backing buffer) -
        // reuse that instance directly rather than round-tripping it through PixelBuffer, so a tile drawn
        // many times (a repeating background) doesn't re-copy its pixels on every draw.
        if (image is RasterImage raster)
        {
            bitmap = raster.GetBitmap();
            naturalWidth = raster.Width;
            naturalHeight = raster.Height;
            return true;
        }

        if (image.GetPixels() is { } buffer)
        {
            // ImageAdapter.GetPixels() wraps its own cached decode's array as-is (no copy on its side),
            // so this recovers that same array with none here either - important since a tiled background
            // draws the same image many times, and re-copying its pixels on every tile would show up.
            var bytes = System.Runtime.InteropServices.MemoryMarshal.TryGetArray(buffer.PremultipliedRgba, out var segment) && segment.Offset == 0 && segment.Count == segment.Array!.Length
                ? segment.Array
                : buffer.PremultipliedRgba.ToArray();
            bitmap = new Bitmap(buffer.Width, buffer.Height, bytes);
            naturalWidth = image.Width;
            naturalHeight = image.Height;
            return true;
        }

        bitmap = null!;
        naturalWidth = naturalHeight = 0;
        return false;
    }

    /// <summary>
    /// Draws <paramref name="bitmap"/> into <paramref name="destRect"/> (layout units). <paramref name="srcRect"/>
    /// selects a sub-rectangle in the image's natural units (whatever <see cref="Image.Width"/> reports), or
    /// null for all of it.
    /// </summary>
    private void DrawBitmap(Bitmap bitmap, double naturalWidth, double naturalHeight, Rect destRect, Rect? srcRect,
        bool interpolate, int opacity, PaintBlendMode mode)
    {
        if (naturalWidth <= 0 || naturalHeight <= 0 || destRect.Width <= 0 || destRect.Height <= 0)
            return;

        var src = srcRect ?? new Rect(0, 0, naturalWidth, naturalHeight);
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
