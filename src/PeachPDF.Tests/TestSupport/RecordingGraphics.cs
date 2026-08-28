using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Text;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>What kind of draw operation a <see cref="PaintOp"/> records.</summary>
    internal enum PaintOpKind
    {
        FillRect,
        Polygon,
        Line,
        PushClip,
        PushClipPath,
        PopClip,
        PushTransform,
        PopTransform,
        DrawString
    }

    /// <summary>One paint call, in the order it happened - the single ordered log <see cref="RecordingGraphics.Log"/> keeps, per this repo's own preference (CLAUDE.md's testing conventions) for one ordered log over parallel per-call-type counts/lists when order across different call types matters. <see cref="Matrix"/> is set only for <see cref="PaintOpKind.PushTransform"/>, <see cref="Text"/> only for <see cref="PaintOpKind.DrawString"/>.</summary>
    internal readonly record struct PaintOp(PaintOpKind Kind, RRect Bounds, RMatrix? Matrix = null, string? Text = null);

    /// <summary>
    /// Minimal <see cref="RGraphics"/> implementation that records paint calls so tests can verify paint
    /// order/behavior without a full PDF rendering stack. Shared across test files (originally private to
    /// <c>CssLayoutEngineTablePageBreakTests</c>) - extend this one rather than adding a parallel copy.
    /// </summary>
    internal sealed class RecordingGraphics : RGraphics
    {
        /// <summary>Every fill/stroke/clip operation, in the order it was made.</summary>
        public List<PaintOp> Log { get; } = [];

        /// <summary>All rects passed to PushClip during this paint pass.</summary>
        public List<RRect> PushedClips { get; } = [];

        /// <summary>
        /// Every rounded-path clip pushed via <see cref="PushClip(RGraphicsPath)"/>, in order - lets a
        /// test inspect the actual corner radii a clip curve was built with (see
        /// <see cref="RecordingGraphicsPath.Arcs"/>), not just that a path clip happened.
        /// </summary>
        public List<RecordingGraphicsPath> PushedClipPaths { get; } = [];

        /// <summary>
        /// Every path filled via <see cref="DrawPath(RBrush, RGraphicsPath)"/>, in order - the rounded
        /// curve a <c>background-clip: padding-box</c>/<c>content-box</c> fill was clipped to.
        /// </summary>
        public List<RecordingGraphicsPath> DrawnPaths { get; } = [];

        /// <summary>
        /// Every path stroked via <see cref="DrawPath(RPen, RGraphicsPath)"/>, in order - the rounded
        /// border-stroke curve <c>BordersDrawHandler.GetRoundedBorderPath</c> builds (issue #812's
        /// second, independent rounded-path builder; a rounded border always strokes via the <c>RPen</c>
        /// overload, never the <c>RBrush</c> one <see cref="DrawnPaths"/> tracks).
        /// </summary>
        public List<RecordingGraphicsPath> StrokedPaths { get; } = [];

        /// <summary>Total PushClip invocations.</summary>
        public int PushCount { get; private set; }

        /// <summary>Total PopClip invocations.</summary>
        public int PopCount { get; private set; }

        /// <summary>Y-coordinates of horizontal lines drawn (where y1 ≈ y2).</summary>
        public List<double> HorizontalLines { get; } = [];

        /// <summary>Every word string passed to DrawString during this paint pass, with the Y it was drawn at.</summary>
        public List<(string Text, double Y)> DrawnStrings { get; } = [];

        /// <summary>
        /// Settable so a test can exercise a non-default <c>PixelsPerPoint</c> (issue #814) without a
        /// full <c>GraphicsAdapter</c>/PDF stack - defaults to the base <see cref="RGraphics.PixelsPerPoint"/>
        /// no-op of <c>1.0</c>. A field-backed override (rather than an auto-property) since the base
        /// virtual member is get-only.
        /// </summary>
        public double PixelsPerPointOverride { get; set; } = 1.0;

        public override double PixelsPerPoint => PixelsPerPointOverride;

        public RecordingGraphics(RAdapter adapter)
            : base(adapter, new RRect(0, 0, double.MaxValue, double.MaxValue)) { }

        public override void DrawLine(RPen pen, double x1, double y1, double x2, double y2)
        {
            if (Math.Abs(y1 - y2) < 0.5)
                HorizontalLines.Add(y1);

            var left = Math.Min(x1, x2);
            var top = Math.Min(y1, y2);
            Log.Add(new PaintOp(PaintOpKind.Line, new RRect(left, top, Math.Abs(x2 - x1), Math.Abs(y2 - y1))));
        }

        /// <summary>
        /// Solid borders paint as a mitered quad (BordersDrawHandler.SetInOutsetRectanglePoints),
        /// not a DrawLine call - a wide-and-thin quad (wider in X than in Y) is a horizontal border
        /// stripe; record its vertical center the same way DrawLine's y1/y2 would, so callers don't
        /// need to know which draw method a given border style happens to use.
        /// </summary>
        public override void DrawPolygon(RBrush brush, RPoint[] points)
        {
            if (points.Length == 0) return;

            var minY = points.Min(p => p.Y);
            var maxY = points.Max(p => p.Y);
            var minX = points.Min(p => p.X);
            var maxX = points.Max(p => p.X);

            if (maxX - minX > maxY - minY)
                HorizontalLines.Add((minY + maxY) / 2);

            Log.Add(new PaintOp(PaintOpKind.Polygon, new RRect(minX, minY, maxX - minX, maxY - minY)));
        }

        public override void PushClip(RRect rect)
        {
            _clipStack.Push(rect);
            PushedClips.Add(rect);
            PushCount++;
            Log.Add(new PaintOp(PaintOpKind.PushClip, rect));
        }

        public override void PopClip()
        {
            if (_clipStack.Count > 1)
                _clipStack.Pop();
            PopCount++;
            Log.Add(new PaintOp(PaintOpKind.PopClip, default));
        }

        /// <summary>
        /// A path clip (the rounded-corner curve of a `border-radius` + `overflow: hidden` box, e.g.)
        /// doesn't have a rectangular bound this mock can track precisely, so - like the real
        /// GraphicsAdapter's own clip-stack bookkeeping (RenderUtils.cs remarks) - it keeps the
        /// enclosing rect on the tracked stack rather than attempting one. It still logs and counts the
        /// push/pop like any other clip, so PushCount/PopCount balance and Log preserves push order
        /// (rect clip, then path clip) for a caller asserting on it.
        /// </summary>
        public override void PushClip(RGraphicsPath path)
        {
            _clipStack.Push(_clipStack.Peek());
            PushCount++;
            if (path is RecordingGraphicsPath recordingPath) PushedClipPaths.Add(recordingPath);
            Log.Add(new PaintOp(PaintOpKind.PushClipPath, default));
        }

        public override void PushClipExclude(RRect rect) { }

        public override void PushTransform(RMatrix matrix) =>
            Log.Add(new PaintOp(PaintOpKind.PushTransform, default, Matrix: matrix));

        public override void PopTransform() => Log.Add(new PaintOp(PaintOpKind.PopTransform, default));

        public override void PushBlendMode(RBlendMode mode) { }
        public override void PopBlendMode() { }
        public override object SetAntiAliasSmoothingMode() => new object();
        public override void ReturnPreviousSmoothingMode(object? prevMode) { }
        /// <summary>
        /// Overrides the default fixed <c>(0, 12)</c> <see cref="MeasureString(string, RFont, TextShapingFeatures?)"/>
        /// result when set - a test whose subject genuinely depends on relative string widths (e.g.
        /// text-overflow's own "does this word/substring still fit" comparisons) can supply a
        /// deterministic, length-sensitive measurement instead of the fixed default every other
        /// consumer of this mock relies on staying zero. Null (the default) preserves the original
        /// behavior exactly.
        /// </summary>
        public Func<string, RFont, TextShapingFeatures?, RSize>? MeasureStringOverride { get; set; }

        public override RSize MeasureString(string str, RFont font, TextShapingFeatures? features = null) =>
            MeasureStringOverride?.Invoke(str, font, features) ?? new RSize(0, 12);
        public override int CountShapedGlyphs(string str, RFont font, TextShapingFeatures? features = null) => str?.Length ?? 0;
        public override void MeasureString(string str, RFont font, double maxWidth, out int charFit, out double charFitWidth) { charFit = str?.Length ?? 0; charFitWidth = 0; }

        public override void DrawString(string str, RFont font, RColor color, RPoint point, RSize size, double letterSpacing = 0, RFontPalette? fontPalette = null, TextShapingFeatures? features = null)
        {
            DrawnStrings.Add((str, point.Y));
            Log.Add(new PaintOp(PaintOpKind.DrawString, new RRect(point.X, point.Y, size.Width, size.Height), Text: str));
        }

        public override void DrawRectangle(RPen pen, double x, double y, double width, double height) { }

        public override void DrawRectangle(RBrush brush, double x, double y, double width, double height) =>
            Log.Add(new PaintOp(PaintOpKind.FillRect, new RRect(x, y, width, height)));

        public override void DrawImage(RImage image, RRect destRect, RRect srcRect) { }
        public override void DrawImage(RImage image, RRect destRect) { }
        public override void DrawPath(RPen pen, RGraphicsPath path)
        {
            if (path is RecordingGraphicsPath recordingPath) StrokedPaths.Add(recordingPath);
        }

        public override void DrawPath(RBrush brush, RGraphicsPath path)
        {
            if (path is RecordingGraphicsPath recordingPath) DrawnPaths.Add(recordingPath);
        }
        public override RGraphicsPath GetGraphicsPath() => new RecordingGraphicsPath();

        public override RGraphicsPath? GetTextOutline(string str, RFont font, RPoint baselineOrigin, double letterSpacing = 0, TextShapingFeatures? features = null) => null;
        public override (RGraphics Graphics, RImage Image)? CreateTile(double width, double height) => null;
        public override void DrawImageMasked(RImage image, RImage maskImage, RRect destRect) { }
        public override void DrawImageWithOpacity(RImage image, RRect destRect, double opacity) { }
        public override void BeginMarkedContent(string structureType, int mcid) { }
        public override void EndMarkedContent() { }
        public override void BeginArtifact() { }
        public override void Dispose() { }
    }

    internal sealed class RecordingGraphicsPath : RGraphicsPath
    {
        /// <summary>Whether <see cref="CloseFigure"/> was called - lets a test verify a path-building
        /// method (e.g. <c>RenderUtils.GetRoundRect</c>) explicitly closes its subpath rather than
        /// relying on the last segment's endpoint coinciding with the first by floating-point luck.</summary>
        public bool Closed { get; private set; }

        /// <summary>Every corner arc added via <see cref="ArcTo"/>, in order - lets a test read back the
        /// actual per-corner radius a rounded-rect path (<c>RenderUtils.GetRoundRect</c>) was built
        /// with, e.g. to confirm a padding-/content-edge curve was reduced per CSS Backgrounds and
        /// Borders Level 3 §5.5 rather than using the box's raw declared radius.</summary>
        public List<(Corner Corner, double RadiusX, double RadiusY)> Arcs { get; } = [];

        /// <summary>Every <see cref="Start"/>/<see cref="LineTo"/>/<see cref="ArcTo"/> endpoint, in order -
        /// lets a test read back the path's actual position/size (not just its corner radii), e.g. to
        /// confirm a rounded-rect path's coordinates came out divided by <c>PixelsPerPoint</c> (issue
        /// #812), not just its radii.</summary>
        public List<(double X, double Y)> Points { get; } = [];

        public override void Start(double x, double y) => Points.Add((x, y));
        public override void LineTo(double x, double y) => Points.Add((x, y));
        public override void ArcTo(double x, double y, double radiusX, double radiusY, Corner corner)
        {
            Points.Add((x, y));
            Arcs.Add((corner, radiusX, radiusY));
        }
        public override void AddMove(double x, double y) { }
        public override void AddBezierTo(double x1, double y1, double x2, double y2, double x3, double y3) { }
        public override void AddArc(double x, double y, double radiusX, double radiusY, double rotationAngle, bool isLargeArc, bool sweepClockwise) { }
        public override void CloseFigure() => Closed = true;
        public override void Transform(RMatrix matrix) { }
        public override void AddPath(RGraphicsPath path) { }
        public override RFillMode FillMode { get; set; }
        public override void Dispose() { }
    }
}
