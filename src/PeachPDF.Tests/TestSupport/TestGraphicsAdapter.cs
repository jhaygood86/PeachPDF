using PeachDrawing.Text.Shaping;
using PeachDrawing.Text;
using PeachDrawing.Text.Unicode;
using PeachDrawing.Text.Internal.Fonts;
using PeachDrawing.Text.Internal.Text;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Network;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
    internal class TestGraphicsAdapter : RAdapter
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
            new TestBrush(stops.Length > 0 ? stops[0].Color : RColor.Black) { GradientStart = p1, GradientEnd = p2 };

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

        protected override RFont? CreateFontForCodepointInt(string family, double size, RFontStyle style, int weight, int stretch, double? obliqueSkewSinus, System.Text.Rune codepoint) => new TestFont(size);

        // No family this stub knows about ever "wins" the last-resort search - there is no real
        // InstalledFonts registry backing it, so the only faithful answer is "nothing found".
        protected override RFont? CreateSystemFallbackFontForCodepointInt(double size, RFontStyle style, int weight, int stretch, double? obliqueSkewSinus, System.Text.Rune codepoint, PeachDrawing.Text.Unicode.EmojiPresentation presentation) => null;

        protected override bool FamilyHasExplicitUnicodeRangesInt(string family) => false;

        protected override Task<bool> AddFontFromStream(string fontFamilyName, Stream stream, string? format, int? weightOverride = null, bool? isItalicOverride = null, int? stretchOverride = null, IReadOnlyList<PeachDrawing.Text.RuneInterval>? unicodeRanges = null) => Task.FromResult(false);

        protected override Task<bool> AddLocalFont(string fontFamilyName, string localFontFaceName, int? weightOverride = null, bool? isItalicOverride = null, int? stretchOverride = null, IReadOnlyList<PeachDrawing.Text.RuneInterval>? unicodeRanges = null) => Task.FromResult(false);
    }

    /// <summary>A solid-color brush that remembers the color it was created with, and (for a linear
    /// gradient) the endpoints it was resolved against - lets a test assert the actual gradient span
    /// reflects the objectBoundingBox it was sized from, not just the fill's first stop color.</summary>
    internal sealed class TestBrush(RColor color) : RBrush
    {
        public RColor Color { get; } = color;
        public RPoint? GradientStart { get; init; }
        public RPoint? GradientEnd { get; init; }
        public override void Dispose() { }
    }

    /// <summary>
    /// A pen that remembers the color/width/dash-style it was created with, plus the line cap and any
    /// explicit dash pattern. Those last two are what distinguish a dotted border from a dashed one now
    /// that both are expressed as a fitted <see cref="RPen.SetDashPattern"/> rather than a canned
    /// <see cref="RDashStyle"/> - a dot is a zero-length dash under a round cap, so a test that only
    /// looked at <see cref="RecordedDashStyle"/> could not tell a circle from a square.
    /// </summary>
    internal sealed class TestPen(RColor color) : RPen
    {
        public RColor Color { get; } = color;
        public override double Width { get; set; }
        public RDashStyle RecordedDashStyle { get; private set; }

        public override RDashStyle DashStyle
        {
            set
            {
                RecordedDashStyle = value;
                RecordedDashPattern = null;
            }
        }

        public override double MiterLimit { get; set; }
        public RLineCap RecordedLineCap { get; private set; } = RLineCap.Butt;
        public override RLineCap LineCap { set => RecordedLineCap = value; }
        public override RLineJoin LineJoin { set { } }

        /// <summary>The dash array of the last <see cref="SetDashPattern"/>, or null if the pen is on a
        /// canned <see cref="RDashStyle"/> instead. In the same absolute units as <see cref="Width"/>.</summary>
        public double[]? RecordedDashPattern { get; private set; }

        public double RecordedDashOffset { get; private set; }

        public override void SetDashPattern(double[] pattern, double offset)
        {
            RecordedDashPattern = (double[])pattern.Clone();
            RecordedDashOffset = offset;
            RecordedDashStyle = RDashStyle.Custom;
        }
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
    internal class TestFont(double size) : RFont
    {
        public override double Size => size;
        public override double Height => size * 1.2;
        public override double UnderlineOffset => size * 0.9;
        public override double Ascent => size * 0.8;
        public override double LeftPadding => 0;
        public override double GetWhitespaceWidth(RGraphics graphics) => size * 0.25;
        public override bool HasGlyph(System.Text.Rune rune) => true;
        public override bool SupportsFontVariantCaps(CapsMode feature) => false;

        public override bool SupportsFontVariantPosition(SubSuperMode feature) => false;

        public override (double SizeScale, double BaselineShift)? GetSubSuperscriptMetrics(bool superscript) => null;
        public override string FaceKey => "test";
    }

    /// <summary>Records every point added to the path (moves, line endpoints, Bézier control/end
    /// points, arc endpoints) so tests can assert the resulting clip/paint geometry - e.g. that a
    /// clip shape's <c>transform</c> was baked into its coordinates (issue #169). <see cref="Transform"/>
    /// applies the matrix to every recorded point (same convention as the real
    /// <c>GraphicsPathAdapter</c>/<c>XMatrix</c>) and <see cref="AddPath"/> merges another path's
    /// recorded points, faithfully mirroring the production path so a test double never diverges from
    /// what actually renders. <see cref="SubpathStarts"/> additionally records where each subpath
    /// begins, which is the only way to tell one shape from several in a single path - an outline
    /// unioned over several fragments emits every disjoint piece of the region as its own subpath of
    /// one fill call, so the merged point list alone cannot distinguish that from one connected
    /// shape.</summary>
    internal sealed class TestGraphicsPath : RGraphicsPath
    {
        public List<RPoint> Points { get; } = [];

        /// <summary>The index into <see cref="Points"/> at which each subpath begins.</summary>
        public List<int> SubpathStarts { get; } = [];

        /// <summary>
        /// The indices into <see cref="Points"/> of the two control points of each Bézier. They are
        /// the only recorded points the path does not pass through, so a test that treats the point
        /// list as the shape - asking whether a clip covers it, say - has to know to skip them or to
        /// flatten the curve through them, rather than assert against a point the curve only leans
        /// towards.
        /// </summary>
        public HashSet<int> BezierControlPoints { get; } = [];

        public override void Start(double x, double y)
        {
            SubpathStarts.Add(Points.Count);
            Points.Add(new RPoint(x, y));
        }

        public override void LineTo(double x, double y) => Points.Add(new RPoint(x, y));
        public override void ArcTo(double x, double y, double radiusX, double radiusY, Corner corner) => Points.Add(new RPoint(x, y));

        public override void AddMove(double x, double y)
        {
            SubpathStarts.Add(Points.Count);
            Points.Add(new RPoint(x, y));
        }

        public override void AddBezierTo(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            BezierControlPoints.Add(Points.Count);
            BezierControlPoints.Add(Points.Count + 1);
            Points.Add(new RPoint(x1, y1));
            Points.Add(new RPoint(x2, y2));
            Points.Add(new RPoint(x3, y3));
        }

        public override void AddArc(double x, double y, double radiusX, double radiusY, double rotationAngle, bool isLargeArc, bool sweepClockwise) => Points.Add(new RPoint(x, y));
        public override void CloseFigure() { }

        public override void Transform(RMatrix matrix)
        {
            for (var i = 0; i < Points.Count; i++)
            {
                var p = Points[i];
                Points[i] = new RPoint(
                    p.X * matrix.M11 + p.Y * matrix.M21 + matrix.OffsetX,
                    p.X * matrix.M12 + p.Y * matrix.M22 + matrix.OffsetY);
            }
        }

        public override void AddPath(RGraphicsPath path)
        {
            var other = (TestGraphicsPath)path;
            foreach (var start in other.SubpathStarts) SubpathStarts.Add(Points.Count + start);
            foreach (var control in other.BezierControlPoints) BezierControlPoints.Add(Points.Count + control);
            Points.AddRange(other.Points);
        }

        public override RFillMode FillMode { get; set; }

        /// <summary>Test-double approximation of <see cref="RGraphicsPath.ClipToRect"/>: since this
        /// double records a flat point list rather than real subpath/contour structure, it can't
        /// reproduce the real Sutherland-Hodgman clip - it just keeps whichever recorded points already
        /// fall inside <paramref name="rect"/>, which is enough for tests that use this double and don't
        /// assert on rectangle-clip geometry specifically (see <c>GraphicsPathAdapter.ClipToRect</c> for
        /// the real implementation).</summary>
        public override RGraphicsPath ClipToRect(RRect rect)
        {
            var clipped = new TestGraphicsPath { FillMode = FillMode };
            clipped.Points.AddRange(Points.Where(p => p.X >= rect.Left && p.X <= rect.Right && p.Y >= rect.Top && p.Y <= rect.Bottom));
            return clipped;
        }

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
    internal class TestRecordingGraphics : RGraphics
    {
        public sealed record DrawStringCall(string Text, RFont Font, RColor Color, RPoint Point, RSize Size, double LetterSpacing = 0, ShapeSettings? Features = null, string? LogicalText = null);
        public sealed record DrawRectCall(RColor Color, double X, double Y, double Width, double Height);
        /// <summary>
        /// A filled or stroked path. <see cref="Points"/> is the path's recorded geometry, which is the only
        /// way to assert on a rounded shape: a <c>border-radius</c> background takes this route rather than
        /// <see cref="DrawRectCall"/>, so its rectangle would otherwise be invisible to a test. Copied at
        /// record time — <see cref="TestGraphicsPath.Transform"/> mutates in place.
        /// </summary>
        public sealed record DrawPathCall(RColor Color, IReadOnlyList<RPoint> Points)
        {
            /// <summary>The axis-aligned bounds of the recorded points, or empty when there are none.</summary>
            public RRect Bounds => Points.Count == 0
                ? RRect.Empty
                : RRect.FromLTRB(Points.Min(p => p.X), Points.Min(p => p.Y), Points.Max(p => p.X), Points.Max(p => p.Y));

            /// <summary>
            /// The index into <see cref="Points"/> at which each of this path's subpaths begins - see
            /// <see cref="TestGraphicsPath.SubpathStarts"/>. One fill call can hold several disjoint
            /// shapes, so this is what distinguishes "one connected shape" from "three separate ones".
            /// </summary>
            public IReadOnlyList<int> SubpathStarts { get; init; } = [];

            /// <summary>
            /// The indices into <see cref="Points"/> that are Bézier control points - see
            /// <see cref="TestGraphicsPath.BezierControlPoints"/>. A test asking whether the shape
            /// lies somewhere needs these to tell the points the path passes through from the two
            /// per curve that it only leans towards.
            /// </summary>
            public IReadOnlySet<int> BezierControlPoints { get; init; } = new HashSet<int>();

            /// <summary>The points making up subpath <paramref name="index"/>.</summary>
            public IReadOnlyList<RPoint> Subpath(int index)
            {
                var start = SubpathStarts[index];
                var end = index + 1 < SubpathStarts.Count ? SubpathStarts[index + 1] : Points.Count;
                return [.. Points.Skip(start).Take(end - start)];
            }

            /// <summary>The axis-aligned bounds of subpath <paramref name="index"/>.</summary>
            public RRect SubpathBounds(int index)
            {
                var points = Subpath(index);
                return points.Count == 0
                    ? RRect.Empty
                    : RRect.FromLTRB(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));
            }

            /// <summary>The linear gradient endpoints this fill was resolved against (null for a solid
            /// fill/stroke, or a non-gradient brush) - see <see cref="TestBrush.GradientStart"/>.</summary>
            public RPoint? GradientStart { get; init; }
            public RPoint? GradientEnd { get; init; }

            /// <summary>True when the path was stroked with a pen rather than filled with a brush.</summary>
            public bool Stroked { get; init; }
            public double StrokeWidth { get; init; }
            public RLineCap LineCap { get; init; }
            public IReadOnlyList<double>? DashPattern { get; init; }
            public double DashOffset { get; init; }
        }
        /// <param name="LineCap">
        /// The pen's cap AS OF this call. Load-bearing for dotted styles: the dot itself is a
        /// zero-length dash, so it is the round cap - not the dash array - that makes it a circle
        /// rather than nothing at all.
        /// </param>
        /// <param name="DashPattern">
        /// The explicit dash array (absolute units, same as <paramref name="Width"/>) when the pen was
        /// given one, else null. A fitted border/outline pattern always takes this route, so
        /// <paramref name="DashStyle"/> alone no longer says what a dotted/dashed edge looks like.
        /// </param>
        public sealed record DrawLineCall(
            RColor Color, double Width, RDashStyle DashStyle, double X1, double Y1, double X2, double Y2,
            RLineCap LineCap = RLineCap.Butt, IReadOnlyList<double>? DashPattern = null);
        public sealed record DrawPolygonCall(RColor Color, RPoint[] Points);
        public sealed record PushClipCall(RRect Rect);
        public sealed record PopClipCall;
        /// <param name="SrcRect">
        /// The portion of the source the call asked for, in the image's own natural units - null for the
        /// whole-image overload. Recorded because a border-image slice is defined entirely by WHICH part of
        /// the source it draws: a mock that only kept the destination could not tell a correct 9-slice from
        /// one drawing the whole image into every region, which is exactly how that shipped once.
        /// </param>
        /// <param name="Interpolate">
        /// The image's smoothing flag AS OF this call - it is toggled around a draw and restored
        /// afterwards, so reading it off the image once painting is over says nothing about what the draw
        /// actually asked for.
        /// </param>
        public sealed record DrawImageCall(RImage Image, RRect DestRect, RRect? SrcRect = null, bool Interpolate = false);
        public sealed record PushBlendModeCall(RBlendMode Mode);
        public sealed record PopBlendModeCall;
        public sealed record PushTransformCall(RMatrix Matrix);
        public sealed record PopTransformCall;

        public List<object> Log { get; } = [];
        public List<DrawStringCall> DrawStringCalls { get; } = [];
        public List<DrawImageCall> DrawImageCalls { get; } = [];

        /// <summary>The path passed to each <see cref="PushClip(RGraphicsPath)"/> call, in order, so
        /// tests can inspect the resulting clip geometry (e.g. a transformed clip region).</summary>
        public List<TestGraphicsPath> ClipPaths { get; } = [];

        /// <summary>
        /// Settable so a test can exercise a non-default <c>PixelsPerPoint</c> (issue #812/#814) without a
        /// full <c>GraphicsAdapter</c>/PDF stack - defaults to the base <see cref="RGraphics.PixelsPerPoint"/>
        /// no-op of <c>1.0</c>. A field-backed override (rather than an auto-property) since the base
        /// virtual member is get-only. Mirrors <c>RecordingGraphics.PixelsPerPointOverride</c>.
        /// </summary>
        public double PixelsPerPointOverride { get; set; } = 1.0;

        public override double PixelsPerPoint => PixelsPerPointOverride;

        public TestRecordingGraphics() : base(new TestGraphicsAdapter(), new RRect(0, 0, double.MaxValue, double.MaxValue)) { }

        public override void DrawString(string str, RFont font, RColor color, RPoint point, RSize size, double letterSpacing = 0, RFontPalette? fontPalette = null, ShapeSettings? features = null)
        {
            var call = new DrawStringCall(str, font, color, point, size, letterSpacing, features);
            DrawStringCalls.Add(call);
            Log.Add(call);
        }

        public override void DrawGlyphs(IReadOnlyList<GlyphPlacement> glyphs, RFont font, RColor color) { }

        /// <summary>See <see cref="RGraphics.DrawString(string, RFont, RColor, RPoint, RSize, double, RFontPalette?, ShapeSettings?, string?)"/>'s
        /// own remarks for <paramref name="logicalText"/>. Dispatches through the virtual 8-arg overload
        /// first (rather than recording independently) so a subclass that overrides only that one (e.g.
        /// <c>RenderErrorReportingTests.ThrowingGraphics</c>) still intercepts every call made through
        /// this overload too - then attaches <paramref name="logicalText"/> to the record that call just
        /// appended.</summary>
        public override void DrawString(string str, RFont font, RColor color, RPoint point, RSize size, double letterSpacing, RFontPalette? fontPalette, ShapeSettings? features, string? logicalText)
        {
            DrawString(str, font, color, point, size, letterSpacing, fontPalette, features);
            if (logicalText is null) return;

            var withLogicalText = DrawStringCalls[^1] with { LogicalText = logicalText };
            DrawStringCalls[^1] = withLogicalText;
            Log[^1] = withLogicalText;
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
            var tb = brush as TestBrush;
            Log.Add(new DrawPathCall(tb?.Color ?? RColor.Empty, PointsOf(path)) { GradientStart = tb?.GradientStart, GradientEnd = tb?.GradientEnd, SubpathStarts = SubpathStartsOf(path), BezierControlPoints = BezierControlPointsOf(path) });
        }

        public override void DrawPath(RPen pen, RGraphicsPath path)
        {
            var testPen = pen as TestPen;
            Log.Add(new DrawPathCall(testPen?.Color ?? RColor.Empty, PointsOf(path))
            {
                Stroked = true,
                StrokeWidth = testPen?.Width ?? 0,
                LineCap = testPen?.RecordedLineCap ?? RLineCap.Butt,
                DashPattern = testPen?.RecordedDashPattern,
                DashOffset = testPen?.RecordedDashOffset ?? 0,
                SubpathStarts = SubpathStartsOf(path),
                BezierControlPoints = BezierControlPointsOf(path)
            });
        }

        private static IReadOnlySet<int> BezierControlPointsOf(RGraphicsPath path) =>
            path is TestGraphicsPath testPath
                ? new HashSet<int>(testPath.BezierControlPoints)
                : new HashSet<int>();

        private static IReadOnlyList<RPoint> PointsOf(RGraphicsPath path) =>
            path is TestGraphicsPath testPath ? testPath.Points.ToArray() : [];

        private static IReadOnlyList<int> SubpathStartsOf(RGraphicsPath path) =>
            path is TestGraphicsPath testPath ? testPath.SubpathStarts.ToArray() : [];

        /// <summary>One filled shape, whichever primitive produced it.</summary>
        public sealed record FilledShape(RColor Color, RRect Bounds);

        /// <summary>
        /// Every filled shape in order, whether it arrived as a polygon or as a filled path.
        /// </summary>
        /// <remarks>
        /// A border paints as four mitred polygons when its edges differ, but as a single closed ring
        /// path when all four share a style and color (<c>BoxEdgesDrawHandler</c>'s uniform fast path -
        /// abutting polygons leave a pale antialiasing seam along every mitre, a ring has no seam). A
        /// test asking "did this border paint", or counting how many fills it made, should read this
        /// rather than <see cref="DrawPolygonCall"/> alone, which would otherwise silently see nothing
        /// for the commonest border there is.
        /// </remarks>
        public IEnumerable<FilledShape> FilledShapes =>
            Log.Select(entry => entry switch
                {
                    DrawPolygonCall p when p.Points.Length > 0 =>
                        new FilledShape(p.Color, RRect.FromLTRB(
                            p.Points.Min(pt => pt.X), p.Points.Min(pt => pt.Y),
                            p.Points.Max(pt => pt.X), p.Points.Max(pt => pt.Y))),
                    DrawPathCall { Stroked: false, Points.Count: > 0 } p => new FilledShape(p.Color, p.Bounds),
                    _ => null
                })
                .Where(shape => shape is not null)
                .Select(shape => shape!);

        /// <summary>Every matrix pushed, in order - convenience shortcut for tests that only need the
        /// matrices/count without filtering <see cref="Log"/> themselves (e.g. asserting a rotation
        /// happened). Also recorded into <see cref="Log"/> (as <see cref="PushTransformCall"/>/
        /// <see cref="PopTransformCall"/>, matching <see cref="PushClipCall"/>/<see cref="PopClipCall"/>'s
        /// own dual-tracking shape) so tests can assert push-before-draw-before-pop ordering per this
        /// repo's own painting-test convention, not just aggregate counts.</summary>
        public List<RMatrix> PushTransformCalls { get; } = [];
        public int PushTransformCount => PushTransformCalls.Count;
        public int PopTransformCount { get; private set; }

        public override void PushTransform(RMatrix matrix)
        {
            PushTransformCalls.Add(matrix);
            Log.Add(new PushTransformCall(matrix));
        }
        public override void PopTransform()
        {
            PopTransformCount++;
            Log.Add(new PopTransformCall());
        }
        public override void PushClip(RRect rect)
        {
            _clipStack.Push(rect);
            Log.Add(new PushClipCall(rect));
        }
        public override void PushClip(RGraphicsPath path)
        {
            if (path is TestGraphicsPath testPath)
                ClipPaths.Add(testPath);
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
        public override void PushBlendMode(RBlendMode mode) => Log.Add(new PushBlendModeCall(mode));
        public override void PopBlendMode() => Log.Add(new PopBlendModeCall());
        public override object SetAntiAliasSmoothingMode() => new object();
        public override void ReturnPreviousSmoothingMode(object? prevMode) { }
        public override RGraphicsPath GetGraphicsPath() => new TestGraphicsPath();

        /// <summary>
        /// A synthetic glyph-run outline: one box subpath per non-whitespace rune, advancing along the
        /// baseline. Faithful to the real <see cref="GraphicsAdapter.GetTextOutline"/> contract
        /// (non-null exactly when there is fillable geometry, null for a whitespace-only/empty run)
        /// without needing a real <c>glyf</c> font, so SVG gradient/pattern-fill and stroke-on-text
        /// paths can be driven and their <see cref="DrawPath(RBrush, RGraphicsPath)"/>/
        /// <see cref="DrawPath(RPen, RGraphicsPath)"/> calls recorded.
        /// </summary>
        public override RGraphicsPath? GetTextOutline(string str, RFont font, RPoint baselineOrigin, double letterSpacing = 0, ShapeSettings? features = null)
        {
            var path = new TestGraphicsPath();
            var penX = baselineOrigin.X;
            var any = false;

            foreach (var rune in str.EnumerateRunes())
            {
                if (!System.Text.Rune.IsWhiteSpace(rune))
                {
                    var top = baselineOrigin.Y - font.Ascent;
                    var right = penX + font.Size * 0.5;
                    path.AddMove(penX, baselineOrigin.Y);
                    path.LineTo(right, baselineOrigin.Y);
                    path.LineTo(right, top);
                    path.LineTo(penX, top);
                    path.CloseFigure();
                    any = true;
                }

                penX += font.Size * 0.6 + letterSpacing;
            }

            if (!any)
            {
                path.Dispose();
                return null;
            }

            return path;
        }
        public override (RGraphics Graphics, RImage Image)? CreateTile(double width, double height) => null;
        public override void DrawImageMasked(RImage image, RImage maskImage, RRect destRect) { }
        public override void DrawImageWithOpacity(RImage image, RRect destRect, double opacity, RBlendMode blendMode = RBlendMode.Normal) { }
        public override void DrawImageWithColorMatrix(RImage image, RRect destRect, ColorMatrix matrix) { }
        public override void DrawImageAlphaMasked(RImage image, RImage maskImage, RRect destRect, bool invert = false) { }
        public override void DrawImageBlendedOver(RImage top, RImage bottom, RRect destRect, RBlendMode blendMode) { }
        public override void BeginMarkedContent(string structureType, int mcid) { }
        public override void EndMarkedContent() { }
        public override void BeginArtifact() { }
        public override void BeginVariableText() { }
        public override void EndVariableText() { }
        public override RSize MeasureString(string str, RFont font, ShapeSettings? features = null) => new((str?.Length ?? 0) * font.Size * 0.6, font.Height);
        public override int CountShapedGlyphs(string str, RFont font, ShapeSettings? features = null) => str?.Length ?? 0;
        public override void MeasureString(string str, RFont font, double maxWidth, out int charFit, out double charFitWidth)
        {
            charFit = str?.Length ?? 0;
            charFitWidth = charFit * font.Size * 0.6;
        }
        public override void DrawLine(RPen pen, double x1, double y1, double x2, double y2)
        {
            var call = pen is TestPen tp
                ? new DrawLineCall(tp.Color, tp.Width, tp.RecordedDashStyle, x1, y1, x2, y2, tp.RecordedLineCap, tp.RecordedDashPattern)
                : new DrawLineCall(RColor.Empty, 0, RDashStyle.Solid, x1, y1, x2, y2);
            Log.Add(call);
        }
        public override void DrawImage(RImage image, RRect destRect, RRect srcRect)
        {
            var call = new DrawImageCall(image, destRect, srcRect, image.Interpolate);
            DrawImageCalls.Add(call);
            Log.Add(call);
        }
        public override void DrawImage(RImage image, RRect destRect)
        {
            var call = new DrawImageCall(image, destRect, null, image.Interpolate);
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

    /// <summary>Records paint into an offscreen tile and the opacity used when it is composited.</summary>
    internal sealed class TestLayerRecordingGraphics : TestRecordingGraphics
    {
        public TestRecordingGraphics? TileGraphics { get; private set; }
        public double? CompositedOpacity { get; private set; }
        public RRect? CompositedBounds { get; private set; }

        public override (RGraphics Graphics, RImage Image)? CreateTile(double width, double height)
        {
            TileGraphics = new TestRecordingGraphics { PixelsPerPointOverride = PixelsPerPoint };
            return (TileGraphics, new TestImage(width, height));
        }

        public override void DrawImageWithOpacity(
            RImage image, RRect destRect, double opacity, RBlendMode blendMode = RBlendMode.Normal)
        {
            CompositedOpacity = opacity;
            CompositedBounds = destRect;
        }
    }
}
