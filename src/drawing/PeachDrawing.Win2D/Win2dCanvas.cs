using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using PeachDrawing.Core;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;

namespace PeachDrawing.Win2D;

/// <summary>
/// A <see cref="Canvas"/> that draws with Win2D: shapes, gradients, clips, layers, text and images go to a
/// <see cref="CanvasDrawingSession"/>, either one the caller already has (<see cref="FromSession"/>) or the offscreen bitmap of a
/// <see cref="Win2dComposer"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Units.</b> Like the SVG canvas, the session's user space is the canvas's layout units: rectangles, lines, polygons, image rectangles,
/// brush geometry and text positions are used as given. What is in points (a <see cref="GraphicsPath"/>'s coordinates, a pen's width and
/// dashes) is multiplied by <see cref="PixelsPerPoint"/> first.
/// </para>
/// <para>
/// <b>Lifetime.</b> Everything native this canvas makes it releases: a clip pushed with <c>PushClip</c> holds a Win2D layer until the
/// matching <c>PopClip</c>; a brush, stroke style or geometry lives for the one draw call that needs it; a tile or layer owns a command
/// list. <see cref="Dispose"/> pops whatever is still pushed, in reverse order, puts back the session state it changed (transform, blend,
/// anti-aliasing) and closes the session only if this canvas opened it. A session passed to <see cref="FromSession"/> stays the caller's: it
/// is never closed or disposed here, and is usable again once this canvas is disposed. A disposed canvas throws
/// <see cref="ObjectDisposedException"/> from every drawing call. A canvas must be used from one thread at a time.
/// </para>
/// </remarks>
public sealed partial class Win2dCanvas : Canvas
{
    private enum OpenKind { Clip, Transform, Blend }

    private readonly record struct Open(OpenKind Kind, CanvasActiveLayer? Layer, IDisposable? Resource, Matrix3x2 Ctm, Matrix3x2 Local, PaintBlendMode Blend, CanvasBlend SessionBlend, ClipSurface? Surface = null);

    /// <summary>A geometry clip drawn into a render target of its own (see <see cref="BeginSurfaceClip"/>), with what to put back when it is popped.</summary>
    private sealed record ClipSurface(CanvasRenderTarget Target, CanvasDrawingSession Session, CanvasDrawingSession ParentSession, CanvasRenderTarget ParentTarget, CanvasGeometry Geometry,
        (int X, int Y, int Width, int Height) Region, long TextAtStart, Matrix3x2 SavedBase);

    private long _textRunsQueued;

    private readonly Win2dRenderContext _context;
    private CanvasDrawingSession _session;
    private readonly bool _ownsSession;
    private readonly bool _isTile;
    private readonly double _ppp;
    private Matrix3x2 _deviceBase;
    private readonly Matrix3x2 _originalTransform;
    private readonly CanvasBlend _originalBlend;
    private readonly CanvasAntialiasing _originalAntialiasing;
    private readonly List<Open> _open = [];
    private readonly Stack<CanvasAntialiasing> _antialiasing = new();
    private Matrix3x2 _ctm;
    private Matrix3x2 _local = Matrix3x2.Identity;
    private PaintBlendMode _blend = PaintBlendMode.Normal;
    private bool _disposed;

    private Win2dCanvas(Win2dRenderContext context, CanvasDrawingSession session, bool ownsSession, Rect initialClip, double pixelsPerPoint,
        Matrix3x2 deviceBase, bool isTile, Matrix3x2 seed, Win2dLinkOptions? linkOptions)
        : base(context, initialClip)
    {
        _context = context;
        _session = session;
        _ownsSession = ownsSession;
        _isTile = isTile;
        _ppp = pixelsPerPoint > 0 ? pixelsPerPoint : 1.0;
        _deviceBase = deviceBase;
        _ctm = seed;
        _originalTransform = session.Transform;
        _originalBlend = session.Blend;
        _originalAntialiasing = session.Antialiasing;
        // A host's session may already scale its drawing (a DPI scale, a zoom); a pointer in its device-independent pixels is that many
        // layout units apart.
        var scale = Math.Sqrt(Math.Abs((double)deviceBase.M11 * deviceBase.M22 - (double)deviceBase.M12 * deviceBase.M21));
        Links = new Win2dLinkMap(linkOptions, scale > 1e-9 ? 1.0 / scale : 1.0);
        ApplyTransform();
    }

    /// <summary>
    /// Makes a canvas that draws into a drawing session the caller already has: the one a <c>CanvasControl</c> hands its <c>Draw</c> handler, a
    /// render target's, a swap chain's.
    /// </summary>
    /// <param name="context">where fonts and images come from; its <see cref="Win2dRenderContext.PixelsPerPoint"/> sets the canvas's</param>
    /// <param name="session">the session to draw into. It is borrowed: this canvas never closes or disposes it, and puts back the transform, blend and anti-aliasing it found when it is disposed</param>
    /// <param name="bounds">the area being drawn, in layout units (the initial clip bound that culling reads)</param>
    /// <param name="linkOptions">which links <see cref="Links"/> may open, or <see langword="null"/> for the defaults</param>
    /// <returns>the canvas; dispose it when the drawing is done</returns>
    public static Win2dCanvas FromSession(Win2dRenderContext context, CanvasDrawingSession session, Rect bounds, Win2dLinkOptions? linkOptions = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(session);
        return new Win2dCanvas(context, session, false, bounds, context.PixelsPerPoint, session.Transform, false, Matrix3x2.Identity, linkOptions);
    }

    /// <summary>For a composer: a canvas over a session it opened, which this canvas closes when it is disposed.</summary>
    internal static Win2dCanvas OverOwnedSession(Win2dRenderContext context, CanvasDrawingSession session, Rect bounds, Win2dLinkOptions? linkOptions)
    {
        context.Counts.Session(1);
        try
        {
            return new Win2dCanvas(context, session, true, bounds, context.PixelsPerPoint, Matrix3x2.Identity, false, Matrix3x2.Identity, linkOptions);
        }
        catch
        {
            context.Counts.Session(-1);
            throw;
        }
    }

    /// <summary>The links and named places painted so far; see <see cref="Win2dLinkMap"/>. It survives <see cref="Dispose"/>.</summary>
    public Win2dLinkMap Links { get; }

    // The scale from the session's device-independent pixels to device pixels.
    private double DpiScale => _session.Dpi / 96.0;

    /// <inheritdoc />
    public override double PixelsPerPoint => _ppp;

    /// <inheritdoc />
    public override bool IsOffscreenTile => _isTile;

    /// <inheritdoc />
    public override bool HasNativeLayerEffects => true;

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

    internal Win2dRenderContext Context => _context;

    internal CanvasDrawingSession Session => _session;

    private void Guard() => ObjectDisposedException.ThrowIf(_disposed, this);

    private Matrix3x2 EffectiveTransform => _local.Then(_deviceBase);

    private void ApplyTransform() => _session.Transform = EffectiveTransform;

    // ---- the push/pop stack -------------------------------------------------------------------------------

    private void Push(OpenKind kind, CanvasActiveLayer? layer = null, IDisposable? resource = null, ClipSurface? surface = null)
    {
        _open.Add(new Open(kind, layer, resource, _ctm, _local, _blend, _session.Blend, surface));
        if (layer is not null)
            _context.Counts.Layer(1);
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

    // Whether a clip made from a geometry (a rounded corner, an excluded rectangle) is open. Direct2D draws what is inside one into a layer of its own and
    // composites it when the clip is popped, so until then the pixels the clip's content has drawn are not in the target to be read or sampled.
    private bool InsideGeometryLayer
    {
        get
        {
            foreach (var open in _open)
            {
                if (open.Layer is not null && open.Resource is not null)
                    return true;
            }

            return false;
        }
    }

    private void Release(Open entry)
    {
        // The layer goes before the state is put back: the clip ends under the transform it was pushed with.
        var releaseStarted = Win2dPaintStatistics.Start();
        if (entry.Surface is { } surface)
        {
            EndSurfaceClip(surface);
            Win2dPaintStatistics.Stop(Win2dPaintStatistics.Counter.SurfaceClipEnd, releaseStarted);
            releaseStarted = Win2dPaintStatistics.Start();
        }

        if (entry.Layer is not null)
        {
            entry.Layer.Dispose();
            _context.Counts.Layer(-1);
        }

        entry.Resource?.Dispose();
        if (entry.Kind == OpenKind.Clip)
            Win2dPaintStatistics.Stop(Win2dPaintStatistics.Counter.ClipRelease, releaseStarted);
        _ctm = entry.Ctm;
        _local = entry.Local;
        _blend = entry.Blend;
        _session.Blend = entry.SessionBlend;
        ApplyTransform();
    }

    // ---- clip, transform, blend ---------------------------------------------------------------------------

    /// <inheritdoc />
    public override void PushClip(Rect rect)
    {
        Guard();
        FlushText();
        Win2dPaintStatistics.Count(Win2dPaintStatistics.Counter.RectClip);
        Push(OpenKind.Clip, _session.CreateLayer(1f, new Windows.Foundation.Rect(rect.X, rect.Y, Math.Max(rect.Width, 0), Math.Max(rect.Height, 0))));
        _clipStack.Push(rect);
    }

    /// <inheritdoc />
    public override void PushClipAroundShapes(GraphicsPath path) => PushPathClip(path, mayDrawText: false);

    /// <inheritdoc />
    public override void PushClip(GraphicsPath path) => PushPathClip(path, mayDrawText: true);

    private void PushPathClip(GraphicsPath path, bool mayDrawText)
    {
        Guard();
        FlushText();

        // No simple bounding rectangle for an arbitrary path, so the tracked clip bound stays as it was (it is only used for culling).
        var geometry = BuildGeometry(path);
        try
        {
            PushGeometryClip(geometry, mayDrawText);
            _clipStack.Push(_clipStack.Peek());
        }
        catch
        {
            geometry.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public override void PushClipExclude(Rect rect)
    {
        Guard();
        FlushText();

        // Everything except the rectangle: a huge rectangle with the excluded one cut out of it.
        const float big = 1e7f;
        using var everything = CanvasGeometry.CreateRectangle(_session, -big, -big, 2 * big, 2 * big);
        using var hole = CanvasGeometry.CreateRectangle(_session, (float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);
        var geometry = everything.CombineWith(hole, Matrix3x2.Identity, CanvasGeometryCombine.Exclude);
        try
        {
            PushGeometryClip(geometry);
            _clipStack.Push(_clipStack.Peek());
        }
        catch
        {
            geometry.Dispose();
            throw;
        }
    }

    private void PushGeometryClip(CanvasGeometry geometry, bool mayDrawText = true)
    {
        Win2dPaintStatistics.Count(Win2dPaintStatistics.Counter.GeometryClip);

        // A clip that only has shapes and images drawn inside it needs no surface of its own to blend text against: a Direct2D layer does.
        if (mayDrawText && BeginSurfaceClip(geometry) is { } surface)
            Push(OpenKind.Clip, null, geometry, surface);
        else
            Push(OpenKind.Clip, _session.CreateLayer(1f, geometry), geometry);
    }

    /// <summary>
    /// Starts a geometry clip that draws into a render target of its own instead of a Direct2D layer, so ClearType text inside it has the pixels behind it to
    /// blend against (a layer's content is not in the target until the layer is popped). The target starts as a copy of the one being drawn to; at the pop it
    /// is composited back through the geometry, which leaves everything outside it as it was. Null, with nothing changed, when this canvas does not blend
    /// text on the GPU, or when no text could use it (the clip then costs what it always did).
    /// </summary>
    private ClipSurface? BeginSurfaceClip(CanvasGeometry geometry)
    {
        if (_lcdTarget is not { } parentTarget || _context.TextRendering is Win2dTextRendering.Outline or Win2dTextRendering.Grayscale
            || (_context.TextRendering == Win2dTextRendering.Auto && !(_clearType ??= Win2dSystemText.ClearTypeEnabled)))
            return null;

        var surfaceStarted = Win2dPaintStatistics.Start();
        var parentSession = _session;
        parentSession.Flush();
        var size = parentTarget.SizeInPixels;

        // Only what the geometry can show is copied (and composited back): text blended inside the clip reads the pixels behind it, and nothing outside the
        // geometry survives the composite. The target is that big and no bigger (a page of rounded cards opens hundreds, and one the size of the whole view each
        // took most of the memory the page used); what is drawn into it is moved so that the region starts at its origin.
        var region = SurfaceRegion(geometry, parentSession.Transform, parentSession.Dpi, (int)size.Width, (int)size.Height);
        var target = _context.SurfacePool.Take(parentSession.Device, parentTarget.Dpi, parentTarget.Format, Math.Max(1, region.Width), Math.Max(1, region.Height),
            (int)size.Width, (int)size.Height);
        if (region.Width > 0 && region.Height > 0)
            target.CopyPixelsFromBitmap(parentTarget, 0, 0, region.X, region.Y, region.Width, region.Height);

        Win2dPaintStatistics.Stop(Win2dPaintStatistics.Counter.SurfaceClip, surfaceStarted);

        var unit = 96f / parentTarget.Dpi;
        var shift = Matrix3x2.CreateTranslation(-region.X * unit, -region.Y * unit);
        var savedBase = _deviceBase;
        _deviceBase = savedBase * shift;

        var session = target.CreateDrawingSession();
        _context.Counts.Session(1);
        session.Transform = parentSession.Transform * shift;
        session.Blend = parentSession.Blend;
        session.Antialiasing = parentSession.Antialiasing;
        session.TextAntialiasing = parentSession.TextAntialiasing;

        _session = session;
        _lcdTarget = target;
        return new ClipSurface(target, session, parentSession, parentTarget, geometry, region, _textRunsQueued, savedBase);
    }

    // The pixels of the target (x, y, width, height) that a geometry drawn under a transform can touch, a little over, or all of it when the bounds cannot be told.
    private static (int X, int Y, int Width, int Height) SurfaceRegion(CanvasGeometry geometry, Matrix3x2 transform, float dpi, int width, int height)
    {
        var bounds = geometry.ComputeBounds(transform);
        var scale = dpi / 96.0;
        if (!double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) || !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height))
            return (0, 0, width, height);

        var left = (int)Math.Clamp(Math.Floor(bounds.Left * scale) - 2, 0, width);
        var top = (int)Math.Clamp(Math.Floor(bounds.Top * scale) - 2, 0, height);
        var right = (int)Math.Clamp(Math.Ceiling(bounds.Right * scale) + 2, 0, width);
        var bottom = (int)Math.Clamp(Math.Ceiling(bounds.Bottom * scale) + 2, 0, height);
        return (left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    // Composites the clip's target onto the one it was copied from, through the geometry, and puts the parent session back.
    private void EndSurfaceClip(ClipSurface surface)
    {
        Win2dPaintStatistics.Count(_textRunsQueued != surface.TextAtStart ? Win2dPaintStatistics.Counter.SurfaceClipWithText : Win2dPaintStatistics.Counter.SurfaceClipEmpty);
        try
        {
            surface.Session.Dispose();
            _context.Counts.Session(-1);
        }
        finally
        {
            _session = surface.ParentSession;
            _lcdTarget = surface.ParentTarget;
            _deviceBase = surface.SavedBase;
            try
            {
                // The geometry is in the user space the clip was pushed under, which is the parent session's transform still.
                if (surface.Region.Width > 0 && surface.Region.Height > 0)
                {
                    using var layer = _session.CreateLayer(1f, surface.Geometry);
                    var transform = _session.Transform;
                    var blend = _session.Blend;
                    try
                    {
                        _session.Transform = Matrix3x2.Identity;
                        _session.Blend = CanvasBlend.Copy;

                        // The target is at the session's resolution, so a pixel is 96 / dpi device-independent units.
                        var unit = 96f / surface.Target.Dpi;
                        var part = new Windows.Foundation.Rect(0, 0, surface.Region.Width * unit, surface.Region.Height * unit);
                        _session.DrawImage(surface.Target, surface.Region.X * unit, surface.Region.Y * unit, part);
                    }
                    finally
                    {
                        _session.Blend = blend;
                        _session.Transform = transform;
                    }
                }
            }
            finally
            {
                _context.SurfacePool.Return(surface.Target);
            }
        }
    }

    /// <inheritdoc />
    public override void PopClip()
    {
        FlushText();
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
        _local = matrix.Then(_local);
        ApplyTransform();
    }

    /// <inheritdoc />
    public override void PopTransform() => Pop(OpenKind.Transform);

    /// <inheritdoc />
    public override void PushBlendMode(PaintBlendMode mode)
    {
        Guard();
        FlushText();
        Push(OpenKind.Blend);
        _blend = mode;
        _session.Blend = SessionBlendFor(mode);
    }

    /// <inheritdoc />
    public override void PopBlendMode()
    {
        FlushText();
        Pop(OpenKind.Blend);
    }

    /// <summary>
    /// The blend a session can apply while it draws. A session blends only by a few simple rules (add, minimum); the other blend modes need the pixels already
    /// underneath, which a session cannot read, so they draw as <see cref="PaintBlendMode.Normal"/>. A layer or a tile composited through
    /// <see cref="DrawImageBlendedOver"/> has both sides in hand and blends exactly.
    /// </summary>
    private static CanvasBlend SessionBlendFor(PaintBlendMode mode) => mode switch
    {
        PaintBlendMode.Plus => CanvasBlend.Add,
        PaintBlendMode.Darken => CanvasBlend.Min,
        _ => CanvasBlend.SourceOver,
    };

    /// <inheritdoc />
    public override object SetAntiAliasSmoothingMode()
    {
        Guard();
        var previous = _session.Antialiasing;
        _session.Antialiasing = CanvasAntialiasing.Antialiased;
        return previous;
    }

    /// <inheritdoc />
    public override void ReturnPreviousSmoothingMode(object? prevMode)
    {
        Guard();
        if (prevMode is CanvasAntialiasing previous)
            _session.Antialiasing = previous;
    }

    /// <inheritdoc />
    public override void PushAntiAlias(bool enabled)
    {
        Guard();
        _antialiasing.Push(_session.Antialiasing);
        _session.Antialiasing = enabled && _context.RasterAntiAliasing ? CanvasAntialiasing.Antialiased : CanvasAntialiasing.Aliased;
    }

    /// <inheritdoc />
    public override void PopAntiAlias()
    {
        Guard();
        if (_antialiasing.Count > 0)
            _session.Antialiasing = _antialiasing.Pop();
    }

    /// <inheritdoc />
    public override GraphicsPath GetGraphicsPath() => new Win2dGraphicsPath();

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
    /// Pops everything still pushed, puts back the session's transform, blend and anti-aliasing, and closes the session if this canvas opened it.
    /// Safe to call more than once. A session passed to <see cref="FromSession"/> is left open and usable.
    /// </summary>
    public override void Dispose()
    {
        if (_disposed)
            return;

        Exception? failure = null;
        try
        {
            FlushText();
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        _disposed = true;
        try
        {
            // Release in reverse, each under the state it was pushed with, so a clip layer ends before the transform beneath it changes. One
            // that throws (a layer of a lost device can) must not leave the others undisposed: the first failure is rethrown afterwards.
            for (var i = _open.Count - 1; i >= 0; i--)
            {
                try
                {
                    Release(_open[i]);
                }
                catch (Exception ex)
                {
                    failure ??= ex;
                }
            }

            _open.Clear();
        }
        finally
        {
            _lcdEffect?.Dispose();
            _lcdEffect = null;
            AbandonLayers();
            ReleaseConverted();
            ReleaseBitmapGlyphs();
            ReleaseNativeTarget();
            _session.Transform = _originalTransform;
            _session.Blend = _originalBlend;
            _session.Antialiasing = _originalAntialiasing;
            if (_ownsSession)
            {
                _session.Dispose();
                _context.Counts.Session(-1);
            }

            if (_isTile)
                _context.Counts.Tile(-1);
            else
            {
                Win2dPaintStatistics.EndPaint();
                Win2dPaintStatistics.NoteMemory(_context);
            }
        }

        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    // ---- geometry -----------------------------------------------------------------------------------------

    /// <summary>The Win2D geometry for <paramref name="path"/>, in layout units. The caller disposes it.</summary>
    private CanvasGeometry BuildGeometry(GraphicsPath path)
    {
        using var builder = new CanvasPathBuilder(_session);
        builder.SetFilledRegionDetermination(path.FillMode == FillMode.EvenOdd
            ? CanvasFilledRegionDetermination.Alternate
            : CanvasFilledRegionDetermination.Winding);

        foreach (var contour in path.GetCurveContours())
        {
            if (contour.Commands.Count == 0)
                continue;

            builder.BeginFigure(Point(contour.Start), CanvasFigureFill.Default);
            foreach (var command in contour.Commands)
            {
                if (command.Kind == PathCommandKind.Line)
                    builder.AddLine(Point(command.End));
                else
                    builder.AddCubicBezier(Point(command.Control1), Point(command.Control2), Point(command.End));
            }

            builder.EndFigure(contour.Closed ? CanvasFigureLoop.Closed : CanvasFigureLoop.Open);
        }

        return CanvasGeometry.CreatePath(builder);
    }

    // A path's coordinates are in points; the canvas works in layout units.
    private Vector2 Point(PaintPoint p) => new((float)(p.X * _ppp), (float)(p.Y * _ppp));

    // ---- shapes -------------------------------------------------------------------------------------------

    /// <inheritdoc />
    public override void DrawLine(Pen pen, double x1, double y1, double x2, double y2)
    {
        Guard();
        var lineReach = Math.Max(pen.Width * _ppp, 1) * 2;
        NoteDrawn(Math.Min(x1, x2) - lineReach, Math.Min(y1, y2) - lineReach, Math.Max(x1, x2) + lineReach, Math.Max(y1, y2) + lineReach);
        Stroke(pen,
            (brush, width, style) => _session.DrawLine((float)x1, (float)y1, (float)x2, (float)y2, brush, width, style),
            () =>
            {
                using var path = new CanvasPathBuilder(_session);
                path.BeginFigure((float)x1, (float)y1);
                path.AddLine((float)x2, (float)y2);
                path.EndFigure(CanvasFigureLoop.Open);
                return CanvasGeometry.CreatePath(path);
            });
    }

    /// <inheritdoc />
    public override void DrawRectangle(Pen pen, double x, double y, double width, double height)
    {
        Guard();
        var strokeReach = Math.Max(pen.Width * _ppp, 1) * 2;
        NoteStrokeBands(x, y, x + width, y + height, strokeReach);
        Stroke(pen,
            (brush, w, style) => _session.DrawRectangle((float)x, (float)y, (float)width, (float)height, brush, w, style),
            () => CanvasGeometry.CreateRectangle(_session, (float)x, (float)y, (float)width, (float)height));
    }

    /// <inheritdoc />
    public override void DrawRectangle(Brush brush, double x, double y, double width, double height)
    {
        Guard();
        NoteDrawn(x, y, x + width, y + height);

        Fill(brush,
            b => _session.FillRectangle((float)x, (float)y, (float)width, (float)height, b),
            () => CanvasGeometry.CreateRectangle(_session, (float)x, (float)y, (float)width, (float)height));
    }

    /// <inheritdoc />
    public override void DrawPath(Pen pen, GraphicsPath path)
    {
        Guard();
        using var geometry = BuildGeometry(path);
        var closed = path.GetCurveContours().All(contour => contour.Closed);
        NoteGeometry(geometry, Math.Max(pen.Width * _ppp, 1) * 2, closed);
        Stroke(pen, (brush, width, style) => _session.DrawGeometry(geometry, brush, width, style), () => BuildGeometry(path));
    }

    /// <inheritdoc />
    public override void DrawPath(Brush brush, GraphicsPath path)
    {
        Guard();
        using var geometry = BuildGeometry(path);
        NoteGeometry(geometry, 1);
        Fill(brush, b => _session.FillGeometry(geometry, b), () => BuildGeometry(path));
    }

    /// <inheritdoc />
    public override void DrawPolygon(Brush brush, PaintPoint[] points)
    {
        Guard();
        if (points.Length < 3)
            return;

        // A border side or a rule is a four-cornered polygon with horizontal and vertical edges: a rectangle, which needs no geometry to fill.
        if (points.Length == 4 && TryAxisAlignedRectangle(points, out var rectangle))
        {
            DrawRectangle(brush, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
            return;
        }

        var vertices = new Vector2[points.Length];
        for (var i = 0; i < points.Length; i++)
            vertices[i] = new Vector2((float)points[i].X, (float)points[i].Y);

        using var geometry = CanvasGeometry.CreatePolygon(_session, vertices);
        NoteGeometry(geometry, 1);
        Fill(brush, b => _session.FillGeometry(geometry, b), () => CanvasGeometry.CreatePolygon(_session, vertices));
    }

    // A closed outline touches the band along its edge, not what it encloses: a card with a border has plain background inside it.
    private static bool TryAxisAlignedRectangle(PaintPoint[] p, out Rect rectangle)
    {
        rectangle = default;
        var horizontalFirst = p[0].Y == p[1].Y && p[1].X == p[2].X && p[2].Y == p[3].Y && p[3].X == p[0].X;
        var verticalFirst = p[0].X == p[1].X && p[1].Y == p[2].Y && p[2].X == p[3].X && p[3].Y == p[0].Y;
        if (!horizontalFirst && !verticalFirst)
            return false;

        var left = Math.Min(Math.Min(p[0].X, p[1].X), Math.Min(p[2].X, p[3].X));
        var right = Math.Max(Math.Max(p[0].X, p[1].X), Math.Max(p[2].X, p[3].X));
        var top = Math.Min(Math.Min(p[0].Y, p[1].Y), Math.Min(p[2].Y, p[3].Y));
        var bottom = Math.Max(Math.Max(p[0].Y, p[1].Y), Math.Max(p[2].Y, p[3].Y));
        if (!(right > left) || !(bottom > top))
            return false;

        rectangle = new Rect(left, top, right - left, bottom - top);
        return true;
    }

    // A shape's reach: its bounds, and a stroke's width or a blur more.
    private void NoteStrokeBands(double left, double top, double right, double bottom, double reach)
    {
        if (right - left <= 4 * reach || bottom - top <= 4 * reach)
        {
            NoteDrawn(left - reach, top - reach, right + reach, bottom + reach);
            return;
        }

        NoteDrawn(left - reach, top - reach, right + reach, top + reach);
        NoteDrawn(left - reach, bottom - reach, right + reach, bottom + reach);
        NoteDrawn(left - reach, top - reach, left + reach, bottom + reach);
        NoteDrawn(right - reach, top - reach, right + reach, bottom + reach);
    }

    private void NoteGeometry(CanvasGeometry geometry, double reach, bool outlineOnly = false)
    {
        var bounds = geometry.ComputeBounds();
        if (bounds.Width <= 0 && bounds.Height <= 0)
        {
            FlushText();
            return;
        }

        if (outlineOnly)
            NoteStrokeBands(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom, reach);
        else
            NoteDrawn(bounds.Left - reach, bounds.Top - reach, bounds.Right + reach, bounds.Bottom + reach);
    }

    // ---- filling and stroking -----------------------------------------------------------------------------

    // `shape` makes a new geometry (the caller of `shape` disposes it) for a conic gradient to be clipped to; for the other brushes `paint`
    // draws directly.
    private void Fill(Brush brush, Action<ICanvasBrush> paint, Func<CanvasGeometry> shape)
    {
        Win2dTileOwner? hatchTile = null;
        try
        {
            if (brush is HatchBrush hatch)
            {
                if (hatch.ToTileBrush(this) is not { } tileBrush)
                    return;

                // The tile a hatch makes is a fresh command list nobody else holds; it is released with the draw.
                hatchTile = new Win2dTileOwner(tileBrush.Tile);
                brush = tileBrush;
            }

            if (brush is ConicGradientBrush conic)
            {
                using var outline = shape();
                FillConic(conic, outline);
                return;
            }

            using var lease = MakeBrush(brush);
            if (lease.Brush is { } resolved)
                paint(resolved);
        }
        finally
        {
            hatchTile?.Dispose();
        }
    }

    private void Stroke(Pen pen, Action<ICanvasBrush, float, CanvasStrokeStyle> draw, Func<CanvasGeometry> shape)
    {
        if (pen.Paint is not { } paint)
            return;

        Win2dTileOwner? hatchTile = null;
        try
        {
            if (paint is HatchBrush hatch)
            {
                if (hatch.ToTileBrush(this) is not { } tileBrush)
                    return;

                hatchTile = new Win2dTileOwner(tileBrush.Tile);
                paint = tileBrush;
            }

            // A pen's width and dash lengths are in points, like a path's coordinates.
            var width = pen.Width > 0 ? (float)(pen.Width * _ppp) : HairlineWidth();
            using var style = StrokeStyleFor(pen, width);

            if (paint is ConicGradientBrush conic)
            {
                // A conic gradient has no stroke form: paint the wedges through the stroked outline.
                using var outline = shape();
                using var stroked = outline.Stroke(width, style);
                FillConic(conic, stroked);
                return;
            }

            using var lease = MakeBrush(paint);
            if (lease.Brush is { } resolved)
                draw(resolved, width, style);
        }
        finally
        {
            hatchTile?.Dispose();
        }
    }

    // A zero width draws the thinnest line the device can, which Win2D has no name for: one device pixel.
    private float HairlineWidth()
    {
        var m = EffectiveTransform;
        var scale = Math.Sqrt(Math.Abs((double)m.M11 * m.M22 - (double)m.M12 * m.M21)) * _session.Dpi / 96.0;
        return scale > 1e-6 ? (float)(1.0 / scale) : 1f;
    }

    private CanvasStrokeStyle StrokeStyleFor(Pen pen, float width)
    {
        var style = new CanvasStrokeStyle
        {
            StartCap = pen.LineCap switch { LineCap.Round => CanvasCapStyle.Round, LineCap.Square => CanvasCapStyle.Square, _ => CanvasCapStyle.Flat },
            LineJoin = pen.LineJoin switch { LineJoin.Round => CanvasLineJoin.Round, LineJoin.Bevel => CanvasLineJoin.Bevel, _ => CanvasLineJoin.MiterOrBevel },

            // An unset limit (0) is 10 on the PDF and raster canvases.
            MiterLimit = (float)(pen.MiterLimit >= 1 ? pen.MiterLimit : 10),
        };
        style.EndCap = style.StartCap;
        // Each dash carries the pen cap, as on the PDF canvas.
        style.DashCap = style.StartCap;

        if (ResolveDashes(pen) is { } dashes)
        {
            // A Win2D dash is measured in stroke widths, not in the units of the drawing.
            var pattern = new float[dashes.Length];
            for (var i = 0; i < pattern.Length; i++)
                pattern[i] = (float)(dashes[i] * _ppp / width);

            style.CustomDashStyle = pattern;
            if (pen.DashStyle == DashStyle.Custom && pen.DashOffset != 0)
                style.DashOffset = (float)(pen.DashOffset * _ppp / width);
        }

        return style;
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
            case DashStyle.Dot: return [w];
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
internal readonly struct Win2dTileOwner(PeachDrawing.Core.Image image) : IDisposable
{
    public void Dispose() => image.Dispose();
}
