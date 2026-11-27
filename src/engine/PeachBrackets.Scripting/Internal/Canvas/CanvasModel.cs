using PeachBrackets.ScriptHosting;
using PeachDrawing.Core;
using PeachDrawing.Text.Shaping;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace PeachBrackets.Scripting.Internal.Canvas;

/// <summary>A path of straight lines and cubic curves, in the space it was built in.</summary>
internal sealed class PathData
{
    public enum Kind { Move, Line, Curve, Close }

    public readonly record struct Segment(Kind Type, double X1, double Y1, double X2, double Y2, double X3, double Y3);

    public List<Segment> Segments { get; } = [];

    // The current point and where the subpath began, in the space the path was built in.
    public double CurrentX { get; private set; }

    public double CurrentY { get; private set; }

    public double StartX { get; private set; }

    public double StartY { get; private set; }

    public bool HasPoint { get; private set; }

    public PathData Clone()
    {
        var copy = new PathData { CurrentX = CurrentX, CurrentY = CurrentY, StartX = StartX, StartY = StartY, HasPoint = HasPoint };
        copy.Segments.AddRange(Segments);
        return copy;
    }

    public void MoveTo(double x, double y)
    {
        Segments.Add(new Segment(Kind.Move, x, y, 0, 0, 0, 0));
        (CurrentX, CurrentY, StartX, StartY, HasPoint) = (x, y, x, y, true);
    }

    public void LineTo(double x, double y)
    {
        if (!HasPoint)
        {
            MoveTo(x, y);
            return;
        }

        Segments.Add(new Segment(Kind.Line, x, y, 0, 0, 0, 0));
        (CurrentX, CurrentY) = (x, y);
    }

    public void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
    {
        if (!HasPoint)
            MoveTo(x1, y1);

        Segments.Add(new Segment(Kind.Curve, x1, y1, x2, y2, x3, y3));
        (CurrentX, CurrentY) = (x3, y3);
    }

    public void Close()
    {
        if (!HasPoint)
            return;

        Segments.Add(new Segment(Kind.Close, 0, 0, 0, 0, 0, 0));
        (CurrentX, CurrentY) = (StartX, StartY);
    }

    /// <summary>
    /// The path as it is stroked: zero-length line and curve pieces are removed, and a subpath left with nothing to draw (a lone moveTo, or a point closed on itself) is dropped, so
    /// it leaves no dot however round its caps are.
    /// </summary>
    public PathData PrunedForStroke()
    {
        var result = new PathData();
        var pending = new List<Segment>();
        (double X, double Y) current = default, start = default;
        var drawn = false;

        void Flush()
        {
            if (drawn)
            {
                foreach (var s in pending)
                {
                    switch (s.Type)
                    {
                        case Kind.Move: result.MoveTo(s.X1, s.Y1); break;
                        case Kind.Line: result.LineTo(s.X1, s.Y1); break;
                        case Kind.Curve: result.CurveTo(s.X1, s.Y1, s.X2, s.Y2, s.X3, s.Y3); break;
                        default: result.Close(); break;
                    }
                }
            }

            pending.Clear();
            drawn = false;
        }

        foreach (var s in Segments)
        {
            switch (s.Type)
            {
                case Kind.Move:
                    Flush();
                    pending.Add(s);
                    current = start = (s.X1, s.Y1);
                    break;
                case Kind.Line:
                    if (pending.Count == 0)
                        pending.Add(new Segment(Kind.Move, current.X, current.Y, 0, 0, 0, 0));

                    if (s.X1 != current.X || s.Y1 != current.Y)
                    {
                        pending.Add(s);
                        drawn = true;
                        current = (s.X1, s.Y1);
                    }

                    break;
                case Kind.Curve:
                    if (pending.Count == 0)
                        pending.Add(new Segment(Kind.Move, current.X, current.Y, 0, 0, 0, 0));

                    if (!(s.X1 == current.X && s.Y1 == current.Y && s.X2 == current.X && s.Y2 == current.Y && s.X3 == current.X && s.Y3 == current.Y))
                    {
                        pending.Add(s);
                        drawn = true;
                    }

                    current = (s.X3, s.Y3);
                    break;
                default:
                    if (drawn)
                        pending.Add(s);

                    Flush();
                    current = start;
                    break;
            }
        }

        Flush();
        return result;
    }

    /// <summary>A copy with every point moved by the matrix.</summary>
    public PathData Transformed(Matrix3x2 matrix) => Transformed(Affine2D.From(matrix));

    /// <summary>A copy with every point moved by the transform, in double precision.</summary>
    public PathData Transformed(Affine2D matrix)
    {
        var copy = new PathData();
        (double X, double Y) P(double x, double y) => matrix.Apply(x, y);
        foreach (var s in Segments)
        {
            switch (s.Type)
            {
                case Kind.Move: copy.MoveTo(P(s.X1, s.Y1).X, P(s.X1, s.Y1).Y); break;
                case Kind.Line: copy.LineTo(P(s.X1, s.Y1).X, P(s.X1, s.Y1).Y); break;
                case Kind.Curve:
                    var (a, b, c) = (P(s.X1, s.Y1), P(s.X2, s.Y2), P(s.X3, s.Y3));
                    copy.CurveTo(a.X, a.Y, b.X, b.Y, c.X, c.Y);
                    break;
                default: copy.Close(); break;
            }
        }

        return copy;
    }

    /// <summary>Adds the segments of another path (already in this path's space).</summary>
    public void Append(PathData other)
    {
        foreach (var s in other.Segments)
        {
            switch (s.Type)
            {
                case Kind.Move: MoveTo(s.X1, s.Y1); break;
                case Kind.Line: LineTo(s.X1, s.Y1); break;
                case Kind.Curve: CurveTo(s.X1, s.Y1, s.X2, s.Y2, s.X3, s.Y3); break;
                default: Close(); break;
            }
        }
    }

    /// <summary>Builds the path on a canvas's path object.</summary>
    public GraphicsPath Build(PeachDrawing.Core.Canvas canvas, FillMode rule)
    {
        var path = canvas.GetGraphicsPath();
        path.FillMode = rule;
        var open = false;
        foreach (var s in Segments)
        {
            switch (s.Type)
            {
                case Kind.Move: path.AddMove(s.X1, s.Y1); open = true; break;
                case Kind.Line: path.LineTo(s.X1, s.Y1); break;
                case Kind.Curve: path.AddBezierTo(s.X1, s.Y1, s.X2, s.Y2, s.X3, s.Y3); break;
                default:
                    if (open) path.CloseFigure();
                    break;
            }
        }

        return path;
    }

    // ---- shapes, each transformed into the space the path is being built in by the matrix the caller gives

    public void AddRect(double x, double y, double w, double h, Matrix3x2 m)
    {
        var (p0, p1, p2, p3) = (T(m, x, y), T(m, x + w, y), T(m, x + w, y + h), T(m, x, y + h));
        MoveTo(p0.X, p0.Y);
        LineTo(p1.X, p1.Y);
        LineTo(p2.X, p2.Y);
        LineTo(p3.X, p3.Y);
        Close();
        MoveTo(p0.X, p0.Y);
    }

    public static Vector2 T(Matrix3x2 m, double x, double y) => Vector2.Transform(new Vector2((float)x, (float)y), m);

    /// <summary>An elliptical arc as Bezier curves; the first point is joined to the current one with a line.</summary>
    public void AddArc(double cx, double cy, double rx, double ry, double rotation, double start, double end, bool counterClockwise, Matrix3x2 m)
    {
        var sweep = end - start;
        var raw = sweep;
        const double TwoPi = Math.PI * 2;
        if (!counterClockwise)
        {
            if (sweep >= TwoPi) sweep = TwoPi;
            else { sweep %= TwoPi; if (sweep < 0) sweep += TwoPi; }
        }
        else
        {
            if (sweep <= -TwoPi) sweep = -TwoPi;
            else { sweep %= TwoPi; if (sweep > 0) sweep -= TwoPi; }
        }

        // Two different angles a whole turn apart (0 to 2π against the direction of the arc) are a whole circle, not no arc: only equal angles are no arc.
        if (sweep == 0 && raw != 0)
            sweep = counterClockwise ? -TwoPi : TwoPi;

        var (cosR, sinR) = (Math.Cos(rotation), Math.Sin(rotation));
        Vector2 Point(double angle)
        {
            var (x, y) = (rx * Math.Cos(angle), ry * Math.Sin(angle));
            return T(m, cx + x * cosR - y * sinR, cy + x * sinR + y * cosR);
        }

        Vector2 Tangent(double angle)
        {
            var (x, y) = (-rx * Math.Sin(angle), ry * Math.Cos(angle));
            var local = new Vector2((float)(x * cosR - y * sinR), (float)(x * sinR + y * cosR));
            return Vector2.TransformNormal(local, m);
        }

        var first = Point(start);
        if (HasPoint) LineTo(first.X, first.Y); else MoveTo(first.X, first.Y);
        if (sweep == 0)
            return;

        var pieces = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 2)));
        var step = sweep / pieces;
        var k = 4.0 / 3.0 * Math.Tan(step / 4);
        for (var i = 0; i < pieces; i++)
        {
            var (a0, a1) = (start + i * step, start + (i + 1) * step);
            var (p0, p3) = (Point(a0), Point(a1));
            var (t0, t3) = (Tangent(a0), Tangent(a1));
            CurveTo(p0.X + k * t0.X, p0.Y + k * t0.Y, p3.X - k * t3.X, p3.Y - k * t3.Y, p3.X, p3.Y);
        }
    }
}

/// <summary>What fillStyle and strokeStyle hold: a color, a gradient or a pattern.</summary>
internal abstract record PaintSource
{
    public sealed record ColorSource(PaintColor Color, string Text) : PaintSource
    {
        /// <summary>The color where an eight-bit sRGB one would lose it (a <c>color()</c> outside the sRGB gamut): extended sRGB red, green, blue and alpha.</summary>
        public (double R, double G, double B, double A)? Extended { get; init; }
    }

    public sealed record GradientSource(CanvasGradient Gradient) : PaintSource;

    public sealed record PatternSource(CanvasPattern Pattern) : PaintSource;
}

/// <summary>A <c>CanvasGradient</c>: linear, radial or conic, with its color stops.</summary>
internal sealed class CanvasGradient(string kind, double[] geometry)
{
    public string Kind { get; } = kind;

    /// <summary>x0, y0, x1, y1 (linear); x0, y0, r0, x1, y1, r1 (radial); angle, x, y (conic).</summary>
    public double[] Geometry { get; } = geometry;

    public List<(double Offset, PaintColor Color)> Stops { get; } = [];

    /// <summary>The canvas element whose <c>color</c> a <c>currentcolor</c> in a stop is.</summary>
    public ScriptNode? Owner { get; init; }

    /// <summary>Whether a stop is a color that is not legacy sRGB, which has the gradient go from stop to stop in Oklab.</summary>
    public bool NonLegacy { get; set; }

    /// <summary>Adds the colors between the stops that make the gradient interpolate in Oklab (<see cref="ScriptDocument.InterpolateGradientStops"/>).</summary>
    public Func<IReadOnlyList<(double Offset, PaintColor Color)>, List<(double Offset, PaintColor Color)>>? InterpolateInOklab { get; init; }
}

/// <summary>A <c>CanvasPattern</c>: an image and how it repeats.</summary>
internal sealed class CanvasPattern(Image image, string repetition)
{
    /// <summary>Whether its picture came from another origin without being allowed to be read: painting with it taints a canvas.</summary>
    public bool Tainted { get; init; }

    public Image Image { get; } = image;

    public string Repetition { get; } = repetition;

    /// <summary>The matrix <c>setTransform</c> gave: how the pattern's own space sits in the space of the path it fills.</summary>
    public Affine2D Transform { get; set; } = Affine2D.Identity;
}

/// <summary>One step of a filter: effects a layer applies, or an SVG filter of the document run over the pixels.</summary>
internal sealed record FilterStep(LayerEffect[] Effects, ScriptSvgFilter? Svg = null);

/// <summary>The settings <c>save()</c> and <c>restore()</c> keep.</summary>
internal sealed class CanvasState
{
    public Affine2D Transform { get; set; } = Affine2D.Identity;

    public PaintSource FillStyle { get; set; } = new PaintSource.ColorSource(PaintColor.Black, "#000000");

    public PaintSource StrokeStyle { get; set; } = new PaintSource.ColorSource(PaintColor.Black, "#000000");

    public double LineWidth { get; set; } = 1;

    public LineCap LineCap { get; set; } = LineCap.Butt;

    public LineJoin LineJoin { get; set; } = LineJoin.Miter;

    public double MiterLimit { get; set; } = 10;

    public double[] LineDash { get; set; } = [];

    public double LineDashOffset { get; set; }

    public double GlobalAlpha { get; set; } = 1;

    public string Composite { get; set; } = "source-over";

    public PaintColor ShadowColor { get; set; } = PaintColor.Transparent;

    public string ShadowColorText { get; set; } = "rgba(0, 0, 0, 0)";

    /// <summary>Where the shadow color really is when its eight-bit sRGB reading lost it (a <c>color()</c> outside the sRGB gamut).</summary>
    public (double R, double G, double B, double A)? ShadowExtended { get; set; }

    public double ShadowBlur { get; set; }

    public double ShadowOffsetX { get; set; }

    public double ShadowOffsetY { get; set; }

    public string Font { get; set; } = "10px sans-serif";

    public string LetterSpacing { get; set; } = "0px";

    public string WordSpacing { get; set; } = "0px";

    public string FontKerning { get; set; } = "auto";

    public string FontStretch { get; set; } = "normal";

    public string FontVariantCaps { get; set; } = "normal";

    public string TextRendering { get; set; } = "auto";

    public string Lang { get; set; } = "inherit";

    public string TextAlign { get; set; } = "start";

    public string TextBaseline { get; set; } = "alphabetic";

    public string Direction { get; set; } = "inherit";

    public bool ImageSmoothing { get; set; } = true;

    public string ImageSmoothingQuality { get; set; } = "low";

    public string Filter { get; set; } = "none";

    /// <summary>The steps <see cref="Filter"/> (or <see cref="FilterObject"/>) stands for, in order.</summary>
    public FilterStep[] FilterSteps { get; set; } = [];

    /// <summary>The <c>CanvasFilter</c> the filter was set to, which is what reading <c>filter</c> gives back, when it was set to one.</summary>
    public object? FilterObject { get; set; }

    public List<(PathData Path, FillMode Rule)> Clips { get; set; } = [];

    public CanvasState Clone() => new()
    {
        Transform = Transform, FillStyle = FillStyle, StrokeStyle = StrokeStyle, LineWidth = LineWidth, LineCap = LineCap, LineJoin = LineJoin,
        MiterLimit = MiterLimit, LineDash = LineDash, LineDashOffset = LineDashOffset, GlobalAlpha = GlobalAlpha, Composite = Composite,
        ShadowColor = ShadowColor, ShadowColorText = ShadowColorText, ShadowExtended = ShadowExtended, ShadowBlur = ShadowBlur, ShadowOffsetX = ShadowOffsetX, ShadowOffsetY = ShadowOffsetY,
        Font = Font, LetterSpacing = LetterSpacing, WordSpacing = WordSpacing, FontKerning = FontKerning, FontStretch = FontStretch, FontVariantCaps = FontVariantCaps, TextRendering = TextRendering, Lang = Lang, TextAlign = TextAlign, TextBaseline = TextBaseline, Direction = Direction, ImageSmoothing = ImageSmoothing,
        ImageSmoothingQuality = ImageSmoothingQuality, Filter = Filter, FilterSteps = FilterSteps, FilterObject = FilterObject, Clips = [.. Clips],
    };
}

/// <summary>
/// The drawing a script makes on a canvas: every call is kept as an operation that can be played again on any <see cref="PeachDrawing.Core.Canvas"/>, so
/// a PDF or SVG gets the shapes and text as vector content. The state (clips, transform, alpha, shadow, blend) each operation was made with travels with it.
/// </summary>
internal sealed class CanvasBitmap(ScriptDocument document, double width, double height) : IDisposable
{
    private readonly List<Action<PeachDrawing.Core.Canvas>> _ops = [];

    public ScriptDocument Document { get; } = document;

    public double Width { get; } = width;

    public double Height { get; } = height;

    public CanvasState State { get; private set; } = new();

    public Stack<CanvasState> Saved { get; } = [];

    public PathData Path { get; set; } = new();

    public int OperationCount => _ops.Count;

    /// <summary>The origin-clean flag: false once something from another origin that a script may not read has been drawn, after which the pixels cannot be read back.</summary>
    public bool OriginClean { get; private set; } = true;

    public void Taint() => OriginClean = false;

    // Painting with a pattern of a picture a script may not read taints the canvas.
    private void TaintFrom(PaintSource source)
    {
        if (source is PaintSource.PatternSource { Pattern.Tainted: true })
            OriginClean = false;
    }

    // The bitmap on the host's backend the drawing is also made on, when the host asked for one (HtmlDocumentOptions.CanvasSurfaces): what is drawn is drawn
    // there as it is recorded, and the page shows that, rather than playing the whole recording again at every paint. The recording is kept all the same, for
    // a PDF or SVG or any canvas of another backend, which plays it.
    private OffscreenSurface? _surface;
    private (int Revision, PeachDrawing.Core.Image Image)? _snapshot;

    /// <summary>Whether the drawing is kept on a bitmap of the host's backend as well as recorded.</summary>
    public bool HasSurface => _surface is not null;

    /// <summary>The color space the canvas keeps its pixels in: what a color or picture of another space is converted to when it is drawn.</summary>
    public string ColorSpace { get; private set; } = "srgb";

    /// <summary>Whether the canvas keeps floating-point pixels (a <c>float16</c> canvas), which a color outside the sRGB gamut or above 1 survives.</summary>
    public bool Wide { get; private set; }

    /// <summary>Sets the space and precision of the canvas, before anything is drawn on it.</summary>
    public void ConfigureColor(string space, bool wide)
    {
        ColorSpace = space;
        Wide = wide;
    }

    private (int Revision, float[] Pixels)? _widePixels;

    /// <summary>The pixels as straight RGBA floats in the canvas's space (four per pixel, row-major), not brought into 0 to 1 on a wide canvas.</summary>
    public float[] WidePixels()
    {
        if (_widePixels is { } cached && cached.Revision == Revision)
            return cached.Pixels;

        float[] floats;
        if (_surface is { IsWide: true } surface && surface.ReadWidePixels() is { } read)
        {
            floats = read;
        }
        else if (Wide)
        {
            floats = this.RenderWidePixels();
        }
        else
        {
            var bytes = Pixels();
            floats = new float[bytes.Length];
            for (var i = 0; i < bytes.Length; i++)
                floats[i] = bytes[i] / 255f;
        }

        _widePixels = (Revision, floats);
        return floats;
    }

    // ---- color in the canvas's space

    /// <summary>The brush of a style, its colors in the canvas's space (a wide color kept as it is on a floating-point canvas).</summary>
    public Brush BrushFor(PaintSource source, double alpha = 1)
    {
        if (source is PaintSource.ColorSource color && (Wide || ColorSpace != "srgb"))
        {
            var (r, g, b, a) = color.Extended ?? (color.Color.R / 255.0, color.Color.G / 255.0, color.Color.B / 255.0, color.Color.A / 255.0);
            var rgb = new[] { r, g, b };
            CanvasColorSpaces.Convert(rgb, "srgb", ColorSpace, clip: !Wide);
            return new SolidBrush(new WideColor((float)rgb[0], (float)rgb[1], (float)rgb[2], (float)(a * Math.Min(1, alpha))));
        }

        return MakeBrush(source, alpha, ColorSpace);
    }

    private PaintColor InSpace(PaintColor color) => CanvasColorSpaces.ConvertPaintColor(color, ColorSpace);

    // A style color as an eight-bit color of the canvas's space (a color() outside the sRGB gamut keeps its place in a wider space).
    private PaintColor InSpace(PaintSource.ColorSource color)
    {
        if (color.Extended is not { } e)
            return InSpace(color.Color);

        var rgb = new[] { e.R, e.G, e.B };
        CanvasColorSpaces.Convert(rgb, "srgb", ColorSpace, clip: true);
        return PaintColor.FromArgb(color.Color.A, (int)Math.Round(rgb[0] * 255), (int)Math.Round(rgb[1] * 255), (int)Math.Round(rgb[2] * 255));
    }

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Image, Dictionary<string, Image>> _converted = new();

    /// <summary>A picture in the canvas's space and precision: the picture itself when it is there already, else a converted copy (kept while the picture lives).</summary>
    public Image ForCanvas(Image image)
    {
        var from = CanvasColorSpaces.SpaceOf(image);
        var floats = CanvasColorSpaces.FloatsOf(image);
        var key = ColorSpace + (Wide ? "/wide" : "");
        var versions = _converted.GetOrCreateValue(image);
        if (versions.TryGetValue(key, out var done))
            return done;

        // A file that declares its own space (a Display-P3 PNG) is read again and converted from what it says it is, whatever space the canvas is in.
        if (from == "srgb" && floats is null && Document.ConvertImageToColorSpace(image, ColorSpace, Wide) is { } declared)
        {
            Image? fromFile;
            if (declared.Floats is { } declaredFloats)
            {
                fromFile = Document.CreateWideImage(declared.Width, declared.Height, declaredFloats);
            }
            else
            {
                fromFile = Document.DecodeImage(PngWriter.Encode(declared.Width, declared.Height, declared.Bytes!));
            }

            if (fromFile is not null)
            {
                CanvasColorSpaces.Tag(fromFile, ColorSpace, declared.Floats);
                versions[key] = fromFile;
                return fromFile;
            }
        }

        if (from == ColorSpace && (floats is not null || !Wide))
            return image;

        float[] straight;
        int width, height;
        if (floats is not null && image.GetPixels() is { } own)
        {
            (width, height, straight) = (own.Width, own.Height, (float[])floats.Clone());
        }
        else if (image.GetPixels() is { } buffer)
        {
            (width, height) = (buffer.Width, buffer.Height);
            straight = new float[width * height * 4];
            var span = buffer.PremultipliedRgba.Span;
            for (var i = 0; i + 3 < straight.Length; i += 4)
            {
                var a = span[i + 3];
                for (var c = 0; c < 3; c++)
                    straight[i + c] = a == 0 ? 0 : Math.Min(1f, span[i + c] / (float)a);

                straight[i + 3] = a / 255f;
            }
        }
        else
        {
            return image;
        }

        CanvasColorSpaces.ConvertFloats(straight, from, ColorSpace, clip: !Wide);
        Image? result;
        if (Wide)
        {
            result = Document.CreateWideImage(width, height, straight);
        }
        else
        {
            var bytes = new byte[straight.Length];
            for (var i = 0; i < bytes.Length; i++)
                bytes[i] = (byte)Math.Round(Math.Clamp(straight[i], 0f, 1f) * 255f);

            result = Document.DecodeImage(PngWriter.Encode(width, height, bytes));
        }

        if (result is null)
            return image;

        CanvasColorSpaces.Tag(result, ColorSpace, Wide ? straight : null);
        versions[key] = result;
        return result;
    }

    /// <summary>Asks the host for a bitmap to keep the drawing on, if it gave this document a way to make one.</summary>
    public void UseSurface()
    {
        if (_surface is not null || Width < 1 || Height < 1 || Width > int.MaxValue || Height > int.MaxValue)
            return;

        _surface = Document.CreateCanvasSurface((int)Width, (int)Height, Wide);
    }

    public ScriptCanvasPainting Painting => new(Width, Height, PaintTo, _surface is null ? null : this);

    private void PaintTo(PeachDrawing.Core.Canvas canvas)
    {
        if (_surface is { } surface && surface.CanDraw(canvas))
        {
            // One snapshot per change, kept until the drawing changes again (the previous one is released then, a paint later than the one that used it).
            if (_snapshot is not { } current || current.Revision != Revision)
            {
                var image = surface.CreateSnapshot();
                if (ColorSpace != "srgb" && PresentedInSrgb(image) is { } presented)
                {
                    image.Dispose();
                    image = presented;
                }

                _snapshot?.Image.Dispose();
                _snapshot = (Revision, image);
            }

            canvas.DrawImage(_snapshot.Value.Image, new Rect(0, 0, Width, Height));
            return;
        }

        Replay(canvas);
    }

    // A canvas in another space is shown on a page in sRGB: the picture of it, converted.
    private Image? PresentedInSrgb(Image image)
    {
        if (image.GetPixels() is not { } buffer)
            return null;

        var span = buffer.PremultipliedRgba.Span;
        var bytes = new byte[span.Length];
        for (var i = 0; i + 3 < bytes.Length; i += 4)
        {
            var a = span[i + 3];
            bytes[i + 3] = a;
            for (var c = 0; c < 3; c++)
                bytes[i + c] = a == 0 ? (byte)0 : (byte)Math.Min(255, (span[i + c] * 255 + a / 2) / a);
        }

        CanvasColorSpaces.ConvertBytes(bytes, ColorSpace, "srgb");
        return Document.DecodeImage(PngWriter.Encode(buffer.Width, buffer.Height, bytes));
    }

    // The layers open (beginLayer without its endLayer), innermost last: what is drawn goes to the innermost one's own list, and reaches the canvas when it ends.
    private readonly Stack<LayerFrame> _layers = [];

    /// <summary>Whether a layer is open: the drawing is not whole until it ends, so it cannot be read.</summary>
    public bool HasOpenLayers => _layers.Count > 0;

    private sealed class LayerFrame(CanvasState outer, int saveDepth, FilterStep[] effects)
    {
        public CanvasState Outer { get; } = outer;

        public int SaveDepth { get; } = saveDepth;

        public FilterStep[] Effects { get; } = effects;

        public List<Action<PeachDrawing.Core.Canvas>> Ops { get; } = [];
    }

    /// <summary>
    /// Opens a layer: what is drawn until <see cref="EndLayer"/> is drawn on a layer of its own with the compositing state (alpha, operator, shadow, filter) reset, and the
    /// layer is composited with the state the canvas had when it opened. The transform, clips and styles carry in unchanged.
    /// </summary>
    public void BeginLayer(FilterStep[] effects)
    {
        var outer = State.Clone();
        _layers.Push(new LayerFrame(outer, Saved.Count, effects));
        State = outer.Clone();
        State.GlobalAlpha = 1;
        State.Composite = "source-over";
        State.ShadowColor = PaintColor.Transparent;
        State.ShadowColorText = "rgba(0, 0, 0, 0)";
        State.ShadowExtended = null;
        State.ShadowBlur = 0;
        State.ShadowOffsetX = 0;
        State.ShadowOffsetY = 0;
        State.Filter = "none";
        State.FilterSteps = [];
        State.FilterObject = null;
    }

    /// <summary>Whether <see cref="EndLayer"/> can close a layer here: one is open and no <c>save()</c> made inside it is still waiting for its restore.</summary>
    public bool CanEndLayer => _layers.Count > 0 && _layers.Peek().SaveDepth == Saved.Count;

    /// <summary>Closes the innermost layer and composites it onto what is under it, with the state the canvas had when the layer was opened (which it also gets back).</summary>
    public void EndLayer()
    {
        var frame = _layers.Pop();
        var inside = State.Transform;
        State = frame.Outer;
        RebasePath(inside, State.Transform);

        var ops = frame.Ops.ToArray();
        var effects = frame.Effects;
        var margin = FilterMargin(effects);
        var bounds = new Rect(-margin, -margin, Width + 2 * margin, Height + 2 * margin);

        // The layer's own transform is already in each operation; the state's alpha, operator, shadow and clips composite the whole.
        var composite = State.Clone();
        composite.Transform = Affine2D.Identity;
        _needsGroup |= effects.Length > 0;
        RecordWith(composite, composite.Composite, canvas => DrawFiltered(canvas, effects, bounds, target =>
        {
            foreach (var op in ops)
                op(target);
        }));
    }

    private void AddOp(Action<PeachDrawing.Core.Canvas> op)
    {
        if (_layers.Count > 0)
        {
            _layers.Peek().Ops.Add(op);
            return;
        }

        _ops.Add(op);
        if (_surface is null)
            return;

        try
        {
            op(_surface.Canvas);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // A call the backend cannot make is left out, as a replay on a canvas that does not understand it leaves it out.
        }
    }

    public void Dispose()
    {
        _snapshot?.Image.Dispose();
        _snapshot = null;
        _surface?.Dispose();
        _surface = null;
    }

    /// <summary>Whether the drawing uses a Porter-Duff operator, which a document writer cannot express as vector content.</summary>
    private bool _usesOperators;

    /// <summary>Whether the drawing composites with anything but source-over, so it must be a group of its own: the mode acts on the canvas, not on the page behind it.</summary>
    private bool _needsGroup;

    /// <summary>
    /// Plays the drawing on a canvas. One that composites with anything but source-over (a blend mode, <c>lighter</c>, an operator) is played as a group of its
    /// own, so the mode acts on the canvas and not on the page behind it: in a layer, or, for a Porter-Duff operator when the target cannot composite with them (a PDF
    /// or SVG writer), onto a raster surface placed with <see cref="PeachDrawing.Core.Canvas.DrawRaster"/>. Vector wherever the format can express the drawing, a
    /// bitmap only where it cannot.
    /// </summary>
    public void Replay(PeachDrawing.Core.Canvas canvas)
    {
        if (_needsGroup)
        {
            var bounds = new Rect(0, 0, Width, Height);
            if (_usesOperators && !canvas.SupportsCompositeOperators)
            {
                if (canvas.BeginRasterSurface(bounds) is { } region)
                {
                    using (region)
                    {
                        foreach (var op in _ops.ToArray())
                            op(region.Graphics);

                        canvas.DrawRaster(region.Surface);
                    }

                    return;
                }
            }
            else if (canvas.BeginLayer(new LayerOptions(1.0, PaintBlendMode.Normal, null, bounds)) is { } group)
            {
                // An element is its own group: what an operator clears is cleared from the canvas, not from the page behind it.
                using (group)
                {
                    foreach (var op in _ops.ToArray())
                        op(group.Canvas);
                }

                return;
            }
        }

        foreach (var op in _ops.ToArray())
            op(canvas);
    }

    /// <summary>Replays onto a canvas that may not understand everything (an image another backend made): an operation that fails is skipped.</summary>
    public void ReplayGuarded(PeachDrawing.Core.Canvas canvas)
    {
        foreach (var op in _ops.ToArray())
        {
            try
            {
                op(canvas);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
            }
        }
    }

    /// <summary>Changes whenever the drawing does, so a read of the pixels can be reused until then.</summary>
    public int Revision { get; private set; }

    private (int Revision, byte[] Pixels)? _pixels;

    /// <summary>The drawing as pixels, kept until the drawing changes.</summary>
    public byte[] Pixels()
    {
        if (_pixels is { } cached && cached.Revision == Revision)
            return cached.Pixels;

        var rendered = _surface is null ? this.RenderPixels() : ReadSurface(_surface);
        _pixels = (Revision, rendered);
        return rendered;
    }

    // The surface's premultiplied pixels as the straight-alpha RGBA a script reads.
    private static byte[] ReadSurface(OffscreenSurface surface)
    {
        var pixels = surface.ReadPixels();
        var source = pixels.PremultipliedRgba.Span;
        var rgba = new byte[source.Length];
        for (var i = 0; i + 3 < rgba.Length; i += 4)
        {
            var alpha = source[i + 3];
            rgba[i + 3] = alpha;
            for (var c = 0; c < 3; c++)
                rgba[i + c] = alpha == 0 ? (byte)0 : alpha == 255 ? source[i + c] : (byte)Math.Min(255, (source[i + c] * 255 + alpha / 2) / alpha);
        }

        return rgba;
    }

    /// <summary>Puts pixels on the canvas as they are: no transform, clip, alpha or shadow applies to this.</summary>
    public void PutPixels(Image image, Rect source, Rect destination)
    {
        // Put pixels replace what is under them, transparent ones included, rather than being drawn over it: the Copy operator, bounded to the rectangle.
        Revision++;
        _usesOperators = true;
        _needsGroup = true;
        AddOp(canvas =>
        {
            canvas.PushBlendMode(PaintBlendMode.Copy);
            try
            {
                canvas.DrawImage(image, destination, source, ImageSampling.Pixelated);
            }
            finally
            {
                canvas.PopBlendMode();
            }
        });
    }

    public void Save() => Saved.Push(State.Clone());

    /// <summary>Whether <see cref="Restore"/> can pop here: a layer does not let a restore reach a state saved before it opened.</summary>
    public bool CanRestore => _layers.Count == 0 || _layers.Peek().SaveDepth < Saved.Count;

    public void Restore()
    {
        if (Saved.Count > 0)
        {
            var old = State.Transform;
            State = Saved.Pop();
            RebasePath(old, State.Transform);
        }
    }

    public void Reset()
    {
        Revision++;
        OriginClean = true;
        _ops.Clear();
        _layers.Clear();
        _surface?.Clear();
        _usesOperators = false;
        _needsGroup = false;
        Saved.Clear();
        State = new CanvasState();
        Path = new PathData();
        if (Opaque)
            PaintOpaqueBase();
    }

    /// <summary>Whether a transformation matrix can be undone.</summary>
    private static bool Invertible(Affine2D m) => m.IsInvertible;

    // ---- recording

    private static PaintBlendMode? BlendFor(string composite) => composite switch
    {
        "source-over" => PaintBlendMode.Normal,
        "lighter" => PaintBlendMode.Plus,
        "multiply" => PaintBlendMode.Multiply,
        "screen" => PaintBlendMode.Screen,
        "overlay" => PaintBlendMode.Overlay,
        "darken" => PaintBlendMode.Darken,
        "lighten" => PaintBlendMode.Lighten,
        "color-dodge" => PaintBlendMode.ColorDodge,
        "color-burn" => PaintBlendMode.ColorBurn,
        "hard-light" => PaintBlendMode.HardLight,
        "soft-light" => PaintBlendMode.SoftLight,
        "difference" => PaintBlendMode.Difference,
        "exclusion" => PaintBlendMode.Exclusion,
        "hue" => PaintBlendMode.Hue,
        "saturation" => PaintBlendMode.Saturation,
        "color" => PaintBlendMode.Color,
        "luminosity" => PaintBlendMode.Luminosity,
        "source-in" => PaintBlendMode.SourceIn,
        "source-out" => PaintBlendMode.SourceOut,
        "source-atop" => PaintBlendMode.SourceAtop,
        "destination-over" => PaintBlendMode.DestinationOver,
        "destination-in" => PaintBlendMode.DestinationIn,
        "destination-out" => PaintBlendMode.DestinationOut,
        "destination-atop" => PaintBlendMode.DestinationAtop,
        "xor" => PaintBlendMode.Xor,
        "copy" => PaintBlendMode.Copy,
        "clear" => PaintBlendMode.Clear,
        _ => null,
    };

    /// <summary>Whether a compositing operation is one this can draw (the others draw as source-over).</summary>
    public static bool IsKnownComposite(string value) => BlendFor(value) is not null;

    /// <summary>
    /// Keeps a drawing operation with the state it was made with. <paramref name="draw"/> runs with the canvas transformed by the transformation matrix of
    /// the moment, inside the clips then in force, in a layer when the alpha, blend mode or shadow ask for one.
    /// </summary>
    public void Record(Action<PeachDrawing.Core.Canvas> draw) => RecordWith(State, State.Composite, draw);

    /// <summary>
    /// <see cref="Record"/> with the compositing operation named instead of the state's. A Porter-Duff operator reaches the whole region the clips leave, not only the
    /// shape (<c>source-in</c> clears everything the shape does not cover), so the shape is drawn in a layer over the whole canvas and the layer is composited
    /// with the operator: the canvas backend does that natively, on the GPU where it has one.
    /// </summary>
    private void RecordWith(CanvasState state, string composite, Action<PeachDrawing.Core.Canvas> draw)
    {
        Revision++;
        var (clips, matrix) = (state.Clips.ToArray(), state.Transform.ToMatrix3x2());
        var alpha = state.GlobalAlpha;
        var blend = BlendFor(composite) ?? PaintBlendMode.Normal;
        _usesOperators |= blend >= PaintBlendMode.SourceIn;
        _needsGroup |= blend != PaintBlendMode.Normal;
        // A shadow is the shape's own alpha, in the shadow color, moved by the offset (which no transform touches) and blurred, drawn first and with the same
        // alpha and compositing as the shape; then the shape is drawn.
        var hasShadow = state.ShadowColor.A > 0 && (state.ShadowBlur > 0 || state.ShadowOffsetX != 0 || state.ShadowOffsetY != 0);
        var (shadowDx, shadowDy, sigma, shadowColor) = (state.ShadowOffsetX, state.ShadowOffsetY, state.ShadowBlur / 2, InSpace(new PaintSource.ColorSource(state.ShadowColor, state.ShadowColorText) { Extended = state.ShadowExtended }));
        var (width, height) = (Width, Height);

        // The filter acts on the shape alone: before the shadow (which is the filtered shape's) and before alpha and compositing.
        var filterSteps = state.FilterSteps;
        var filterMargin = FilterMargin(filterSteps);
        void Content(PeachDrawing.Core.Canvas target) => DrawFiltered(target, filterSteps, new Rect(-filterMargin, -filterMargin, width + 2 * filterMargin, height + 2 * filterMargin), inner =>
        {
            inner.PushTransform(matrix);
            try
            {
                draw(inner);
            }
            finally
            {
                inner.PopTransform();
            }
        });

        AddOp(canvas =>
        {
            foreach (var (clip, rule) in clips)
            {
                using var path = clip.Build(canvas, rule);
                canvas.PushClip(path);
            }

            try
            {
                if (hasShadow)
                    DrawShadow(canvas, Content, (shadowDx, shadowDy), sigma, shadowColor, alpha, blend, width, height);

                CanvasLayer? layer = null;
                if (alpha < 1 || blend != PaintBlendMode.Normal)
                    layer = canvas.BeginLayer(new LayerOptions(alpha, blend, null, new Rect(0, 0, width, height)));

                try
                {
                    Content(layer?.Canvas ?? canvas);
                }
                finally
                {
                    layer?.Dispose();
                }
            }
            finally
            {
                for (var i = 0; i < clips.Length; i++)
                    canvas.PopClip();
            }
        });
    }

    // The shadow of one drawing: the drawing in a layer over the visible canvas (and as far beyond it as the blur reaches), moved by the offset first so that a shape outside
    // the canvas whose shadow falls inside it is still drawn, recolored to the shadow color with the shape's alpha kept, and blurred. A backend that cannot make the layer
    // draws no shadow.
    private static void DrawShadow(PeachDrawing.Core.Canvas canvas, Action<PeachDrawing.Core.Canvas> draw, (double X, double Y) offset, double sigma, PaintColor color,
        double alpha, PaintBlendMode blend, double width, double height)
    {
        // Every color becomes the shadow color and the alpha is the shape's times the shadow color's.
        var linear = new Matrix4x4 { M44 = color.A / 255f };
        var recolor = new ColorMatrix(linear, new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, 0));
        var effects = new List<LayerEffect> { new ColorMatrixEffect(recolor) };
        if (sigma > 0)
            effects.Add(new BlurEffect(sigma, sigma));

        var margin = sigma * 3 + 2;
        using var layer = canvas.BeginLayer(new LayerOptions(alpha, blend, null, new Rect(-margin, -margin, width + 2 * margin, height + 2 * margin), effects));
        if (layer is null)
            return;

        var target = layer.Canvas;
        target.PushTransform(Matrix3x2.CreateTranslation((float)offset.X, (float)offset.Y));
        try
        {
            draw(target);
        }
        finally
        {
            target.PopTransform();
        }
    }

    // How far beyond the canvas what a filter draws can reach, so a layer is cut wide enough that a blur or a shadow is not clipped at the canvas's edge.
    private static double FilterMargin(FilterStep[] steps)
    {
        double margin = 0;
        foreach (var effect in steps.SelectMany(step => step.Effects))
        {
            margin += effect switch
            {
                BlurEffect blur => 3 * Math.Max(blur.SigmaX, blur.SigmaY),
                DropShadowEffect shadow => Math.Abs(shadow.OffsetX) + Math.Abs(shadow.OffsetY) + 3 * Math.Max(shadow.SigmaX, shadow.SigmaY),
                _ => 0,
            };
        }

        return steps.Length == 0 ? 0 : margin + 2;
    }

    // Draws what <paramref name="draw"/> draws through a filter. Effects alone are a layer with those effects (on the GPU where the canvas can); a filter of the document's
    // is run over the pixels, so the drawing goes to a raster surface, through the steps in order, and is placed on the canvas.
    private static void DrawFiltered(PeachDrawing.Core.Canvas canvas, FilterStep[] steps, Rect bounds, Action<PeachDrawing.Core.Canvas> draw)
    {
        if (steps.Length == 0)
        {
            draw(canvas);
            return;
        }

        if (steps.All(step => step.Svg is null))
        {
            using var layer = canvas.BeginLayer(new LayerOptions(1.0, PaintBlendMode.Normal, null, bounds, steps.SelectMany(step => step.Effects).ToArray()));
            draw(layer?.Canvas ?? canvas);
            return;
        }

        using var scope = canvas.BeginRasterSurface(bounds);
        if (scope is null)
        {
            draw(canvas);
            return;
        }

        draw(scope.Graphics);
        foreach (var step in steps)
        {
            if (step.Svg is { } svg)
                svg.Apply(canvas, scope.Surface, bounds);
            else if (step.Effects.Length > 0)
                PeachDrawing.RasterLayerEffects.Apply(scope.Surface, step.Effects);
        }

        canvas.DrawRaster(scope.Surface);
    }

    /// <summary>Starts over: everything drawn so far is dropped (what <c>clearRect</c> over the whole canvas comes to).</summary>
    public void ClearDrawing()
    {
        Revision++;

        // Inside a layer only the layer's own drawing is cleared.
        if (_layers.Count > 0)
        {
            _layers.Peek().Ops.Clear();
            return;
        }

        _ops.Clear();
        _surface?.Clear();
        _usesOperators = false;
        _needsGroup = false;
        if (Opaque)
            PaintOpaqueBase();
    }

    /// <summary>Whether the canvas has no alpha (<c>alpha: false</c>): it is opaque black where nothing has been drawn, and clearing it gives black again.</summary>
    public bool Opaque { get; private set; }

    /// <summary>Makes the canvas one with no alpha channel, black to begin with.</summary>
    public void MakeOpaque()
    {
        Opaque = true;
        PaintOpaqueBase();
    }

    private void PaintOpaqueBase()
    {
        var (w, h) = (Width, Height);
        AddOp(canvas => canvas.DrawRectangle(new SolidBrush(PaintColor.Black), 0, 0, w, h));
    }

    // ---- brushes and pens

    public static Brush MakeBrush(PaintSource source, double alpha = 1, string space = "srgb")
    {
        switch (source)
        {
            case PaintSource.ColorSource c:
                return new SolidBrush(CanvasColorSpaces.ConvertPaintColor(alpha >= 1 ? c.Color : PaintColor.FromArgb((int)Math.Round(c.Color.A * alpha), c.Color.R, c.Color.G, c.Color.B), space));
            case PaintSource.GradientSource g:
                return MakeGradient(g.Gradient, space);
            default:
                return new SolidBrush(PaintColor.Transparent);
        }
    }

    private static Brush MakeGradient(CanvasGradient gradient, string space = "srgb")
    {
        var stops = gradient.Stops.OrderBy(s => s.Offset).Select(s => (s.Offset, Color: CanvasColorSpaces.ConvertPaintColor(s.Color, space))).ToList();
        if (gradient.NonLegacy && gradient.InterpolateInOklab is { } interpolate && stops.Count > 1)
            stops = interpolate(stops);

        // A line with no length, and two equal circles, have no direction to paint along: nothing is painted.
        var degenerate = gradient.Kind switch
        {
            "linear" => gradient.Geometry[0] == gradient.Geometry[2] && gradient.Geometry[1] == gradient.Geometry[3],
            "radial" => gradient.Geometry[0] == gradient.Geometry[3] && gradient.Geometry[1] == gradient.Geometry[4] && gradient.Geometry[2] == gradient.Geometry[5],
            _ => false,
        };
        if (stops.Count == 0 || degenerate)
            return new SolidBrush(PaintColor.Transparent);
        if (stops.Count == 1)
            return new SolidBrush(stops[0].Color);

        // A canvas gradient mixes colors and alphas separately (not premultiplied, as the brush does), so a segment whose ends differ in alpha is given the colors in between.
        stops = StraightAlphaStops(stops);
        var list = stops.Select(s => new GradientStop(s.Color, s.Offset)).ToList();
        var g = gradient.Geometry;
        switch (gradient.Kind)
        {
            case "linear":
                return new LinearGradientBrush(new PaintPoint(g[0], g[1]), new PaintPoint(g[2], g[3]), list, GradientSpread.Pad);
            case "radial":
                return new RadialGradientBrush(new PaintPoint(g[3], g[4]), new PaintPoint(g[0], g[1]), g[5], g[5], list, GradientSpread.Pad) { FocusRadius = g[2] };
            default:
                // A canvas measures the start angle clockwise from the positive x axis (3 o'clock); the brush measures from the top, as CSS does.
                // A conic brush's stop positions are those angles, in radians, not offsets from 0 to 1 (see RenderContext.GetConicGradientBrush).
                var angles = stops.Select(s => g[0] + Math.PI / 2 + s.Offset * Math.PI * 2).ToList();
                var conicStops = stops.Select((s, i) => new GradientStop(s.Color, angles[i])).ToList();
                return new ConicGradientBrush(new PaintPoint(g[1], g[2]), 1e6, conicStops, angles);
        }
    }

    private static List<(double Offset, PaintColor Color)> StraightAlphaStops(List<(double Offset, PaintColor Color)> stops)
    {
        if (stops.All(s => s.Color.A == stops[0].Color.A))
            return stops;

        const int steps = 32;
        var result = new List<(double Offset, PaintColor Color)> { stops[0] };
        for (var i = 1; i < stops.Count; i++)
        {
            var (from, to) = (stops[i - 1], stops[i]);
            if (from.Color.A != to.Color.A && to.Offset > from.Offset)
            {
                for (var step = 1; step < steps; step++)
                {
                    var t = (double)step / steps;
                    int Mix(int a, int b) => (int)Math.Round(a + (b - a) * t);
                    result.Add((from.Offset + (to.Offset - from.Offset) * t, PaintColor.FromArgb(Mix(from.Color.A, to.Color.A), Mix(from.Color.R, to.Color.R), Mix(from.Color.G, to.Color.G), Mix(from.Color.B, to.Color.B))));
                }
            }

            result.Add(to);
        }

        return result;
    }

    public Pen MakePen(CanvasState state)
    {
        TaintFrom(state.StrokeStyle);
        var pen = new Pen
        {
            Paint = BrushFor(state.StrokeStyle),
            Width = state.LineWidth,
            LineCap = state.LineCap,
            LineJoin = state.LineJoin,
            MiterLimit = state.MiterLimit,
        };
        if (state.LineDash.Length > 0)
            pen.SetDashPattern(state.LineDash, state.LineDashOffset);

        return pen;
    }

    // ---- drawing calls

    public void FillPath(PathData path, FillMode rule)
    {
        if (!Invertible(State.Transform) || path.Segments.Count == 0)
            return;

        // The path is in the space the transform makes (a copy: the script may go on adding to it before this is drawn).
        var user = path.Clone();
        var brush = State.FillStyle;
        TaintFrom(brush);
        var sampling = State.ImageSmoothing ? ImageSampling.Automatic : ImageSampling.Nearest;
        var patternImage = brush is PaintSource.PatternSource fillPattern ? ForCanvas(fillPattern.Pattern.Image) : null;
        var alpha = 1.0;
        Record(canvas =>
        {
            using var built = user.Build(canvas, rule);
            if (brush is PaintSource.PatternSource pattern)
                FillWithPattern(canvas, pattern.Pattern, patternImage!, built, BoundsOf(user), sampling);
            else
                canvas.DrawPath(BrushFor(brush, alpha), built);
        });
    }

    // A pattern fill: the path is the clip, and the image is drawn as many times as covers the path's bounds, as the repetition says.
    private static (double MinX, double MinY, double MaxX, double MaxY) BoundsOf(PathData bounds)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var segment in bounds.Segments)
        {
            foreach (var (x, y) in new[] { (segment.X1, segment.Y1), (segment.X2, segment.Y2), (segment.X3, segment.Y3) })
            {
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
        }

        return (minX, minY, maxX, maxY);
    }

    private static void FillWithPattern(PeachDrawing.Core.Canvas canvas, CanvasPattern pattern, Image tile, GraphicsPath clip, (double MinX, double MinY, double MaxX, double MaxY) box, ImageSampling sampling)
    {
        var (minX, minY, maxX, maxY) = box;
        double w = pattern.Image.Width, h = pattern.Image.Height;
        if (w <= 0 || h <= 0 || minX > maxX || !pattern.Transform.TryInvert(out var inverse))
            return;

        // The path's box in the pattern's own space, which the tiles cover.
        double tileMinX = double.MaxValue, tileMinY = double.MaxValue, tileMaxX = double.MinValue, tileMaxY = double.MinValue;
        foreach (var (cornerX, cornerY) in new[] { (minX, minY), (maxX, minY), (minX, maxY), (maxX, maxY) })
        {
            var (px, py) = inverse.Apply(cornerX, cornerY);
            tileMinX = Math.Min(tileMinX, px); tileMaxX = Math.Max(tileMaxX, px);
            tileMinY = Math.Min(tileMinY, py); tileMaxY = Math.Max(tileMaxY, py);
        }

        if (!double.IsFinite(tileMinX + tileMaxX + tileMinY + tileMaxY))
            return;

        var repeatX = pattern.Repetition is "repeat" or "repeat-x";
        var repeatY = pattern.Repetition is "repeat" or "repeat-y";
        var (startX, endX) = repeatX ? (Math.Floor(tileMinX / w) * w, tileMaxX) : (0.0, 0.0);
        var (startY, endY) = repeatY ? (Math.Floor(tileMinY / h) * h, tileMaxY) : (0.0, 0.0);
        if ((endX - startX) / w * ((endY - startY) / h) > 40_000)
            return;

        canvas.PushClip(clip);
        canvas.PushTransform(pattern.Transform.ToMatrix3x2());
        try
        {
            for (var y = startY; y <= endY; y += h)
            {
                for (var x = startX; x <= endX; x += w)
                    canvas.DrawImage(tile, new Rect(x, y, w, h), new Rect(0, 0, w, h), sampling);
                if (!repeatX && !repeatY)
                    break;
            }
        }
        finally
        {
            canvas.PopTransform();
            canvas.PopClip();
        }
    }

    public void StrokePath(PathData path)
    {
        if (!Invertible(State.Transform) || path.Segments.Count == 0)
            return;

        var user = path.PrunedForStroke();
        if (user.Segments.Count == 0)
            return;

        var pen = MakePen(State);
        if (State.StrokeStyle is PaintSource.PatternSource stroked)
        {
            // A pattern is not a pen's paint: the line is made into an outline, which the pattern then fills.
            var transform = State.Transform;
            var scale = Math.Sqrt(Math.Abs(transform.A * transform.D - transform.B * transform.C));
            var tolerance = scale > 0 && double.IsFinite(scale) ? 0.1 / scale : 0.1;
            var smoothing = State.ImageSmoothing ? ImageSampling.Automatic : ImageSampling.Nearest;
            var strokedImage = ForCanvas(stroked.Pattern.Image);
            Record(canvas =>
            {
                using var built = user.Build(canvas, FillMode.Nonzero);
                using var outline = canvas.GetGraphicsPath();
                PeachDrawing.PathStroker.Stroke(built, pen, outline, tolerance);
                var points = outline.Flatten(tolerance).SelectMany(c => c.Points).ToList();
                if (points.Count > 0)
                    FillWithPattern(canvas, stroked.Pattern, strokedImage, outline, (points.Min(q => q.X), points.Min(q => q.Y), points.Max(q => q.X), points.Max(q => q.Y)), smoothing);
            });
            return;
        }

        Record(canvas =>
        {
            using var built = user.Build(canvas, FillMode.Nonzero);
            canvas.DrawPath(pen, built);
        });
    }

    public void FillRect(double x, double y, double w, double h)
    {
        if (w == 0 || h == 0)
            return;

        var brush = State.FillStyle;
        TaintFrom(brush);
        if (brush is PaintSource.PatternSource)
        {
            var rectangle = new PathData();
            rectangle.AddRect(Math.Min(x, x + w), Math.Min(y, y + h), Math.Abs(w), Math.Abs(h), Matrix3x2.Identity);
            FillPath(rectangle, FillMode.Nonzero);
            return;
        }

        Record(canvas => canvas.DrawRectangle(BrushFor(brush), Math.Min(x, x + w), Math.Min(y, y + h), Math.Abs(w), Math.Abs(h)));
    }

    public void StrokeRect(double x, double y, double w, double h)
    {
        if (w == 0 && h == 0)
            return;

        // A rectangle with no width or no height is a line out and back, closed: it has joins at both ends and no caps.
        if (w == 0 || h == 0)
        {
            var line = new PathData();
            line.MoveTo(x, y);
            line.LineTo(x + w, y + h);
            line.LineTo(x, y);
            line.Close();
            StrokePath(line);
            return;
        }

        var pen = MakePen(State);
        Record(canvas => canvas.DrawRectangle(pen, Math.Min(x, x + w), Math.Min(y, y + h), Math.Abs(w), Math.Abs(h)));
    }

    public void ClearRect(double x, double y, double w, double h)
    {
        // Clearing all of the drawing, which is what animation loops do, starts over and keeps nothing to composite.
        var m = State.Transform;
        var corners = new[] { m.Apply(x, y), m.Apply(x + w, y), m.Apply(x, y + h), m.Apply(x + w, y + h) };
        if (m.B == 0 && m.C == 0 && State.Clips.Count == 0
            && corners.Min(c => c.X) <= 0 && corners.Min(c => c.Y) <= 0 && corners.Max(c => c.X) >= Width && corners.Max(c => c.Y) >= Height)
        {
            ClearDrawing();
            return;
        }

        // Part of the canvas: erase it, as destination-out with an opaque shape does (a canvas with no alpha is painted black instead).
        var eraser = State.Clone();
        eraser.FillStyle = new PaintSource.ColorSource(PaintColor.Black, "#000000");
        eraser.GlobalAlpha = 1;
        eraser.ShadowColor = PaintColor.Transparent;
        RecordWith(eraser, Opaque ? "source-over" : "destination-out", canvas => canvas.DrawRectangle(new SolidBrush(PaintColor.Black), Math.Min(x, x + w), Math.Min(y, y + h), Math.Abs(w), Math.Abs(h)));
    }

    /// <summary>Clips to a path in the space the transform makes: a clip is kept where it lies on the canvas, whatever the transform becomes.</summary>
    public void ClipPath(PathData path, FillMode rule) => State.Clips = [.. State.Clips, (path.Transformed(State.Transform), rule)];

    public void DrawImage(Image image, Rect source, Rect destination)
    {
        image = ForCanvas(image);

        // Smoothing off is the nearest pixel, shrinking or enlarging (Pixelated would still average a shrunk picture).
        var sampling = State.ImageSmoothing ? ImageSampling.Automatic : ImageSampling.Nearest;

        // The part of the source outside the picture is not drawn, and the destination shrinks with it (the spec's clipping of the source and destination rectangles).
        if (!(source.Width > 0) || !(source.Height > 0))
            return;

        var (x0, y0, x1, y1) = (Math.Max(source.X, 0), Math.Max(source.Y, 0), Math.Min(source.X + source.Width, image.Width), Math.Min(source.Y + source.Height, image.Height));
        if (!(x1 > x0) || !(y1 > y0))
            return;

        if (x0 != source.X || y0 != source.Y || x1 != source.X + source.Width || y1 != source.Y + source.Height)
        {
            var (scaleX, scaleY) = (destination.Width / source.Width, destination.Height / source.Height);
            destination = new Rect(destination.X + (x0 - source.X) * scaleX, destination.Y + (y0 - source.Y) * scaleY, (x1 - x0) * scaleX, (y1 - y0) * scaleY);
            source = new Rect(x0, y0, x1 - x0, y1 - y0);
        }

        Record(canvas => canvas.DrawImage(image, destination, source, sampling));
    }

    /// <summary>Draws a loaded SVG image: its part <paramref name="source"/> (in the image's own size) lands on <paramref name="destination"/>, clipped to it.</summary>
    public void DrawSvg(string address, double imageWidth, double imageHeight, Rect source, Rect destination)
    {
        if (!(source.Width > 0) || !(source.Height > 0) || !(destination.Width > 0) || !(destination.Height > 0) || !(imageWidth > 0) || !(imageHeight > 0))
            return;

        var (scaleX, scaleY) = (destination.Width / source.Width, destination.Height / source.Height);
        var viewport = new Rect(destination.X - source.X * scaleX, destination.Y - source.Y * scaleY, imageWidth * scaleX, imageHeight * scaleY);
        var document = Document;
        Record(canvas =>
        {
            canvas.PushClip(destination);
            try
            {
                document.DrawSvgImage(canvas, address, viewport);
            }
            finally
            {
                canvas.PopClip();
            }
        });
    }

    /// <summary>Draws a source that holds nothing (a canvas with a size and no context): transparent, which an operator that reaches the whole canvas still acts on.</summary>
    public void DrawTransparent(Rect destination) =>
        Record(canvas => canvas.DrawRectangle(canvas.GetSolidBrush(PaintColor.FromArgb(0, 0, 0, 0)), destination.X, destination.Y, destination.Width, destination.Height));

    public void DrawCanvas(CanvasBitmap other, Rect destination)
    {
        if (other.Width <= 0 || other.Height <= 0)
            return;

        var scale = Matrix3x2.CreateScale((float)(destination.Width / other.Width), (float)(destination.Height / other.Height)) * Matrix3x2.CreateTranslation((float)destination.X, (float)destination.Y);
        // The source is read when drawImage is called (a snapshot, as the spec says), not when this canvas is painted: a live reference would replay a canvas
        // drawn onto itself, or two drawn onto each other, forever.
        var snapshot = other._ops.ToArray();
        _usesOperators |= other._usesOperators;
        _needsGroup |= other._needsGroup;
        Record(canvas =>
        {
            canvas.PushTransform(scale);
            try
            {
                foreach (var op in snapshot)
                    op(canvas);
            }
            finally
            {
                canvas.PopTransform();
            }
        });
    }

    /// <summary>
    /// Draws a laid-out run. <paramref name="anchor"/> is how far from the run's left edge the point <paramref name="x"/> falls (where the text's alignment puts it), and
    /// <paramref name="baselineY"/> the alphabetic baseline. A run wider than <paramref name="maxWidth"/> is condensed to it.
    /// </summary>
    public void DrawText(string text, double x, double baselineY, double anchor, bool stroke, double? maxWidth, Font font, TextRunMetrics metrics, double width, double letterSpacing, double wordSpacing,
        ShapeSettings shape, PaintSource fill, Pen? pen)
    {
        if (text.Length == 0)
            return;

        TaintFrom(fill);
        var scale = maxWidth is { } max && width > max && max > 0 ? max / width : 1.0;
        var left = x - anchor * scale;

        // With a word spacing the words are drawn each where the measure puts them; otherwise the run is one piece.
        // A font with no small capitals is drawn with capitals at a smaller size: each size is a piece of its own, in a font of its own.
        var shown = text;
        Font? smallCapsFont = null;
        var sizes =new List<(string Text, int Start, bool Small)> { (text, 0, false) };
        if (SyntheticCaps.Applies(font, shape) && font.Typeface is { } typeface)
        {
            shown = SyntheticCaps.Convert(text, shape.Caps, out var small);
            sizes = [.. SyntheticCaps.Split(shown, small)];
            shape = shape with { Caps = CapsMode.None };
            smallCapsFont = new TypefaceFont(typeface, font.Size * SyntheticCaps.Scale);
        }

        var pieces = new List<(string Text, double X, bool Small)>();
        foreach (var (sizeText, sizeStart, isSmall) in sizes)
        {
            if (wordSpacing != 0 && sizeText.Contains(' ', StringComparison.Ordinal))
            {
                var start = 0;
                for (var i = 0; i <= sizeText.Length; i++)
                {
                    if (i == sizeText.Length || sizeText[i] == ' ')
                    {
                        if (i > start)
                            pieces.Add((sizeText[start..i], metrics.Clusters.FirstOrDefault(c => c.Start == sizeStart + start) is { Width: > 0 } cluster ? cluster.X : 0, isSmall));
                        start = i + 1;
                    }
                }
            }
            else
            {
                pieces.Add((sizeText, sizeStart == 0 ? 0 : metrics.Clusters.FirstOrDefault(c => c.Start == sizeStart).X, isSmall));
            }
        }

        Record(canvas =>
        {
            if (scale != 1.0)
                canvas.PushTransform(Matrix3x2.CreateScale((float)scale, 1) * Matrix3x2.CreateTranslation((float)(left * (1 - scale)), 0));

            try
            {
                foreach (var (piece, offset, isSmall) in pieces)
                {
                    var pieceLeft = left + offset;
                    var pieceFont = isSmall && smallCapsFont is not null ? smallCapsFont : font;
                    var pieceTop = baselineY - pieceFont.Ascent;
                    var advance = piece == text && smallCapsFont is null ? width : TextRunMeasure.Measure(pieceFont, piece, letterSpacing, 0, shape).Width;
                    if (!stroke && fill is PaintSource.ColorSource color)
                    {
                        canvas.DrawString(piece, pieceFont, InSpace(color), new PaintPoint(pieceLeft, pieceTop), new Size(advance, pieceFont.Height), letterSpacing, null, shape);
                    }
                    else if (canvas.GetTextOutline(piece, pieceFont, new PaintPoint(pieceLeft, baselineY), letterSpacing, shape) is { } outline)
                    {
                        using (outline)
                        {
                            if (stroke && pen is not null)
                                canvas.DrawPath(pen, outline);
                            else
                                canvas.DrawPath(BrushFor(fill), outline);
                        }
                    }
                }
            }
            finally
            {
                if (scale != 1.0)
                    canvas.PopTransform();
            }
        });
    }

    /// <summary>The state's transform moved by a matrix applied first (what <c>transform()</c> and <c>translate()</c> do).</summary>
    public void Concat(Affine2D matrix) => SetTransform(matrix * State.Transform);

    /// <summary>
    /// Changes the transform. The path being built is kept in the space the transform makes, so it moves with it: where it lies on the canvas is the same before and after.
    /// A transform that cannot be undone leaves the path as it is.
    /// </summary>
    public void SetTransform(Affine2D matrix)
    {
        var old = State.Transform;
        State.Transform = matrix;
        RebasePath(old, matrix);
    }

    private void RebasePath(Affine2D from, Affine2D to)
    {
        if (from != to && to.TryInvert(out var inverse) && from.IsInvertible && Path.Segments.Count > 0)
            Path = Path.Transformed(from * inverse);
    }

    public static string ColorText(PaintColor color) =>
        color.A == 255
            ? $"#{color.R:x2}{color.G:x2}{color.B:x2}"
            : string.Create(CultureInfo.InvariantCulture, $"rgba({color.R}, {color.G}, {color.B}, {AlphaText(color.A)})");

    /// <summary>The alpha of a color as the HTML standard serializes it: the shortest number, of two decimal places and else three, that reads back as the same 8-bit alpha.</summary>
    internal static string AlphaText(byte alpha)
    {
        if (alpha == 0)
            return "0";

        foreach (var digits in new[] { 2, 3 })
        {
            var rounded = Math.Round(alpha / 255.0, digits, MidpointRounding.AwayFromZero);
            if ((int)Math.Round(rounded * 255, MidpointRounding.AwayFromZero) == alpha)
                return rounded.ToString("0.###", CultureInfo.InvariantCulture);
        }

        return Math.Round(alpha / 255.0, 3, MidpointRounding.AwayFromZero).ToString("0.###", CultureInfo.InvariantCulture);
    }
}
