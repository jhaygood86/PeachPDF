using PeachDrawing.Text.Shaping;
using PeachDrawing.Text.Internal.Text;
using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using System.Linq;
using System.Threading.Tasks;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    // Painting-change coverage per CLAUDE.md: asserts the actual sequence of calls made to the
    // RGraphics adapter layer (not just that painting completes, and not string-parsing serialized
    // PDF bytes) - a single ordered log records BeginMarkedContent/EndMarkedContent/BeginArtifact
    // interleaved with DrawString, so tests can assert BDC/EMC lands immediately around the draw
    // calls it's supposed to wrap, in the right nesting order.
    public class TaggedPdfPaintOrderTests
    {
        [Fact]
        public async Task Paragraph_WrapsItsOwnDrawStringInMatchingBeginEndMarkedContent()
        {
            // <p> itself is pure Grouping (/P), not Content - a real element's own text always
            // lives on an anonymous text-leaf child box (see StructureTagMapper), which is what
            // actually opens the marked-content sequence around the DrawString call, tagged /Span
            // via the auto-fallback (anonymous boxes have no source element for any CSS selector,
            // including -peachpdf-pdf-tag-type rules, to match).
            var log = await RecordPaintCalls("<p id='p'>text</p>", enableTagging: true);

            var bdcIndex = log.FindIndex(e => e is ("BeginMarkedContent", "/Span"));
            var drawIndex = log.FindIndex(e => e.Item1 == "DrawString");
            var emcIndex = log.FindIndex(e => e.Item1 == "EndMarkedContent");

            Assert.True(bdcIndex >= 0, "Expected a BeginMarkedContent(\"/Span\", ...) call.");
            Assert.True(drawIndex > bdcIndex, "DrawString must come after BeginMarkedContent.");
            Assert.True(emcIndex > drawIndex, "EndMarkedContent must come after DrawString.");
        }

        [Fact]
        public async Task DivWrappingParagraph_OnlyWrapsTheTextLeaf_NotDivOrP()
        {
            var log = await RecordPaintCalls("<div><p id='p'>text</p></div>", enableTagging: true);

            var beginMarkedContentCalls = log.Count(e => e.Item1 == "BeginMarkedContent");
            var endMarkedContentCalls = log.Count(e => e.Item1 == "EndMarkedContent");

            // Both <div> and <p> are pure Grouping (no MCID of their own, per StructureTagMapper -
            // see the previous test) - only the anonymous text-leaf child holding "text" (tagged
            // /Span by the auto-fallback) opens a single marked-content sequence.
            Assert.Equal(1, beginMarkedContentCalls);
            Assert.Equal(1, endMarkedContentCalls);
            Assert.Contains(log, e => e is ("BeginMarkedContent", "/Span"));
        }

        [Fact]
        public async Task Hr_ProducesArtifactNotMarkedContent()
        {
            var log = await RecordPaintCalls("<hr id='hr' />", enableTagging: true);

            Assert.Contains(log, e => e.Item1 == "BeginArtifact");
            Assert.DoesNotContain(log, e => e.Item1 == "BeginMarkedContent");
        }

        [Fact]
        public async Task ListItem_WrapsMarkerAndBodyInSeparateBalancedMarkedContentSequences()
        {
            var log = await RecordPaintCalls("<ul><li>One</li></ul>", enableTagging: true);

            // Two BDC/EMC pairs: one for the marker's own draw call(s) tagged "/Lbl", one for the
            // body text's anonymous "/Span" - each opened and closed (balanced) rather than nested
            // inside one another.
            var beginIndices = log.Select((e, i) => (e, i)).Where(t => t.e.Item1 == "BeginMarkedContent").Select(t => t.i).ToList();
            var endIndices = log.Select((e, i) => (e, i)).Where(t => t.e.Item1 == "EndMarkedContent").Select(t => t.i).ToList();

            Assert.Equal(2, beginIndices.Count);
            Assert.Equal(2, endIndices.Count);
            Assert.Contains(log, e => e is ("BeginMarkedContent", "/Lbl"));
            Assert.Contains(log, e => e is ("BeginMarkedContent", "/Span"));

            // Each BDC is matched by the very next EMC before another BDC opens - i.e. the two
            // sequences are siblings, not nested.
            for (var i = 0; i < beginIndices.Count; i++)
            {
                Assert.True(endIndices[i] > beginIndices[i]);
                if (i + 1 < beginIndices.Count)
                    Assert.True(endIndices[i] < beginIndices[i + 1]);
            }
        }

        [Fact]
        public async Task EnableTaggedPdf_False_ProducesZeroMarkedContentOrArtifactCalls()
        {
            // The single most important regression test for "off by default": the same HTML that
            // produces several BeginMarkedContent/BeginArtifact calls when tagging is enabled must
            // produce exactly zero when it's not - not just "off in config" but genuinely inert,
            // with StructureTagMapper.Classify never even invoked (see CssBox.PaintImp's null check).
            var log = await RecordPaintCalls(
                "<div><h1>Title</h1><p>Body</p><hr/><img src='data:image/png;base64," + TinyPngBase64 + "' alt='x'/></div>",
                enableTagging: false);

            Assert.DoesNotContain(log, e => e.Item1 is "BeginMarkedContent" or "EndMarkedContent" or "BeginArtifact");
            Assert.Contains(log, e => e.Item1 == "DrawString");
        }

        [Fact]
        public async Task Outline_IsAnArtifact_DrawnAfterItsOwnersMarkedContentCloses()
        {
            // An outline is drawn once its scope has painted everything else, by when the structure
            // element owning it has long closed its marked content - so it is marked as decoration,
            // never nested inside a structure element's sequence.
            var log = await RecordPaintCalls(
                "<p style='outline: 2pt solid rgb(200,0,0)'>text</p>", enableTagging: true);

            var textEnd = log.FindIndex(e => e.Item1 == "EndMarkedContent");
            var artifact = log.FindIndex(e => e.Item1 == "BeginArtifact");
            var ring = log.FindIndex(e => e.Item1 == "FillPath");

            Assert.True(textEnd >= 0 && artifact > textEnd, "the outline's artifact opened inside the text's marked content");
            Assert.True(ring > artifact, "the outline was drawn outside its artifact");
            Assert.Equal("EndMarkedContent", log[ring + 1].Item1);
        }

        [Theory]
        // A grouping box: opens no marked content of its own.
        [InlineData("<div style='position: relative; z-index: 0; background: #eee; outline: 2px solid red'>" +
                    "text<span style='position: absolute; z-index: 1'>x</span></div>")]
        // A box tagged as an artifact: its own artifact sequence is open while it paints.
        [InlineData("<div style='position: relative; z-index: 0; outline: 2px solid red; -peachpdf-pdf-tag-type: artifact'>" +
                    "text<span style='position: absolute; z-index: 1'>x</span></div>")]
        public async Task Outline_DrawnUnderAPositiveZIndexChild_IsNeverNestedInMarkedContent(string html)
        {
            // The scope's outlines are drawn ahead of its raised layers, inside the scope box's own paint
            // rather than after it - where the scope box's own sequence may still be open. The outline's
            // artifact must never open inside it, and every shape the outline draws must still be marked.
            // (A content element nested in an author's artifact - the artifact case's text - predates this.)
            var log = await RecordPaintCalls(html, enableTagging: true);

            var depth = 0;
            var shapes = 0;
            foreach (var (call, _) in log)
            {
                switch (call)
                {
                    case "BeginMarkedContent":
                        depth++;
                        break;
                    case "BeginArtifact":
                        Assert.True(depth == 0, "an artifact was opened inside another marked-content sequence");
                        depth++;
                        break;
                    case "EndMarkedContent":
                        depth--;
                        break;
                    case "FillPath" or "StrokePath" or "FillPolygon":
                        Assert.True(depth > 0, $"{call} was drawn outside any marked-content sequence");
                        shapes++;
                        break;
                }
            }

            Assert.Equal(0, depth);
            Assert.True(shapes > 0, "the outline drew nothing");
        }

        // A 1x1 transparent PNG.
        const string TinyPngBase64 =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

        // ─── Helpers ─────────────────────────────────────────────────────────────

        static async Task<System.Collections.Generic.List<(string, string?)>> RecordPaintCalls(string bodyHtml, bool enableTagging)
        {
            var html = $"<!DOCTYPE html><html><body>{bodyHtml}</body></html>";

            var adapter = new PdfSharpAdapter();
            adapter.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var doc = new PdfDocument();
            var page = doc.AddPage();
            page.Width = size.Width;
            page.Height = size.Height;

            if (enableTagging)
            {
                var structureTagBuilder = new StructureTagBuilder(doc);
                container.StructureTagBuilder = structureTagBuilder;
                structureTagBuilder.BeginPage(page);
            }

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var measureGraphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(measureGraphics);

            var recorder = new RecordingGraphics(adapter);
            FragmentPaintHarness.PaintPage(container, recorder);

            return recorder.Log;
        }

        /// <summary>
        /// Minimal RGraphics mock recording a single ordered log of the calls this test suite cares
        /// about (DrawString/BeginMarkedContent/EndMarkedContent/BeginArtifact) - a single shared log
        /// rather than separate per-call-type lists, since cross-call-type ordering is exactly what's
        /// under test here (per this repo's CLAUDE.md painting-test conventions).
        /// </summary>
        sealed class RecordingGraphics : RGraphics
        {
            public System.Collections.Generic.List<(string, string?)> Log { get; } = [];

            public RecordingGraphics(RAdapter adapter)
                : base(adapter, new RRect(0, 0, double.MaxValue, double.MaxValue)) { }

            public override void BeginMarkedContent(string structureType, int mcid) => Log.Add(("BeginMarkedContent", structureType));
            public override void EndMarkedContent() => Log.Add(("EndMarkedContent", null));
            public override void BeginArtifact() => Log.Add(("BeginArtifact", null));
            public override void BeginVariableText() => Log.Add(("BeginVariableText", null));
            public override void EndVariableText() => Log.Add(("EndVariableText", null));
            public override void DrawString(string str, RFont font, RColor color, RPoint point, RSize size, double letterSpacing = 0, RFontPalette? fontPalette = null, ShapeSettings? features = null) => Log.Add(("DrawString", str));
            public override void DrawGlyphs(IReadOnlyList<GlyphPlacement> glyphs, RFont font, RColor color) { }

            public override void PushTransform(RMatrix matrix) { }
            public override void PopTransform() { }
            public override void PushBlendMode(RBlendMode mode) { }
            public override void PopBlendMode() { }
            public override void PushClip(RRect rect) => _clipStack.Push(rect);
            public override void PushClip(RGraphicsPath path) => _clipStack.Push(_clipStack.Peek());
            public override void PopClip() { if (_clipStack.Count > 1) _clipStack.Pop(); }
            public override void PushClipExclude(RRect rect) { }
            public override object SetAntiAliasSmoothingMode() => new object();
            public override void ReturnPreviousSmoothingMode(object? prevMode) { }
            public override RGraphicsPath GetGraphicsPath() => new NoOpGraphicsPath();

            public override RGraphicsPath? GetTextOutline(string str, RFont font, RPoint baselineOrigin, double letterSpacing = 0, ShapeSettings? features = null) => null;
            public override (RGraphics Graphics, RImage Image)? CreateTile(double width, double height) => null;
            public override void DrawImageMasked(RImage image, RImage maskImage, RRect destRect) { }
            public override void DrawImageWithOpacity(RImage image, RRect destRect, double opacity, RBlendMode blendMode = RBlendMode.Normal) { }
            public override void DrawImageWithColorMatrix(RImage image, RRect destRect, ColorMatrix matrix) { }
            public override void DrawImageAlphaMasked(RImage image, RImage maskImage, RRect destRect, bool invert = false) { }
            public override void DrawImageBlendedOver(RImage top, RImage bottom, RRect destRect, RBlendMode blendMode) { }
            public override RSize MeasureString(string str, RFont font, ShapeSettings? features = null) => new(10, 12);
            public override int CountShapedGlyphs(string str, RFont font, ShapeSettings? features = null) => str?.Length ?? 0;
            public override void MeasureString(string str, RFont font, double maxWidth, out int charFit, out double charFitWidth)
            {
                charFit = str?.Length ?? 0;
                charFitWidth = maxWidth;
            }
            public override void DrawLine(RPen pen, double x1, double y1, double x2, double y2) { }
            public override void DrawRectangle(RPen pen, double x, double y, double width, double height) { }
            public override void DrawRectangle(RBrush brush, double x, double y, double width, double height) { }
            public override void DrawImage(RImage image, RRect destRect, RRect srcRect) { }
            public override void DrawImage(RImage image, RRect destRect) { }
            public override void DrawPath(RPen pen, RGraphicsPath path) => Log.Add(("StrokePath", null));
            public override void DrawPath(RBrush brush, RGraphicsPath path) => Log.Add(("FillPath", null));
            public override void DrawPolygon(RBrush brush, RPoint[] points) => Log.Add(("FillPolygon", null));
            public override void Dispose() { }
        }

        // A real (non-null) RGraphicsPath is needed once list-item marker painting is exercised
        // (RenderUtils.GetRoundRect, used for disc/square markers, calls g.GetGraphicsPath()
        // unconditionally) - mirrors CssLayoutEngineTablePageBreakTests.RecordingGraphicsPath.
        sealed class NoOpGraphicsPath : RGraphicsPath
        {
            public override void Start(double x, double y) { }
            public override void LineTo(double x, double y) { }
            public override void ArcTo(double x, double y, double radiusX, double radiusY, Corner corner) { }
            public override void AddMove(double x, double y) { }
            public override void AddBezierTo(double x1, double y1, double x2, double y2, double x3, double y3) { }
            public override void AddArc(double x, double y, double radiusX, double radiusY, double rotationAngle, bool isLargeArc, bool sweepClockwise) { }
            public override void CloseFigure() { }
            public override void Transform(RMatrix matrix) { }
            public override void AddPath(RGraphicsPath path) { }
            public override RFillMode FillMode { get; set; }
            public override RGraphicsPath ClipToRect(RRect rect) => this;
            public override void Dispose() { }
        }
    }
}
