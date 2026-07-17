using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Network;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>
    /// A minimal, non-sealed <see cref="RAdapter"/> for tests that need to assert the exact
    /// <see cref="RColor"/> a shape/pen/brush was drawn with. The only concrete adapter in the
    /// product, <c>PdfSharpAdapter</c>, is <c>sealed</c>, and <see cref="RGraphics.GetSolidBrush"/>/
    /// <see cref="RGraphics.GetPen(RColor)"/> are non-virtual (they delegate to whichever
    /// <see cref="RAdapter"/> the graphics instance was constructed with) - so intercepting color at
    /// the brush/pen-creation boundary requires a real, separate <see cref="RAdapter"/> like this one,
    /// not a mock of <see cref="RGraphics"/> alone. Only <see cref="CreateSolidBrush"/>/
    /// <see cref="CreatePen(RColor)"/> have real behavior (returning the color-carrying
    /// <see cref="TestBrush"/>/<see cref="TestPen"/> below); everything else is a minimal stub, safe
    /// for the small, focused test documents this is used with - anything actually needed but not
    /// stubbed here will fail loudly rather than silently.
    /// </summary>
    internal sealed class TestGraphicsAdapter : RAdapter
    {
        public override RUri? BaseUri => null;

        public override string GetCssMediaType(IEnumerable<string> mediaTypesAvailable) => "print";

        public override Task<RNetworkResponse?> GetResourceStream(RUri uri) => Task.FromResult<RNetworkResponse?>(null);

        protected override RColor GetColorInt(string colorName) => RColor.Black;

        protected override RPen CreatePen(RColor color) => new TestPen(color);

        protected override RPen CreatePen(RBrush brush) => new TestPen(RColor.Black);

        protected override RBrush CreateSolidBrush(RColor color) => new TestBrush(color);

        protected override RBrush CreateLinearGradientBrush(RRect rect, RColor color1, RColor color2, double angle) => new TestBrush(color1);

        protected override RBrush CreateLinearGradientBrush(RPoint p1, RPoint p2, (RColor Color, double Position)[] stops, bool isRepeating = false) =>
            new TestBrush(stops.Length > 0 ? stops[0].Color : RColor.Black);

        protected override RBrush CreateRadialGradientBrush(RPoint center, double radiusX, double radiusY, (RColor Color, double Position)[] stops, bool isRepeating = false, RPoint? focalCenter = null) =>
            new TestBrush(stops.Length > 0 ? stops[0].Color : RColor.Black);

        protected override RBrush CreateConicGradientBrush(RPoint center, double outerRadius, RColor[] colors, double[] anglesRad) =>
            new TestBrush(colors.Length > 0 ? colors[0] : RColor.Black);

        protected override RImage ImageFromStreamInt(Stream memoryStream) => throw new NotSupportedException("TestGraphicsAdapter does not support image loading");

        protected override RFont CreateFontInt(string family, double size, RFontStyle style) => new TestFont(size);

        protected override RFont CreateFontInt(RFontFamily family, double size, RFontStyle style) => new TestFont(size);

        protected override Task AddFontFromStream(string fontFamilyName, Stream stream, string? format) => Task.CompletedTask;

        protected override Task<bool> AddLocalFont(string fontFamilyName, string localFontFaceName) => Task.FromResult(false);
    }

    /// <summary>A solid-color brush that remembers the color it was created with.</summary>
    internal sealed class TestBrush(RColor color) : RBrush
    {
        public RColor Color { get; } = color;
        public override void Dispose() { }
    }

    /// <summary>A pen that remembers the color/width/dash-style it was created with.</summary>
    internal sealed class TestPen(RColor color) : RPen
    {
        public RColor Color { get; } = color;
        public override double Width { get; set; }
        public RDashStyle RecordedDashStyle { get; private set; }
        public override RDashStyle DashStyle { set => RecordedDashStyle = value; }
        public override double MiterLimit { get; set; }
        public override RLineCap LineCap { set { } }
        public override RLineJoin LineJoin { set { } }
        public override void SetDashPattern(double[] pattern, double offset) { }
    }

    /// <summary>A deterministic fixed-metric font, independent of any real font file/rasterizer.</summary>
    internal sealed class TestFont(double size) : RFont
    {
        public override double Size => size;
        public override double Height => size * 1.2;
        public override double UnderlineOffset => size * 0.9;
        public override double Ascent => size * 0.8;
        public override double LeftPadding => 0;
        public override double GetWhitespaceWidth(RGraphics graphics) => size * 0.25;
    }

    /// <summary>No-op path builder - sufficient for shape markers, since color assertions only need
    /// <see cref="RGraphics.DrawPath(RBrush, RGraphicsPath)"/>/<see cref="RGraphics.DrawPath(RPen, RGraphicsPath)"/>
    /// to have been called with the right brush/pen, not the exact path geometry.</summary>
    internal sealed class TestGraphicsPath : RGraphicsPath
    {
        public override void Start(double x, double y) { }
        public override void LineTo(double x, double y) { }
        public override void ArcTo(double x, double y, double radiusX, double radiusY, Corner corner) { }
        public override void AddMove(double x, double y) { }
        public override void AddBezierTo(double x1, double y1, double x2, double y2, double x3, double y3) { }
        public override void AddArc(double x, double y, double radiusX, double radiusY, double rotationAngle, bool isLargeArc, bool sweepClockwise) { }
        public override void CloseFigure() { }
        public override RFillMode FillMode { get; set; }
        public override void Dispose() { }
    }

    /// <summary>
    /// Records painting calls into a single ordered log (per this repo's painting-test convention -
    /// see <c>CssLayoutEngineTablePageBreakTests.RecordingGraphics</c>), backed by
    /// <see cref="TestGraphicsAdapter"/> so brush/pen colors are introspectable via
    /// <see cref="TestBrush"/>/<see cref="TestPen"/>.
    /// </summary>
    internal sealed class TestRecordingGraphics : RGraphics
    {
        public sealed record DrawStringCall(string Text, RFont Font, RColor Color, RPoint Point, RSize Size, bool Rtl, double LetterSpacing = 0);
        public sealed record DrawRectCall(RColor Color, double X, double Y, double Width, double Height);
        public sealed record DrawPathCall(RColor Color);
        public sealed record DrawLineCall(RColor Color, double Width, RDashStyle DashStyle, double X1, double Y1, double X2, double Y2);
        public sealed record DrawPolygonCall(RColor Color, RPoint[] Points);

        public List<object> Log { get; } = [];
        public List<DrawStringCall> DrawStringCalls { get; } = [];

        public TestRecordingGraphics() : base(new TestGraphicsAdapter(), new RRect(0, 0, double.MaxValue, double.MaxValue)) { }

        public override void DrawString(string str, RFont font, RColor color, RPoint point, RSize size, bool rtl, double letterSpacing = 0)
        {
            var call = new DrawStringCall(str, font, color, point, size, rtl, letterSpacing);
            DrawStringCalls.Add(call);
            Log.Add(call);
        }

        public override void DrawRectangle(RBrush brush, double x, double y, double width, double height)
        {
            var color = brush is TestBrush tb ? tb.Color : RColor.Empty;
            Log.Add(new DrawRectCall(color, x, y, width, height));
        }

        public override void DrawRectangle(RPen pen, double x, double y, double width, double height)
        {
            var color = pen is TestPen tp ? tp.Color : RColor.Empty;
            Log.Add(new DrawRectCall(color, x, y, width, height));
        }

        public override void DrawPath(RBrush brush, RGraphicsPath path)
        {
            Log.Add(new DrawPathCall(brush is TestBrush tb ? tb.Color : RColor.Empty));
        }

        public override void DrawPath(RPen pen, RGraphicsPath path)
        {
            Log.Add(new DrawPathCall(pen is TestPen tp ? tp.Color : RColor.Empty));
        }

        public override void PushTransform(RMatrix matrix) { }
        public override void PopTransform() { }
        public override void PushClip(RRect rect) => _clipStack.Push(rect);
        public override void PushClip(RGraphicsPath path) => _clipStack.Push(_clipStack.Peek());
        public override void PopClip() { if (_clipStack.Count > 1) _clipStack.Pop(); }
        public override void PushClipExclude(RRect rect) { }
        public override object SetAntiAliasSmoothingMode() => new object();
        public override void ReturnPreviousSmoothingMode(object? prevMode) { }
        public override RGraphicsPath GetGraphicsPath() => new TestGraphicsPath();
        public override (RGraphics Graphics, RImage Image)? CreateTile(double width, double height) => null;
        public override void DrawImageMasked(RImage image, RImage maskImage, RRect destRect) { }
        public override void DrawImageWithOpacity(RImage image, RRect destRect, double opacity) { }
        public override void BeginMarkedContent(string structureType, int mcid) { }
        public override void EndMarkedContent() { }
        public override void BeginArtifact() { }
        public override RSize MeasureString(string str, RFont font) => new((str?.Length ?? 0) * font.Size * 0.6, font.Height);
        public override void MeasureString(string str, RFont font, double maxWidth, out int charFit, out double charFitWidth)
        {
            charFit = str?.Length ?? 0;
            charFitWidth = charFit * font.Size * 0.6;
        }
        public override void DrawLine(RPen pen, double x1, double y1, double x2, double y2)
        {
            var call = pen is TestPen tp
                ? new DrawLineCall(tp.Color, tp.Width, tp.RecordedDashStyle, x1, y1, x2, y2)
                : new DrawLineCall(RColor.Empty, 0, RDashStyle.Solid, x1, y1, x2, y2);
            Log.Add(call);
        }
        public override void DrawImage(RImage image, RRect destRect, RRect srcRect) { }
        public override void DrawImage(RImage image, RRect destRect) { }
        public override void DrawPolygon(RBrush brush, RPoint[] points)
        {
            var color = brush is TestBrush tb ? tb.Color : RColor.Empty;
            // _borderPts (BordersDrawHandler) is a shared, reused array - clone so this log entry
            // isn't silently mutated by later calls that overwrite the same backing array.
            Log.Add(new DrawPolygonCall(color, (RPoint[])points.Clone()));
        }
        public override void Dispose() { }
    }
}
