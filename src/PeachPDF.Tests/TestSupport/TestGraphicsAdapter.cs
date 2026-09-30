using PeachDrawing.Text.Shaping;
using PeachDrawing.Text;
using PeachDrawing.Text.Unicode;
using PeachDrawing.Core;
using PeachPDF.Network;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>
    /// A minimal, non-sealed <see cref="RenderContext"/> for tests. The only concrete adapter in the
    /// product, <c>PdfSharpAdapter</c>, is <c>sealed</c>, so a document that needs its own <see cref="RenderContext"/>
    /// (e.g. to control resource loading) needs a real, separate one like this one. Brush/pen creation
    /// needs no stub of its own any more - <see cref="Brush"/>'s subtypes (<see cref="SolidBrush"/>,
    /// <see cref="LinearGradientBrush"/>, etc.) and <see cref="Pen"/> are plain data now, so
    /// <see cref="RenderContext.GetSolidBrush"/>/<see cref="RenderContext.GetPen(PaintColor)"/> already return real,
    /// directly-inspectable instances; a test reads a drawn brush's color via
    /// <c>brush is SolidBrush solid ? solid.Color : ...</c> instead of a color-tracking test double.
    /// Everything else here is a minimal stub, safe for the small, focused test documents this is used
    /// with - anything actually needed but not stubbed here will fail loudly rather than silently.
    /// </summary>
    internal class TestGraphicsAdapter : RenderContext
    {
        public override RUri? BaseUri => null;

        public override string GetCssMediaType(IEnumerable<string> mediaTypesAvailable) => "print";

        // Only data: URIs get a real (dummy - see ImageFromStreamInt) resource stream, so tests that
        // need a replaced element (img/svg) to actually resolve an Image can use one; every other
        // scheme still resolves to null, preserving the existing "no real network access" behavior
        // other tests rely on.
        public override Task<RNetworkResponse?> GetResourceStream(RUri uri) =>
            Task.FromResult(uri.Scheme == "data"
                ? new RNetworkResponse(new MemoryStream([0]), null)
                : null);

        protected override PaintColor GetColorInt(string colorName) => PaintColor.Black;

        // Real PNG/JPEG decoding is out of scope for this minimal stub - tests that need an actually-
        // loaded image (e.g. asserting a replaced element gets its own intrinsic size) only need SOME
        // deterministic, non-zero size to resolve, not faithful pixel decoding, so this ignores the
        // stream's real bytes and returns a fixed-size TestImage instead of throwing.
        protected override Image ImageFromStreamInt(Stream memoryStream) => new TestImage(40, 30);

        protected override Font CreateFontInt(string family, double size, PaintFontStyle style, double weight = 400, double stretch = 100, double? obliqueSkewSinus = null, string? variations = null) => new TestFont(size);

        protected override Font CreateFontInt(FontFamily family, double size, PaintFontStyle style, double weight = 400, double stretch = 100, double? obliqueSkewSinus = null, string? variations = null) => new TestFont(size);

        protected override Font? CreateFontForCodepointInt(string family, double size, PaintFontStyle style, double weight, double stretch, double? obliqueSkewSinus, System.Text.Rune codepoint, string? variations) => new TestFont(size);

        // No family this stub knows about ever "wins" the last-resort search - there is no real
        // InstalledFonts registry backing it, so the only faithful answer is "nothing found".
        protected override Font? CreateSystemFallbackFontForCodepointInt(double size, PaintFontStyle style, double weight, double stretch, double? obliqueSkewSinus, System.Text.Rune codepoint, PeachDrawing.Text.Unicode.EmojiPresentation presentation, string? variations) => null;

        protected override bool FamilyHasExplicitUnicodeRangesInt(string family) => false;

        protected override Task<bool> AddFontFromStream(string fontFamilyName, Stream stream, string? format, FontFaceDescriptors descriptors = default, IReadOnlyList<PeachDrawing.Text.RuneInterval>? unicodeRanges = null) => Task.FromResult(false);

        protected override Task<bool> AddLocalFont(string fontFamilyName, string localFontFaceName, FontFaceDescriptors descriptors = default, IReadOnlyList<PeachDrawing.Text.RuneInterval>? unicodeRanges = null) => Task.FromResult(false);
    }

    /// <summary>A fixed-size image, independent of any real pixel decoding - see ImageFromStreamInt's
    /// own comment for why TestGraphicsAdapter doesn't decode real image bytes.</summary>
    internal sealed class TestImage(double width, double height) : Image
    {
        public override double Width => width;
        public override double Height => height;
        public override bool Interpolate { get; set; }
        public override void Dispose() { }
    }

    /// <summary>A deterministic fixed-metric font, independent of any real font file/rasterizer.</summary>
    internal class TestFont(double size) : Font
    {
        public override double Size => size;
        public override double Height => size * 1.2;
        public override double UnderlineOffset => size * 0.9;
        public override double Ascent => size * 0.8;
        public override double LeftPadding => 0;
        public override double GetWhitespaceWidth(Canvas graphics) => size * 0.25;
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
    internal sealed class TestGraphicsPath : GraphicsPath
    {
        public List<PaintPoint> Points { get; } = [];

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
            Points.Add(new PaintPoint(x, y));
        }

        public override void LineTo(double x, double y) => Points.Add(new PaintPoint(x, y));
        public override void ArcTo(double x, double y, double radiusX, double radiusY, Corner corner) => Points.Add(new PaintPoint(x, y));

        public override void AddMove(double x, double y)
        {
            SubpathStarts.Add(Points.Count);
            Points.Add(new PaintPoint(x, y));
        }

        public override void AddBezierTo(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            BezierControlPoints.Add(Points.Count);
            BezierControlPoints.Add(Points.Count + 1);
            Points.Add(new PaintPoint(x1, y1));
            Points.Add(new PaintPoint(x2, y2));
            Points.Add(new PaintPoint(x3, y3));
        }

        public override void AddArc(double x, double y, double radiusX, double radiusY, double rotationAngle, bool isLargeArc, bool sweepClockwise) => Points.Add(new PaintPoint(x, y));
        public override void CloseFigure() { }

        public override void Transform(Matrix3x2 matrix)
        {
            for (var i = 0; i < Points.Count; i++)
            {
                var p = Points[i];
                Points[i] = new PaintPoint(
                    p.X * matrix.M11 + p.Y * matrix.M21 + matrix.M31,
                    p.X * matrix.M12 + p.Y * matrix.M22 + matrix.M32);
            }
        }

        public override void AddPath(GraphicsPath path)
        {
            var other = (TestGraphicsPath)path;
            foreach (var start in other.SubpathStarts) SubpathStarts.Add(Points.Count + start);
            foreach (var control in other.BezierControlPoints) BezierControlPoints.Add(Points.Count + control);
            Points.AddRange(other.Points);
        }

        public override FillMode FillMode { get; set; }

        /// <summary>Test-double approximation of <see cref="GraphicsPath.ClipToRect"/>: since this
        /// double records a flat point list rather than real subpath/contour structure, it can't
        /// reproduce the real Sutherland-Hodgman clip - it just keeps whichever recorded points already
        /// fall inside <paramref name="rect"/>, which is enough for tests that use this double and don't
        /// assert on rectangle-clip geometry specifically (see <c>GraphicsPathAdapter.ClipToRect</c> for
        /// the real implementation).</summary>
        public override GraphicsPath ClipToRect(Rect rect)
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
    /// <see cref="TestGraphicsAdapter"/> so brush/pen colors are introspectable directly off the real
    /// <see cref="SolidBrush"/>/<see cref="Pen"/> a draw call used. <see cref="PushClip(Rect)"/>/<see cref="PopClip"/>
    /// are also logged (as <see cref="PushClipCall"/>/<see cref="PopClipCall"/>) alongside the draw
    /// calls, not just tracked in the internal clip stack, so tests can assert clip pushes happen in
    /// the right order relative to specific draw calls (e.g. that a clip is pushed before the content
    /// it's meant to constrain) - per this class's own convention of extending the single ordered log
    /// for new call types rather than building a separate parallel mechanism.
    /// </summary>
    internal class TestRecordingGraphics : Canvas
    {
        public sealed record DrawStringCall(string Text, Font Font, PaintColor PaintColor, PaintPoint PaintPoint, Size Size, double LetterSpacing = 0, ShapeSettings? Features = null, string? LogicalText = null);
        public sealed record DrawRectCall(PaintColor PaintColor, double X, double Y, double Width, double Height);
        /// <summary>
        /// A filled or stroked path. <see cref="Points"/> is the path's recorded geometry, which is the only
        /// way to assert on a rounded shape: a <c>border-radius</c> background takes this route rather than
        /// <see cref="DrawRectCall"/>, so its rectangle would otherwise be invisible to a test. Copied at
        /// record time — <see cref="TestGraphicsPath.Transform"/> mutates in place.
        /// </summary>
        public sealed record DrawPathCall(PaintColor PaintColor, IReadOnlyList<PaintPoint> Points)
        {
            /// <summary>The axis-aligned bounds of the recorded points, or empty when there are none.</summary>
            public Rect Bounds => Points.Count == 0
                ? Rect.Empty
                : Rect.FromLTRB(Points.Min(p => p.X), Points.Min(p => p.Y), Points.Max(p => p.X), Points.Max(p => p.Y));

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
            public IReadOnlyList<PaintPoint> Subpath(int index)
            {
                var start = SubpathStarts[index];
                var end = index + 1 < SubpathStarts.Count ? SubpathStarts[index + 1] : Points.Count;
                return [.. Points.Skip(start).Take(end - start)];
            }

            /// <summary>The axis-aligned bounds of subpath <paramref name="index"/>.</summary>
            public Rect SubpathBounds(int index)
            {
                var points = Subpath(index);
                return points.Count == 0
                    ? Rect.Empty
                    : Rect.FromLTRB(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));
            }

            /// <summary>The linear gradient endpoints this fill was resolved against (null for a solid
            /// fill/stroke, or a non-gradient brush) - see <see cref="LinearGradientBrush.Start"/>.</summary>
            public PaintPoint? GradientStart { get; init; }
            public PaintPoint? GradientEnd { get; init; }

            /// <summary>True when the path was stroked with a pen rather than filled with a brush.</summary>
            public bool Stroked { get; init; }
            public double StrokeWidth { get; init; }
            public LineCap LineCap { get; init; }
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
            PaintColor PaintColor, double Width, DashStyle DashStyle, double X1, double Y1, double X2, double Y2,
            LineCap LineCap = LineCap.Butt, IReadOnlyList<double>? DashPattern = null);
        public sealed record DrawPolygonCall(PaintColor PaintColor, PaintPoint[] Points);
        public sealed record PushClipCall(Rect Rect);
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
        public sealed record DrawImageCall(Image Image, Rect DestRect, Rect? SrcRect = null, bool Interpolate = false);
        public sealed record PushBlendModeCall(PaintBlendMode Mode);
        public sealed record PopBlendModeCall;
        public sealed record PushTransformCall(Matrix3x2 Matrix);
        public sealed record PopTransformCall;

        public List<object> Log { get; } = [];
        public List<DrawStringCall> DrawStringCalls { get; } = [];
        public List<DrawImageCall> DrawImageCalls { get; } = [];

        /// <summary>The path passed to each <see cref="PushClip(GraphicsPath)"/> call, in order, so
        /// tests can inspect the resulting clip geometry (e.g. a transformed clip region).</summary>
        public List<TestGraphicsPath> ClipPaths { get; } = [];

        /// <summary>
        /// Settable so a test can exercise a non-default <c>PixelsPerPoint</c> (issue #812/#814) without a
        /// full <c>GraphicsAdapter</c>/PDF stack - defaults to the base <see cref="Canvas.PixelsPerPoint"/>
        /// no-op of <c>1.0</c>. A field-backed override (rather than an auto-property) since the base
        /// virtual member is get-only. Mirrors <c>RecordingGraphics.PixelsPerPointOverride</c>.
        /// </summary>
        public double PixelsPerPointOverride { get; set; } = 1.0;

        public override double PixelsPerPoint => PixelsPerPointOverride;

        public TestRecordingGraphics() : base(new TestGraphicsAdapter(), new Rect(0, 0, double.MaxValue, double.MaxValue)) { }

        public override void DrawString(string str, Font font, PaintColor color, PaintPoint point, Size size, double letterSpacing = 0, FontPalette? fontPalette = null, ShapeSettings? features = null)
        {
            var call = new DrawStringCall(str, font, color, point, size, letterSpacing, features);
            DrawStringCalls.Add(call);
            Log.Add(call);
        }

        public override void DrawGlyphs(IReadOnlyList<GlyphPlacement> glyphs, Font font, PaintColor color) { }

        /// <summary>See <see cref="Canvas.DrawString(string, Font, PaintColor, PaintPoint, Size, double, FontPalette?, ShapeSettings?, string?)"/>'s
        /// own remarks for <paramref name="logicalText"/>. Dispatches through the virtual 8-arg overload
        /// first (rather than recording independently) so a subclass that overrides only that one (e.g.
        /// <c>RenderErrorReportingTests.ThrowingGraphics</c>) still intercepts every call made through
        /// this overload too - then attaches <paramref name="logicalText"/> to the record that call just
        /// appended.</summary>
        public override void DrawString(string str, Font font, PaintColor color, PaintPoint point, Size size, double letterSpacing, FontPalette? fontPalette, ShapeSettings? features, string? logicalText)
        {
            DrawString(str, font, color, point, size, letterSpacing, fontPalette, features);
            if (logicalText is null) return;

            var withLogicalText = DrawStringCalls[^1] with { LogicalText = logicalText };
            DrawStringCalls[^1] = withLogicalText;
            Log[^1] = withLogicalText;
        }

        /// <summary>A brush's representative color for tests that only care about "what color did this
        /// paint with": the solid color, or a gradient's first stop.</summary>
        private static PaintColor ColorOf(Brush? brush) => brush switch
        {
            SolidBrush solid => solid.PaintColor,
            LinearGradientBrush { Stops.Count: > 0 } linear => linear.Stops[0].PaintColor,
            RadialGradientBrush { Stops.Count: > 0 } radial => radial.Stops[0].PaintColor,
            ConicGradientBrush { Stops.Count: > 0 } conic => conic.Stops[0].PaintColor,
            _ => PaintColor.Empty,
        };

        public override void DrawRectangle(Brush brush, double x, double y, double width, double height)
        {
            Log.Add(new DrawRectCall(ColorOf(brush), x, y, width, height));
        }

        public override void DrawRectangle(Pen pen, double x, double y, double width, double height)
        {
            Log.Add(new DrawRectCall(ColorOf(pen.Paint), x, y, width, height));
        }

        public override void DrawPath(Brush brush, GraphicsPath path)
        {
            var (start, end) = brush is LinearGradientBrush linear ? ((PaintPoint?)linear.Start, (PaintPoint?)linear.End) : (null, null);
            Log.Add(new DrawPathCall(ColorOf(brush), PointsOf(path)) { GradientStart = start, GradientEnd = end, SubpathStarts = SubpathStartsOf(path), BezierControlPoints = BezierControlPointsOf(path) });
        }

        public override void DrawPath(Pen pen, GraphicsPath path)
        {
            Log.Add(new DrawPathCall(ColorOf(pen.Paint), PointsOf(path))
            {
                Stroked = true,
                StrokeWidth = pen.Width,
                LineCap = pen.LineCap,
                DashPattern = pen.DashStyle == DashStyle.Custom ? pen.DashPattern : null,
                DashOffset = pen.DashOffset,
                SubpathStarts = SubpathStartsOf(path),
                BezierControlPoints = BezierControlPointsOf(path)
            });
        }

        private static IReadOnlySet<int> BezierControlPointsOf(GraphicsPath path) =>
            path is TestGraphicsPath testPath
                ? new HashSet<int>(testPath.BezierControlPoints)
                : new HashSet<int>();

        private static IReadOnlyList<PaintPoint> PointsOf(GraphicsPath path) =>
            path is TestGraphicsPath testPath ? testPath.Points.ToArray() : [];

        private static IReadOnlyList<int> SubpathStartsOf(GraphicsPath path) =>
            path is TestGraphicsPath testPath ? testPath.SubpathStarts.ToArray() : [];

        /// <summary>One filled shape, whichever primitive produced it.</summary>
        public sealed record FilledShape(PaintColor PaintColor, Rect Bounds);

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
                        new FilledShape(p.PaintColor, Rect.FromLTRB(
                            p.Points.Min(pt => pt.X), p.Points.Min(pt => pt.Y),
                            p.Points.Max(pt => pt.X), p.Points.Max(pt => pt.Y))),
                    DrawPathCall { Stroked: false, Points.Count: > 0 } p => new FilledShape(p.PaintColor, p.Bounds),
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
        public List<Matrix3x2> PushTransformCalls { get; } = [];
        public int PushTransformCount => PushTransformCalls.Count;
        public int PopTransformCount { get; private set; }

        public override void PushTransform(Matrix3x2 matrix)
        {
            PushTransformCalls.Add(matrix);
            Log.Add(new PushTransformCall(matrix));
        }
        public override void PopTransform()
        {
            PopTransformCount++;
            Log.Add(new PopTransformCall());
        }
        public override void PushClip(Rect rect)
        {
            _clipStack.Push(rect);
            Log.Add(new PushClipCall(rect));
        }
        public override void PushClip(GraphicsPath path)
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
        public override void PushClipExclude(Rect rect) { }
        public override void PushBlendMode(PaintBlendMode mode) => Log.Add(new PushBlendModeCall(mode));
        public override void PopBlendMode() => Log.Add(new PopBlendModeCall());
        public override object SetAntiAliasSmoothingMode() => new object();
        public override void ReturnPreviousSmoothingMode(object? prevMode) { }
        public override GraphicsPath GetGraphicsPath() => new TestGraphicsPath();

        /// <summary>
        /// A synthetic glyph-run outline: one box subpath per non-whitespace rune, advancing along the
        /// baseline. Faithful to the real <see cref="GraphicsAdapter.GetTextOutline"/> contract
        /// (non-null exactly when there is fillable geometry, null for a whitespace-only/empty run)
        /// without needing a real <c>glyf</c> font, so SVG gradient/pattern-fill and stroke-on-text
        /// paths can be driven and their <see cref="DrawPath(Brush, GraphicsPath)"/>/
        /// <see cref="DrawPath(Pen, GraphicsPath)"/> calls recorded.
        /// </summary>
        public override GraphicsPath? GetTextOutline(string str, Font font, PaintPoint baselineOrigin, double letterSpacing = 0, ShapeSettings? features = null)
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
        public override (Canvas Graphics, Image Image)? CreateTile(double width, double height) => null;
        public override void DrawImageMasked(Image image, Image maskImage, Rect destRect) { }
        public override void DrawImageWithOpacity(Image image, Rect destRect, double opacity, PaintBlendMode blendMode = PaintBlendMode.Normal) { }
        public override void DrawImageWithColorMatrix(Image image, Rect destRect, ColorMatrix matrix) { }
        public override void DrawImageAlphaMasked(Image image, Image maskImage, Rect destRect, bool invert = false) { }
        public override void DrawImageBlendedOver(Image top, Image bottom, Rect destRect, PaintBlendMode blendMode) { }
        public override void BeginMarkedContent(string structureType, int mcid) { }
        public override void EndMarkedContent() { }
        public override void BeginArtifact() { }
        public override void BeginVariableText() { }
        public override void EndVariableText() { }
        public override Size MeasureString(string str, Font font, ShapeSettings? features = null) => new((str?.Length ?? 0) * font.Size * 0.6, font.Height);
        public override int CountShapedGlyphs(string str, Font font, ShapeSettings? features = null) => str?.Length ?? 0;
        public override void MeasureString(string str, Font font, double maxWidth, out int charFit, out double charFitWidth)
        {
            charFit = str?.Length ?? 0;
            charFitWidth = charFit * font.Size * 0.6;
        }
        public override void DrawLine(Pen pen, double x1, double y1, double x2, double y2)
        {
            var call = new DrawLineCall(ColorOf(pen.Paint), pen.Width, pen.DashStyle, x1, y1, x2, y2, pen.LineCap,
                pen.DashStyle == DashStyle.Custom ? pen.DashPattern : null);
            Log.Add(call);
        }
        public override void DrawImage(Image image, Rect destRect, Rect srcRect)
        {
            var call = new DrawImageCall(image, destRect, srcRect, image.Interpolate);
            DrawImageCalls.Add(call);
            Log.Add(call);
        }
        public override void DrawImage(Image image, Rect destRect)
        {
            var call = new DrawImageCall(image, destRect, null, image.Interpolate);
            DrawImageCalls.Add(call);
            Log.Add(call);
        }
        public override void DrawPolygon(Brush brush, PaintPoint[] points)
        {
            // _borderPts (BordersDrawHandler) is a shared, reused array - clone so this log entry
            // isn't silently mutated by later calls that overwrite the same backing array.
            Log.Add(new DrawPolygonCall(ColorOf(brush), (PaintPoint[])points.Clone()));
        }
        public override void Dispose() { }
    }

    /// <summary>Records paint into an offscreen tile and the opacity used when it is composited.</summary>
    internal sealed class TestLayerRecordingGraphics : TestRecordingGraphics
    {
        public TestRecordingGraphics? TileGraphics { get; private set; }
        public double? CompositedOpacity { get; private set; }
        public Rect? CompositedBounds { get; private set; }

        public override (Canvas Graphics, Image Image)? CreateTile(double width, double height)
        {
            TileGraphics = new TestRecordingGraphics { PixelsPerPointOverride = PixelsPerPoint };
            return (TileGraphics, new TestImage(width, height));
        }

        public override void DrawImageWithOpacity(
            Image image, Rect destRect, double opacity, PaintBlendMode blendMode = PaintBlendMode.Normal)
        {
            CompositedOpacity = opacity;
            CompositedBounds = destRect;
        }
    }
}
