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

        // Only data: URIs get a real (dummy - see ImageFromStreamInt) resource stream, so tests that
        // need a replaced element (img/svg) to actually resolve an RImage can use one; every other
        // scheme still resolves to null, preserving the existing "no real network access" behavior
        // other tests rely on.
        public override Task<RNetworkResponse?> GetResourceStream(RUri uri) =>
            Task.FromResult(uri.Scheme == "data"
                ? new RNetworkResponse(new MemoryStream([0]), null)
                : null);

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

        // Real PNG/JPEG decoding is out of scope for this minimal stub - tests that need an actually-
        // loaded image (e.g. asserting a replaced element gets its own intrinsic size) only need SOME
        // deterministic, non-zero size to resolve, not faithful pixel decoding, so this ignores the
        // stream's real bytes and returns a fixed-size TestImage instead of throwing.
        protected override RImage ImageFromStreamInt(Stream memoryStream) => new TestImage(40, 30);

        protected override RFont CreateFontInt(string family, double size, RFontStyle style, int weight = 400, int stretch = 5, double? obliqueSkewSinus = null) => new TestFont(size);

        protected override RFont CreateFontInt(RFontFamily family, double size, RFontStyle style, int weight = 400, int stretch = 5, double? obliqueSkewSinus = null) => new TestFont(size);

        protected override Task<bool> AddFontFromStream(string fontFamilyName, Stream stream, string? format, int? weightOverride = null, bool? isItalicOverride = null, int? stretchOverride = null) => Task.FromResult(false);

        protected override Task<bool> AddLocalFont(string fontFamilyName, string localFontFaceName, int? weightOverride = null, bool? isItalicOverride = null, int? stretchOverride = null) => Task.FromResult(false);
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

    /// <summary>A fixed-size image, independent of any real pixel decoding - see ImageFromStreamInt's
    /// own comment for why TestGraphicsAdapter doesn't decode real image bytes.</summary>
    internal sealed class TestImage(double width, double height) : RImage
    {
        public override double Width => width;
        public override double Height => height;
        public override bool Interpolate { get; set; }
        public override void Dispose() { }
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
    /// <see cref="TestBrush"/>/<see cref="TestPen"/>. <see cref="PushClip(RRect)"/>/<see cref="PopClip"/>
    /// are also logged (as <see cref="PushClipCall"/>/<see cref="PopClipCall"/>) alongside the draw
    /// calls, not just tracked in the internal clip stack, so tests can assert clip pushes happen in
    /// the right order relative to specific draw calls (e.g. that a clip is pushed before the content
    /// it's meant to constrain) - per this class's own convention of extending the single ordered log
    /// for new call types rather than building a separate parallel mechanism.
    /// </summary>
    internal sealed class TestRecordingGraphics : RGraphics
    {
        public sealed record DrawStringCall(string Text, RFont Font, RColor Color, RPoint Point, RSize Size, bool Rtl, double LetterSpacing = 0);
        public sealed record DrawRectCall(RColor Color, double X, double Y, double Width, double Height);
        public sealed record DrawPathCall(RColor Color);
        public sealed record DrawLineCall(RColor Color, double Width, RDashStyle DashStyle, double X1, double Y1, double X2, double Y2);
        public sealed record DrawPolygonCall(RColor Color, RPoint[] Points);
        public sealed record PushClipCall(RRect Rect);
        public sealed record PopClipCall;
        public sealed record DrawImageCall(RImage Image, RRect DestRect);

        public List<object> Log { get; } = [];
        public List<DrawStringCall> DrawStringCalls { get; } = [];
        public List<DrawImageCall> DrawImageCalls { get; } = [];

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
        public override void PushClip(RRect rect)
        {
            _clipStack.Push(rect);
            Log.Add(new PushClipCall(rect));
        }
        public override void PushClip(RGraphicsPath path)
        {
            var rect = _clipStack.Peek();
            _clipStack.Push(rect);
            Log.Add(new PushClipCall(rect));
        }
        public override void PopClip()
        {
            if (_clipStack.Count > 1) _clipStack.Pop();
            Log.Add(new PopClipCall());
        }
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
        public override void DrawImage(RImage image, RRect destRect, RRect srcRect)
        {
            var call = new DrawImageCall(image, destRect);
            DrawImageCalls.Add(call);
            Log.Add(call);
        }
        public override void DrawImage(RImage image, RRect destRect)
        {
            var call = new DrawImageCall(image, destRect);
            DrawImageCalls.Add(call);
            Log.Add(call);
        }
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
