using CoreGraphics;
using ObjCRuntime;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace PeachDrawing.CoreGraphics;

/// <summary>
/// A <see cref="Canvas"/> that draws with CoreGraphics (Quartz 2D): shapes, gradients, clips, layers, text and images go to a <see cref="CGContext"/>
/// the caller already has (<see cref="FromContext"/>) - a view's, a layer's, a bitmap's - or to the bitmap a <see cref="CoreGraphicsComposer"/> makes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Units.</b> The context's user space is the canvas's layout units: rectangles, lines, polygons, image rectangles, brush geometry and text
/// positions are used as given. What is in points (a <see cref="GraphicsPath"/>'s coordinates, a pen's width and dashes) is multiplied by
/// <see cref="PixelsPerPoint"/> first. A matrix already set on the host's context (a DPI scale, a zoom) is kept: this canvas draws under it. The
/// canvas draws top-down (y grows downwards); <see cref="FromContext"/> says whether the context's own origin is at the bottom left (CoreGraphics'
/// default, and a bitmap context's) or already the top left (UIKit's, and a flipped view's), and flips it when it is not.
/// </para>
/// <para>
/// <b>Lifetime.</b> Everything native this canvas makes it releases: a clip, transform or blend pushed holds a graphics-state level of the
/// <see cref="CGContext"/> until the matching pop; a layer or tile owns a bitmap context. <see cref="Dispose"/> pops whatever is still pushed, in
/// reverse order, and restores the <see cref="CGContext"/> to the state it found. The context passed to <see cref="FromContext"/> stays the
/// caller's: it is never released or flushed here, and is usable again once this canvas is disposed. A disposed canvas throws
/// <see cref="ObjectDisposedException"/> from every drawing call. A canvas must be used from one thread at a time.
/// </para>
/// </remarks>
public sealed partial class CoreGraphicsCanvas : Canvas
{
    private enum OpenKind { Clip, Transform, Blend }

    private readonly record struct Open(OpenKind Kind, int Depth, Matrix3x2 Ctm, PaintBlendMode Blend, bool Smooth);

    private readonly CoreGraphicsRenderContext _context;
    private readonly CGContext _cg;
    private readonly bool _isOffscreen;
    private readonly bool _countsAsTile;
    private readonly double _ppp;
    private readonly List<Open> _open = [];
    private readonly Stack<bool> _antialiasing = new();
    private int _depth;
    private Matrix3x2 _ctm;
    private PaintBlendMode _blend = PaintBlendMode.Normal;
    private bool _smooth = true;
    private bool _disposed;

    private CoreGraphicsCanvas(CoreGraphicsRenderContext context, CGContext cg, Rect initialClip, double pixelsPerPoint, bool isOffscreen,
        bool countsAsTile, Matrix3x2 seed, CoreGraphicsLinkOptions? linkOptions, bool smooth, bool flipAboutBounds)
        : base(context, initialClip)
    {
        _context = context;
        _cg = cg;
        _isOffscreen = isOffscreen;
        _countsAsTile = countsAsTile;
        _ppp = pixelsPerPoint > 0 ? pixelsPerPoint : 1.0;
        _ctm = seed;
        _smooth = smooth;

        // A host's context may already scale its drawing (a DPI scale, a zoom); a pointer in its own coordinates is that many layout units apart.
        var m = cg.GetCTM();
        var scale = Math.Sqrt(Math.Abs((double)m.A * m.D - (double)m.B * m.C));
        Links = new CoreGraphicsLinkMap(linkOptions, scale > 1e-9 ? 1.0 / scale : 1.0);

        // Everything this canvas does sits above this level, so disposing it can put the host's context back exactly as it was.
        Save();
        if (flipAboutBounds)
        {
            // y grows downwards for the canvas: a point at layout y lands at (top + bottom - y) in the context's y-up space.
            cg.ConcatCTM(new CGAffineTransform(1, 0, 0, -1, 0, (nfloat)(initialClip.Y + initialClip.Y + initialClip.Height)));
        }

        cg.SetShouldAntialias(smooth);
        cg.SetBlendMode(CGBlendMode.Normal);
        cg.InterpolationQuality = CGInterpolationQuality.High;
    }

    /// <summary>
    /// Makes a canvas that draws into a <see cref="CGContext"/> the caller already has: the one a view hands its draw handler, a layer's, a
    /// bitmap's.
    /// </summary>
    /// <param name="context">where fonts and images come from; its <see cref="CoreGraphicsRenderContext.PixelsPerPoint"/> sets the canvas's</param>
    /// <param name="graphicsContext">the context to draw into. It is borrowed: this canvas never releases or flushes it, and restores its graphics state (matrix, clip, colours) when it is disposed</param>
    /// <param name="bounds">the area being drawn, in layout units (the initial clip bound that culling reads, and the extent the flip is about)</param>
    /// <param name="linkOptions">which links <see cref="Links"/> may open, or <see langword="null"/> for the defaults</param>
    /// <param name="originAtBottomLeft">
    /// <see langword="true"/> (the default) when the context's origin is at the bottom left, as CoreGraphics', an AppKit view's and a bitmap context's are:
    /// the canvas flips it so that y grows downwards. <see langword="false"/> when it is already the top left, as UIKit's contexts and a flipped
    /// AppKit view's are.
    /// </param>
    /// <returns>the canvas; dispose it when the drawing is done</returns>
    public static CoreGraphicsCanvas FromContext(CoreGraphicsRenderContext context, CGContext graphicsContext, Rect bounds,
        CoreGraphicsLinkOptions? linkOptions = null, bool originAtBottomLeft = true)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(graphicsContext);
        return new CoreGraphicsCanvas(context, graphicsContext, bounds, context.PixelsPerPoint, false, false, Matrix3x2.Identity, linkOptions, true, originAtBottomLeft);
    }

    /// <summary>The links and named places painted so far; see <see cref="CoreGraphicsLinkMap"/>. It survives <see cref="Dispose"/>.</summary>
    public CoreGraphicsLinkMap Links { get; }

    /// <inheritdoc />
    public override double PixelsPerPoint => _ppp;

    /// <inheritdoc />
    public override bool IsOffscreenTile => _isOffscreen;

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

    internal CoreGraphicsRenderContext Context => _context;

    internal CGContext Surface => _cg;

    private void Guard() => ObjectDisposedException.ThrowIf(_disposed, this);

    private void Save()
    {
        _cg.SaveState();
        _depth++;
    }

    private void RestoreTo(int depth)
    {
        while (_depth > depth)
        {
            _cg.RestoreState();
            _depth--;
        }
    }

    private static CGAffineTransform ToTransform(Matrix3x2 m) => new(m.M11, m.M12, m.M21, m.M22, m.M31, m.M32);

    /// <summary>The device pixels one layout unit currently covers: the context's matrix as drawn on (host matrix, flip and pushed transforms).</summary>
    internal double DeviceScale
    {
        get
        {
            var m = _cg.GetCTM();
            var s = Math.Sqrt(Math.Abs((double)m.A * m.D - (double)m.B * m.C));
            return s > 1e-6 ? s : 1.0;
        }
    }

    // ---- the push/pop stack -------------------------------------------------------------------------------

    private void Push(OpenKind kind)
    {
        Save();
        _open.Add(new Open(kind, _depth - 1, _ctm, _blend, _smooth));
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
        RestoreTo(entry.Depth);
        _ctm = entry.Ctm;
        _blend = entry.Blend;

        // Anti-aliasing is part of the graphics state the restore just rolled back; the field follows it.
        _smooth = entry.Smooth;
    }

    // ---- clip, transform, blend ---------------------------------------------------------------------------

    private static CGRect ToRect(Rect rect) => new(rect.X, rect.Y, Math.Max(rect.Width, 0), Math.Max(rect.Height, 0));

    /// <inheritdoc />
    public override void PushClip(Rect rect)
    {
        Guard();
        Push(OpenKind.Clip);
        _cg.ClipToRect(ToRect(rect));
        _clipStack.Push(rect);
    }

    /// <inheritdoc />
    public override void PushClip(GraphicsPath path)
    {
        Guard();

        // No simple bounding rectangle for an arbitrary path, so the tracked clip bound stays as it was (it is only used for culling).
        using var shape = BuildPath(path);
        Push(OpenKind.Clip);
        _cg.AddPath(shape);
        if (path.FillMode == FillMode.EvenOdd)
            _cg.EOClip();
        else
            _cg.Clip();

        _clipStack.Push(_clipStack.Peek());
    }

    /// <inheritdoc />
    public override void PushClipExclude(Rect rect)
    {
        Guard();
        Push(OpenKind.Clip);

        // An even-odd clip of a rectangle far larger than anything drawn, with the excluded one inside it, leaves everything but the hole.
        const double Huge = 1e7;
        _cg.AddRect(new CGRect(-Huge, -Huge, 2 * Huge, 2 * Huge));
        _cg.AddRect(ToRect(rect));
        _cg.EOClip();
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
        _cg.ConcatCTM(ToTransform(matrix));
    }

    /// <inheritdoc />
    public override void PopTransform() => Pop(OpenKind.Transform);

    /// <inheritdoc />
    public override void PushBlendMode(PaintBlendMode mode)
    {
        Guard();
        Push(OpenKind.Blend);
        _blend = mode;
        _cg.SetBlendMode(BlendModeFor(mode));
    }

    /// <inheritdoc />
    public override void PopBlendMode() => Pop(OpenKind.Blend);

    /// <summary>Every <see cref="PaintBlendMode"/> has a CoreGraphics blend mode of its own, so a pushed blend applies to each draw exactly.</summary>
    internal static CGBlendMode BlendModeFor(PaintBlendMode mode) => mode switch
    {
        PaintBlendMode.Multiply => CGBlendMode.Multiply,
        PaintBlendMode.Screen => CGBlendMode.Screen,
        PaintBlendMode.Overlay => CGBlendMode.Overlay,
        PaintBlendMode.Darken => CGBlendMode.Darken,
        PaintBlendMode.Lighten => CGBlendMode.Lighten,
        PaintBlendMode.ColorDodge => CGBlendMode.ColorDodge,
        PaintBlendMode.ColorBurn => CGBlendMode.ColorBurn,
        PaintBlendMode.HardLight => CGBlendMode.HardLight,
        PaintBlendMode.SoftLight => CGBlendMode.SoftLight,
        PaintBlendMode.Difference => CGBlendMode.Difference,
        PaintBlendMode.Exclusion => CGBlendMode.Exclusion,
        PaintBlendMode.Hue => CGBlendMode.Hue,
        PaintBlendMode.Saturation => CGBlendMode.Saturation,
        PaintBlendMode.Color => CGBlendMode.Color,
        PaintBlendMode.Luminosity => CGBlendMode.Luminosity,
        PaintBlendMode.Plus => CGBlendMode.PlusLighter,
        _ => CGBlendMode.Normal,
    };

    /// <inheritdoc />
    public override object SetAntiAliasSmoothingMode()
    {
        Guard();
        var previous = _smooth;
        SetSmooth(true);
        return previous;
    }

    /// <inheritdoc />
    public override void ReturnPreviousSmoothingMode(object? prevMode)
    {
        Guard();
        if (prevMode is bool previous)
            SetSmooth(previous);
    }

    /// <inheritdoc />
    public override void PushAntiAlias(bool enabled)
    {
        Guard();
        _antialiasing.Push(_smooth);
        SetSmooth(enabled && _context.RasterAntiAliasing);
    }

    /// <inheritdoc />
    public override void PopAntiAlias()
    {
        Guard();
        if (_antialiasing.Count > 0)
            SetSmooth(_antialiasing.Pop());
    }

    private void SetSmooth(bool smooth)
    {
        _smooth = smooth;
        _cg.SetShouldAntialias(smooth);
    }

    /// <inheritdoc />
    public override GraphicsPath GetGraphicsPath() => new CoreGraphicsGraphicsPath();

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
    /// Pops everything still pushed and puts the <see cref="CGContext"/> back to the graphics state it was in. Safe to call more than once. A
    /// context passed to <see cref="FromContext"/> is left open and usable.
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
            _open.Clear();
        }
        finally
        {
            ReleaseConverted();
            ReleaseBitmapGlyphs();
            RestoreTo(0);

            if (_countsAsTile)
                _context.Counts.Tile(-1);
        }
    }

    // ---- geometry -----------------------------------------------------------------------------------------

    /// <summary>The CoreGraphics path for <paramref name="path"/>, in layout units. The caller disposes it.</summary>
    private CGPath BuildPath(GraphicsPath path)
    {
        var result = new CGPath();
        foreach (var contour in path.GetCurveContours())
        {
            if (contour.Commands.Count == 0)
                continue;

            var start = Point(contour.Start);
            result.MoveToPoint(start.X, start.Y);
            foreach (var command in contour.Commands)
            {
                if (command.Kind == PathCommandKind.Line)
                {
                    var end = Point(command.End);
                    result.AddLineToPoint(end.X, end.Y);
                }
                else
                {
                    var c1 = Point(command.Control1);
                    var c2 = Point(command.Control2);
                    var end = Point(command.End);
                    result.AddCurveToPoint(c1.X, c1.Y, c2.X, c2.Y, end.X, end.Y);
                }
            }

            if (contour.Closed)
                result.CloseSubpath();
        }

        return result;
    }

    // A path's coordinates are in points; the canvas works in layout units.
    private CGPoint Point(PaintPoint p) => new(p.X * _ppp, p.Y * _ppp);

    private static CGPathDrawingMode FillModeFor(GraphicsPath path) =>
        path.FillMode == FillMode.EvenOdd ? CGPathDrawingMode.EOFill : CGPathDrawingMode.Fill;

    // ---- shapes -------------------------------------------------------------------------------------------

    /// <inheritdoc />
    public override void DrawLine(Pen pen, double x1, double y1, double x2, double y2)
    {
        Guard();
        Stroke(pen, () =>
        {
            _cg.MoveTo((nfloat)x1, (nfloat)y1);
            _cg.AddLineToPoint((nfloat)x2, (nfloat)y2);
        });
    }

    /// <inheritdoc />
    public override void DrawRectangle(Pen pen, double x, double y, double width, double height)
    {
        Guard();
        Stroke(pen, () => _cg.AddRect(new CGRect(x, y, width, height)));
    }

    /// <inheritdoc />
    public override void DrawRectangle(Brush brush, double x, double y, double width, double height)
    {
        Guard();
        Fill(brush, () => _cg.AddRect(new CGRect(x, y, width, height)), false);
    }

    /// <inheritdoc />
    public override void DrawPath(Pen pen, GraphicsPath path)
    {
        Guard();
        using var shape = BuildPath(path);
        Stroke(pen, () => _cg.AddPath(shape));
    }

    /// <inheritdoc />
    public override void DrawPath(Brush brush, GraphicsPath path)
    {
        Guard();
        using var shape = BuildPath(path);
        Fill(brush, () => _cg.AddPath(shape), path.FillMode == FillMode.EvenOdd);
    }

    /// <inheritdoc />
    public override void DrawPolygon(Brush brush, PaintPoint[] points)
    {
        Guard();
        if (points.Length < 3)
            return;

        Fill(brush, () =>
        {
            _cg.MoveTo((nfloat)points[0].X, (nfloat)points[0].Y);
            for (var i = 1; i < points.Length; i++)
                _cg.AddLineToPoint((nfloat)points[i].X, (nfloat)points[i].Y);

            _cg.ClosePath();
        }, false);
    }
}
