using PeachBrackets.ScriptHosting;
using PeachDrawing.Core;
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

    /// <summary>A copy with every point moved by the matrix.</summary>
    public PathData Transformed(Matrix3x2 matrix)
    {
        var copy = new PathData();
        Vector2 P(double x, double y) => Vector2.Transform(new Vector2((float)x, (float)y), matrix);
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
    public sealed record ColorSource(PaintColor Color, string Text) : PaintSource;

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
}

/// <summary>A <c>CanvasPattern</c>: an image and how it repeats.</summary>
internal sealed class CanvasPattern(Image image, string repetition)
{
    public Image Image { get; } = image;

    public string Repetition { get; } = repetition;
}

/// <summary>The settings <c>save()</c> and <c>restore()</c> keep.</summary>
internal sealed class CanvasState
{
    public Matrix3x2 Transform { get; set; } = Matrix3x2.Identity;

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

    public double ShadowBlur { get; set; }

    public double ShadowOffsetX { get; set; }

    public double ShadowOffsetY { get; set; }

    public string Font { get; set; } = "10px sans-serif";

    public string TextAlign { get; set; } = "start";

    public string TextBaseline { get; set; } = "alphabetic";

    public string Direction { get; set; } = "inherit";

    public bool ImageSmoothing { get; set; } = true;

    public string ImageSmoothingQuality { get; set; } = "low";

    public string Filter { get; set; } = "none";

    public List<(PathData Path, FillMode Rule)> Clips { get; set; } = [];

    public CanvasState Clone() => new()
    {
        Transform = Transform, FillStyle = FillStyle, StrokeStyle = StrokeStyle, LineWidth = LineWidth, LineCap = LineCap, LineJoin = LineJoin,
        MiterLimit = MiterLimit, LineDash = LineDash, LineDashOffset = LineDashOffset, GlobalAlpha = GlobalAlpha, Composite = Composite,
        ShadowColor = ShadowColor, ShadowColorText = ShadowColorText, ShadowBlur = ShadowBlur, ShadowOffsetX = ShadowOffsetX, ShadowOffsetY = ShadowOffsetY,
        Font = Font, TextAlign = TextAlign, TextBaseline = TextBaseline, Direction = Direction, ImageSmoothing = ImageSmoothing,
        ImageSmoothingQuality = ImageSmoothingQuality, Filter = Filter, Clips = [.. Clips],
    };
}

/// <summary>
/// The drawing a script makes on a canvas: every call is kept as an operation that can be played again on any <see cref="PeachDrawing.Core.Canvas"/>, so
/// a PDF or SVG gets the shapes and text as vector content. The state (clips, transform, alpha, shadow, blend) each operation was made with travels with it.
/// </summary>
internal sealed class CanvasBitmap(ScriptDocument document, double width, double height)
{
    private readonly List<Action<PeachDrawing.Core.Canvas>> _ops = [];

    public ScriptDocument Document { get; } = document;

    public double Width { get; } = width;

    public double Height { get; } = height;

    public CanvasState State { get; private set; } = new();

    public Stack<CanvasState> Saved { get; } = [];

    public PathData Path { get; set; } = new();

    public int OperationCount => _ops.Count;

    public ScriptCanvasPainting Painting => new(Width, Height, Replay);

    public void Replay(PeachDrawing.Core.Canvas canvas)
    {
        foreach (var op in _ops.ToArray())
            op(canvas);
    }

    public void Save() => Saved.Push(State.Clone());

    public void Restore()
    {
        if (Saved.Count > 0)
            State = Saved.Pop();
    }

    public void Reset()
    {
        _ops.Clear();
        Saved.Clear();
        State = new CanvasState();
        Path = new PathData();
    }

    /// <summary>Whether a transformation matrix can be undone.</summary>
    private static bool Invertible(Matrix3x2 m) => Matrix3x2.Invert(m, out _);

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
        _ => null,
    };

    /// <summary>Whether a compositing operation is one this can draw (the others draw as source-over).</summary>
    public static bool IsKnownComposite(string value) =>
        BlendFor(value) is not null
        || value is "source-in" or "source-out" or "source-atop" or "destination-over" or "destination-in" or "destination-out" or "destination-atop" or "copy" or "xor";

    /// <summary>
    /// Keeps a drawing operation with the state it was made with. <paramref name="draw"/> runs with the canvas transformed by the transformation matrix of
    /// the moment, inside the clips then in force, in a layer when the alpha, blend mode or shadow ask for one.
    /// </summary>
    public void Record(Action<PeachDrawing.Core.Canvas> draw)
    {
        var state = State;
        var (clips, matrix) = (state.Clips.ToArray(), state.Transform);
        var alpha = state.GlobalAlpha;
        var blend = BlendFor(state.Composite) ?? PaintBlendMode.Normal;
        var shadow = state.ShadowColor.A > 0 && (state.ShadowBlur > 0 || state.ShadowOffsetX != 0 || state.ShadowOffsetY != 0)
            ? new DropShadowEffect(state.ShadowOffsetX, state.ShadowOffsetY, state.ShadowBlur / 2, state.ShadowBlur / 2, state.ShadowColor)
            : null;
        var (width, height) = (Width, Height);

        _ops.Add(canvas =>
        {
            foreach (var (clip, rule) in clips)
            {
                using var path = clip.Build(canvas, rule);
                canvas.PushClip(path);
            }

            try
            {
                CanvasLayer? layer = null;
                if (alpha < 1 || blend != PaintBlendMode.Normal || shadow is not null)
                {
                    var margin = shadow is null ? 0 : shadow.SigmaX * 3 + Math.Abs(shadow.OffsetX) + Math.Abs(shadow.OffsetY) + 2;
                    layer = canvas.BeginLayer(new LayerOptions(alpha, blend, null, new Rect(-margin, -margin, width + 2 * margin, height + 2 * margin), shadow is null ? null : [shadow]));
                }

                try
                {
                    var target = layer?.Canvas ?? canvas;
                    target.PushTransform(matrix);
                    try
                    {
                        draw(target);
                    }
                    finally
                    {
                        target.PopTransform();
                    }
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

    /// <summary>Starts over: everything drawn so far is dropped (what <c>clearRect</c> over the whole canvas comes to).</summary>
    public void ClearDrawing() => _ops.Clear();

    // ---- brushes and pens

    public static Brush MakeBrush(PaintSource source, double alpha = 1)
    {
        switch (source)
        {
            case PaintSource.ColorSource c:
                return new SolidBrush(alpha >= 1 ? c.Color : PaintColor.FromArgb((int)Math.Round(c.Color.A * alpha), c.Color.R, c.Color.G, c.Color.B));
            case PaintSource.GradientSource g:
                return MakeGradient(g.Gradient);
            default:
                return new SolidBrush(PaintColor.Transparent);
        }
    }

    private static Brush MakeGradient(CanvasGradient gradient)
    {
        var stops = gradient.Stops.OrderBy(s => s.Offset).ToList();
        if (stops.Count == 0)
            return new SolidBrush(PaintColor.Transparent);
        if (stops.Count == 1)
            return new SolidBrush(stops[0].Color);

        var list = stops.Select(s => new GradientStop(s.Color, s.Offset)).ToList();
        var g = gradient.Geometry;
        switch (gradient.Kind)
        {
            case "linear":
                return new LinearGradientBrush(new PaintPoint(g[0], g[1]), new PaintPoint(g[2], g[3]), list, GradientSpread.Pad);
            case "radial":
                return new RadialGradientBrush(new PaintPoint(g[3], g[4]), new PaintPoint(g[0], g[1]), g[5], g[5], list, GradientSpread.Pad) { FocusRadius = g[2] };
            default:
                var angles = stops.Select(s => g[0] + s.Offset * Math.PI * 2).ToList();
                return new ConicGradientBrush(new PaintPoint(g[1], g[2]), 1e6, list, angles);
        }
    }

    public Pen MakePen(CanvasState state)
    {
        var pen = new Pen
        {
            Paint = MakeBrush(state.StrokeStyle),
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

        Matrix3x2.Invert(State.Transform, out var inverse);
        var user = path.Transformed(inverse);
        var brush = State.FillStyle;
        var alpha = 1.0;
        Record(canvas =>
        {
            using var built = user.Build(canvas, rule);
            canvas.DrawPath(MakeBrush(brush, alpha), built);
        });
    }

    public void StrokePath(PathData path)
    {
        if (!Invertible(State.Transform) || path.Segments.Count == 0)
            return;

        Matrix3x2.Invert(State.Transform, out var inverse);
        var user = path.Transformed(inverse);
        var pen = MakePen(State);
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
        Record(canvas => canvas.DrawRectangle(MakeBrush(brush), Math.Min(x, x + w), Math.Min(y, y + h), Math.Abs(w), Math.Abs(h)));
    }

    public void StrokeRect(double x, double y, double w, double h)
    {
        if (w == 0 && h == 0)
            return;

        var pen = MakePen(State);
        Record(canvas => canvas.DrawRectangle(pen, Math.Min(x, x + w), Math.Min(y, y + h), Math.Abs(w), Math.Abs(h)));
    }

    public void ClearRect(double x, double y, double w, double h)
    {
        // Erasing part of the drawing has no vector form; clearing all of it, which is what animation loops do, does.
        var m = State.Transform;
        var corners = new[] { PathData.T(m, x, y), PathData.T(m, x + w, y), PathData.T(m, x, y + h), PathData.T(m, x + w, y + h) };
        if (m.M12 == 0 && m.M21 == 0 && State.Clips.Count == 0
            && corners.Min(c => c.X) <= 0 && corners.Min(c => c.Y) <= 0 && corners.Max(c => c.X) >= Width && corners.Max(c => c.Y) >= Height)
            ClearDrawing();
    }

    public void ClipPath(PathData path, FillMode rule) => State.Clips = [.. State.Clips, (path.Clone(), rule)];

    public void DrawImage(Image image, Rect source, Rect destination)
    {
        var sampling = State.ImageSmoothing ? ImageSampling.Automatic : ImageSampling.Pixelated;
        Record(canvas => canvas.DrawImage(image, destination, source, sampling));
    }

    public void DrawCanvas(CanvasBitmap other, Rect destination)
    {
        if (other.Width <= 0 || other.Height <= 0)
            return;

        var scale = Matrix3x2.CreateScale((float)(destination.Width / other.Width), (float)(destination.Height / other.Height)) * Matrix3x2.CreateTranslation((float)destination.X, (float)destination.Y);
        var painting = other.Painting;
        Record(canvas =>
        {
            canvas.PushTransform(scale);
            try
            {
                painting.Paint(canvas);
            }
            finally
            {
                canvas.PopTransform();
            }
        });
    }

    public void DrawText(string text, double x, double y, bool stroke, double? maxWidth, Font font, string align, string baseline, PaintSource fill, Pen? pen)
    {
        if (text.Length == 0)
            return;

        var width = MeasureText(text, font);
        var scale = maxWidth is { } max && width > max && max > 0 ? max / width : 1.0;
        var drawn = width * scale;
        var left = align switch
        {
            "center" => x - drawn / 2,
            "right" or "end" => x - drawn,
            _ => x,
        };

        var ascent = font.Ascent;
        var top = baseline switch
        {
            "top" => y,
            "hanging" => y - ascent * 0.2,
            "middle" => y - font.Height / 2,
            "bottom" or "ideographic" => y - font.Height,
            _ => y - ascent,
        };
        var baselineY = top + ascent;

        Record(canvas =>
        {
            if (scale != 1.0)
            {
                canvas.PushTransform(Matrix3x2.CreateScale((float)scale, 1) * Matrix3x2.CreateTranslation((float)(left * (1 - scale)), 0));
            }

            try
            {
                if (!stroke && fill is PaintSource.ColorSource color)
                {
                    canvas.DrawString(text, font, color.Color, new PaintPoint(left, top), new Size(width, font.Height));
                }
                else if (canvas.GetTextOutline(text, font, new PaintPoint(left, baselineY)) is { } outline)
                {
                    using (outline)
                    {
                        if (stroke && pen is not null)
                            canvas.DrawPath(pen, outline);
                        else
                            canvas.DrawPath(MakeBrush(fill), outline);
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

    public double MeasureText(string text, Font font)
    {
        using var measuring = Document.CreateMeasuringCanvas();
        return measuring is null ? text.Length * font.Size * 0.5 : measuring.MeasureString(text, font).Width;
    }

    /// <summary>The state's transform moved by a matrix applied first (what <c>transform()</c> and <c>translate()</c> do).</summary>
    public void Concat(Matrix3x2 matrix) => State.Transform = matrix * State.Transform;

    public static string ColorText(PaintColor color) =>
        color.A == 255
            ? $"#{color.R:x2}{color.G:x2}{color.B:x2}"
            : string.Create(CultureInfo.InvariantCulture, $"rgba({color.R}, {color.G}, {color.B}, {Math.Round(color.A / 255.0, 2)})");
}
