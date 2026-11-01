using PeachDrawing.Core;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace PeachDrawing.Skia;

/// <summary>
/// A <see cref="Canvas"/> that draws with Skia: shapes, gradients, clips, layers, text and images go to an <see cref="SKCanvas"/> the caller
/// already has (<see cref="FromCanvas"/>) - a GPU surface's, a bitmap's, a picture recorder's - or to the surface a <see cref="SkiaComposer"/>
/// is given.
/// </summary>
/// <remarks>
/// <para>
/// <b>Units.</b> Like the SVG and Win2D canvases, the <see cref="SKCanvas"/>'s user space is the canvas's layout units: rectangles, lines,
/// polygons, image rectangles, brush geometry and text positions are used as given. What is in points (a <see cref="GraphicsPath"/>'s
/// coordinates, a pen's width and dashes) is multiplied by <see cref="PixelsPerPoint"/> first. A matrix already set on the host's
/// <see cref="SKCanvas"/> (a DPI scale, a zoom) is kept: this canvas draws under it.
/// </para>
/// <para>
/// <b>Surfaces.</b> This package never creates the surface it draws on. Hardware acceleration is whatever the surface behind the
/// <see cref="SKCanvas"/> you pass has - a canvas of a GPU-backed <c>SKSurface</c> draws on the GPU.
/// </para>
/// <para>
/// <b>Lifetime.</b> Everything native this canvas makes it releases: a clip, transform or blend pushed holds a save level of the
/// <see cref="SKCanvas"/> until the matching pop; a paint, shader or path lives for the one draw call that needs it; a tile owns a picture
/// recorder. <see cref="Dispose"/> pops whatever is still pushed, in reverse order, and restores the <see cref="SKCanvas"/> to the save level
/// it found. The <see cref="SKCanvas"/> passed to <see cref="FromCanvas"/> stays the caller's: it is never disposed or flushed here, and is
/// usable again once this canvas is disposed. A disposed canvas throws <see cref="ObjectDisposedException"/> from every drawing call. A
/// canvas must be used from one thread at a time.
/// </para>
/// </remarks>
public sealed partial class SkiaCanvas : Canvas
{
    private enum OpenKind { Clip, Transform, Blend }

    private readonly record struct Open(OpenKind Kind, int SaveCount, IDisposable? Resource, Matrix3x2 Ctm, PaintBlendMode Blend);

    private readonly SkiaRenderContext _context;
    private readonly SKCanvas _canvas;
    private readonly bool _isOffscreen;
    private readonly bool _countsAsTile;
    private readonly double _ppp;
    private readonly int _baseSave;
    private readonly List<Open> _open = [];
    private readonly Stack<bool> _antialiasing = new();
    private Matrix3x2 _ctm;
    private PaintBlendMode _blend = PaintBlendMode.Normal;
    private bool _smooth = true;
    private bool _disposed;

    private SkiaCanvas(SkiaRenderContext context, SKCanvas canvas, Rect initialClip, double pixelsPerPoint, bool isOffscreen, bool countsAsTile,
        Matrix3x2 seed, SkiaLinkOptions? linkOptions, bool smooth)
        : base(context, initialClip)
    {
        _context = context;
        _canvas = canvas;
        _isOffscreen = isOffscreen;
        _countsAsTile = countsAsTile;
        _ppp = pixelsPerPoint > 0 ? pixelsPerPoint : 1.0;
        _ctm = seed;
        _smooth = smooth;

        // Everything this canvas does sits above this level, so disposing it can put the host's canvas back exactly as it was.
        _baseSave = canvas.Save();

        // A host's canvas may already scale its drawing (a DPI scale, a zoom); a pointer in its own coordinates is that many
        // layout units apart.
        var m = canvas.TotalMatrix;
        var scale = Math.Sqrt(Math.Abs((double)m.ScaleX * m.ScaleY - (double)m.SkewX * m.SkewY));
        Links = new SkiaLinkMap(linkOptions, scale > 1e-9 ? 1.0 / scale : 1.0);
    }

    /// <summary>
    /// Makes a canvas that draws into an <see cref="SKCanvas"/> the caller already has: the one a view hands its paint handler, a surface's,
    /// a bitmap's.
    /// </summary>
    /// <param name="context">where fonts and images come from; its <see cref="SkiaRenderContext.PixelsPerPoint"/> sets the canvas's</param>
    /// <param name="canvas">the canvas to draw into. It is borrowed: this canvas never disposes or flushes it, and restores its save level and so its matrix and clip when it is disposed</param>
    /// <param name="bounds">the area being drawn, in layout units (the initial clip bound that culling reads)</param>
    /// <param name="linkOptions">which links <see cref="Links"/> may open, or <see langword="null"/> for the defaults</param>
    /// <returns>the canvas; dispose it when the drawing is done</returns>
    public static SkiaCanvas FromCanvas(SkiaRenderContext context, SKCanvas canvas, Rect bounds, SkiaLinkOptions? linkOptions = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(canvas);
        return new SkiaCanvas(context, canvas, bounds, context.PixelsPerPoint, false, false, Matrix3x2.Identity, linkOptions, true);
    }

    /// <summary>A canvas over a picture recorder's canvas: a tile.</summary>
    private SkiaCanvas NewTileCanvas(SKCanvas recording, Rect clip)
    {
        _context.Counts.Tile(1);

        // The recording starts at its own origin; the transform a reader consults is seeded from this canvas's (see Canvas.CreateTile).
        return new SkiaCanvas(_context, recording, clip, _ppp, true, true, _ctm, null, _smooth);
    }

    /// <summary>A canvas that shares this one's <see cref="SKCanvas"/> for the content of a layer: it stands above the layer's save level.</summary>
    private SkiaCanvas NewLayerCanvas(Rect clip) => new(_context, _canvas, clip, _ppp, true, false, _ctm, null, _smooth);

    /// <summary>The links and named places painted so far; see <see cref="SkiaLinkMap"/>. It survives <see cref="Dispose"/>.</summary>
    public SkiaLinkMap Links { get; }

    /// <inheritdoc />
    public override double PixelsPerPoint => _ppp;

    /// <inheritdoc />
    public override bool IsOffscreenTile => _isOffscreen;

    /// <inheritdoc />
    public override Matrix3x2 CurrentTransform => _ctm;

    /// <inheritdoc />
    public override (double X, double Y) TransformScale
    {
        get
        {
            var x = Math.Sqrt((double)_ctm.M11 * _ctm.M11 + (double)_ctm.M12 * _ctm.M12);
            var y = Math.Sqrt((double)_ctm.M21 * _ctm.M21 + (double)_ctm.M22 * _ctm.M22);
            return (x > 1e-6 ? x : 1.0, y > 1e-6 ? y : 1.0);
        }
    }

    internal SkiaRenderContext Context => _context;

    internal SKCanvas Surface => _canvas;

    private void Guard() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static SKMatrix ToMatrix(Matrix3x2 m) => new(m.M11, m.M21, m.M31, m.M12, m.M22, m.M32, 0, 0, 1);

    // ---- the push/pop stack -------------------------------------------------------------------------------

    private void Push(OpenKind kind, IDisposable? resource = null)
    {
        var save = _canvas.Save();
        _open.Add(new Open(kind, save, resource, _ctm, _blend));
    }

    // Pops the innermost push, whatever its kind: SuspendClipping and ResumeClipping unwind and replay clips around other pushes, so a pop
    // has to undo the latest push and not search for one of its own kind.
    private void Pop(OpenKind expected)
    {
        Guard();
        if (_open.Count == 0)
            throw new InvalidOperationException($"There is no {expected} pushed on this canvas to pop.");

        var entry = _open[^1];
        _open.RemoveAt(_open.Count - 1);
        Release(entry);
    }

    private void Release(Open entry)
    {
        _canvas.RestoreToCount(entry.SaveCount);
        entry.Resource?.Dispose();
        _ctm = entry.Ctm;
        _blend = entry.Blend;
    }

    // ---- clip, transform, blend ---------------------------------------------------------------------------

    /// <inheritdoc />
    public override void PushClip(Rect rect)
    {
        Guard();
        Push(OpenKind.Clip);
        _canvas.ClipRect(SKRect.Create((float)rect.X, (float)rect.Y, (float)Math.Max(rect.Width, 0), (float)Math.Max(rect.Height, 0)), SKClipOperation.Intersect, _smooth);
        _clipStack.Push(rect);
    }

    /// <inheritdoc />
    public override void PushClip(GraphicsPath path)
    {
        Guard();

        // No simple bounding rectangle for an arbitrary path, so the tracked clip bound stays as it was (it is only used for culling).
        using var shape = BuildPath(path);
        Push(OpenKind.Clip);
        _canvas.ClipPath(shape, SKClipOperation.Intersect, _smooth);
        _clipStack.Push(_clipStack.Peek());
    }

    /// <inheritdoc />
    public override void PushClipExclude(Rect rect)
    {
        Guard();
        Push(OpenKind.Clip);
        _canvas.ClipRect(SKRect.Create((float)rect.X, (float)rect.Y, (float)Math.Max(rect.Width, 0), (float)Math.Max(rect.Height, 0)), SKClipOperation.Difference, _smooth);
        _clipStack.Push(_clipStack.Peek());
    }

    /// <inheritdoc />
    public override void PopClip()
    {
        Pop(OpenKind.Clip);
        if (_clipStack.Count > 1)
            _clipStack.Pop();
    }

    /// <inheritdoc />
    public override void PushTransform(Matrix3x2 matrix)
    {
        Guard();
        Push(OpenKind.Transform);
        _ctm = matrix.Then(_ctm);
        _canvas.Concat(ToMatrix(matrix));
    }

    /// <inheritdoc />
    public override void PopTransform() => Pop(OpenKind.Transform);

    /// <inheritdoc />
    public override void PushBlendMode(PaintBlendMode mode)
    {
        Guard();
        Push(OpenKind.Blend);
        _blend = mode;
    }

    /// <inheritdoc />
    public override void PopBlendMode() => Pop(OpenKind.Blend);

    /// <summary>Every <see cref="PaintBlendMode"/> has a Skia blend mode of its own, so a pushed blend applies to each draw exactly.</summary>
    private static SKBlendMode BlendModeFor(PaintBlendMode mode) => mode switch
    {
        PaintBlendMode.Multiply => SKBlendMode.Multiply,
        PaintBlendMode.Screen => SKBlendMode.Screen,
        PaintBlendMode.Overlay => SKBlendMode.Overlay,
        PaintBlendMode.Darken => SKBlendMode.Darken,
        PaintBlendMode.Lighten => SKBlendMode.Lighten,
        PaintBlendMode.ColorDodge => SKBlendMode.ColorDodge,
        PaintBlendMode.ColorBurn => SKBlendMode.ColorBurn,
        PaintBlendMode.HardLight => SKBlendMode.HardLight,
        PaintBlendMode.SoftLight => SKBlendMode.SoftLight,
        PaintBlendMode.Difference => SKBlendMode.Difference,
        PaintBlendMode.Exclusion => SKBlendMode.Exclusion,
        PaintBlendMode.Hue => SKBlendMode.Hue,
        PaintBlendMode.Saturation => SKBlendMode.Saturation,
        PaintBlendMode.Color => SKBlendMode.Color,
        PaintBlendMode.Luminosity => SKBlendMode.Luminosity,
        PaintBlendMode.Plus => SKBlendMode.Plus,
        _ => SKBlendMode.SrcOver,
    };

    /// <inheritdoc />
    public override object SetAntiAliasSmoothingMode()
    {
        Guard();
        var previous = _smooth;
        _smooth = true;
        return previous;
    }

    /// <inheritdoc />
    public override void ReturnPreviousSmoothingMode(object? prevMode)
    {
        Guard();
        if (prevMode is bool previous)
            _smooth = previous;
    }

    /// <inheritdoc />
    public override void PushAntiAlias(bool enabled)
    {
        Guard();
        _antialiasing.Push(_smooth);
        _smooth = enabled && _context.RasterAntiAliasing;
    }

    /// <inheritdoc />
    public override void PopAntiAlias()
    {
        Guard();
        if (_antialiasing.Count > 0)
            _smooth = _antialiasing.Pop();
    }

    /// <inheritdoc />
    public override GraphicsPath GetGraphicsPath() => new SkiaGraphicsPath();

    // ---- tagging: nothing to tag in pixels -----------------------------------------------------------------

    /// <inheritdoc />
    public override void BeginMarkedContent(string structureType, int mcid)
    {
    }

    /// <inheritdoc />
    public override void EndMarkedContent()
    {
    }

    /// <inheritdoc />
    public override void BeginArtifact()
    {
    }

    /// <inheritdoc />
    public override void BeginVariableText()
    {
    }

    /// <inheritdoc />
    public override void EndVariableText()
    {
    }

    // ---- lifetime -----------------------------------------------------------------------------------------

    /// <summary>
    /// Pops everything still pushed and puts the <see cref="SKCanvas"/> back to the save level it was at. Safe to call more than once. A canvas
    /// passed to <see cref="FromCanvas"/> is left open and usable.
    /// </summary>
    public override void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            // Layers still open sit above everything pushed here, so they end first.
            AbandonLayers();

            // Release in reverse, each under the state it was pushed with. One that throws must not leave the others undisposed.
            for (var i = _open.Count - 1; i >= 0; i--)
                Release(_open[i]);

            _open.Clear();
        }
        finally
        {
            ReleaseConverted();
            ReleaseBitmapGlyphs();
            _canvas.RestoreToCount(_baseSave);

            if (_countsAsTile)
                _context.Counts.Tile(-1);
        }
    }

    // ---- geometry -----------------------------------------------------------------------------------------

    /// <summary>The Skia path for <paramref name="path"/>, in layout units. The caller disposes it.</summary>
    private SKPath BuildPath(GraphicsPath path)
    {
        var result = new SKPath
        {
            FillType = path.FillMode == FillMode.EvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding,
        };

        foreach (var contour in path.GetCurveContours())
        {
            if (contour.Commands.Count == 0)
                continue;

            result.MoveTo(Point(contour.Start));
            foreach (var command in contour.Commands)
            {
                if (command.Kind == PathCommandKind.Line)
                    result.LineTo(Point(command.End));
                else
                    result.CubicTo(Point(command.Control1), Point(command.Control2), Point(command.End));
            }

            if (contour.Closed)
                result.Close();
        }

        return result;
    }

    // A path's coordinates are in points; the canvas works in layout units.
    private SKPoint Point(PaintPoint p) => new((float)(p.X * _ppp), (float)(p.Y * _ppp));

    // ---- shapes -------------------------------------------------------------------------------------------

    /// <inheritdoc />
    public override void DrawLine(Pen pen, double x1, double y1, double x2, double y2)
    {
        Guard();
        Stroke(pen, paint => _canvas.DrawLine((float)x1, (float)y1, (float)x2, (float)y2, paint));
    }

    /// <inheritdoc />
    public override void DrawRectangle(Pen pen, double x, double y, double width, double height)
    {
        Guard();
        Stroke(pen, paint => _canvas.DrawRect((float)x, (float)y, (float)width, (float)height, paint));
    }

    /// <inheritdoc />
    public override void DrawRectangle(Brush brush, double x, double y, double width, double height)
    {
        Guard();
        Fill(brush, paint => _canvas.DrawRect((float)x, (float)y, (float)width, (float)height, paint));
    }

    /// <inheritdoc />
    public override void DrawPath(Pen pen, GraphicsPath path)
    {
        Guard();
        using var shape = BuildPath(path);
        Stroke(pen, paint => _canvas.DrawPath(shape, paint));
    }

    /// <inheritdoc />
    public override void DrawPath(Brush brush, GraphicsPath path)
    {
        Guard();
        using var shape = BuildPath(path);
        Fill(brush, paint => _canvas.DrawPath(shape, paint));
    }

    /// <inheritdoc />
    public override void DrawPolygon(Brush brush, PaintPoint[] points)
    {
        Guard();
        if (points.Length < 3)
            return;

        using var shape = new SKPath();
        shape.MoveTo((float)points[0].X, (float)points[0].Y);
        for (var i = 1; i < points.Length; i++)
            shape.LineTo((float)points[i].X, (float)points[i].Y);

        shape.Close();
        Fill(brush, paint => _canvas.DrawPath(shape, paint));
    }
}
