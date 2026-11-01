using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachDrawing.PDF.Engine.Advanced;
using PeachDrawing.PDF.Engine.Structure;
using PeachDrawing.Text.Shaping;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace PeachPDF.Tests.PdfEngineTests
{
    /// <summary>
    /// The tag recorder's handling of the cases the HTML pipeline reaches only incidentally: tile-painted content, a link whose content was
    /// never tagged, the document title, and one element appearing on several pages. Drives <see cref="PdfTagRecorder"/> with plain keys,
    /// the way any host would, and reads the structure it built back from the document.
    /// </summary>
    public class PdfTagRecorderTests
    {
        [Fact]
        public void OpenContentElement_OffscreenTile_AllocatesNoMcid_AndDoesNotEmitMarkedContent()
        {
            var composer = new PdfComposer(new PdfRenderContext());
            var page = composer.AddPage(595, 842);
            var recorder = composer.CreateTagRecorder();
            recorder.BeginPage(page);

            var key = new object();
            var g = new RecordingGraphics(isOffscreenTile: true);

            using (recorder.OpenContentElement(g, key, "P"))
            {
            }

            Assert.Empty(g.Log);

            // The struct element is still created (keeps the tree shape well-formed) even though
            // no MCID/BDC was emitted for this tile-painted occurrence.
            Assert.NotNull(recorder.TryGetStructureElement(key));
        }

        [Fact]
        public void OpenArtifact_OffscreenTile_DoesNotEmitBeginArtifact()
        {
            var composer = new PdfComposer(new PdfRenderContext());
            var page = composer.AddPage(595, 842);
            var recorder = composer.CreateTagRecorder();
            recorder.BeginPage(page);

            var g = new RecordingGraphics(isOffscreenTile: true);

            using (recorder.OpenArtifact(g))
            {
            }

            Assert.Empty(g.Log);
        }

        [Fact]
        public void LinkAnnotationToStructureElement_BoxNeverTagged_IsNoOp()
        {
            var composer = new PdfComposer(new PdfRenderContext());
            var page = composer.AddPage(595, 842);
            var recorder = composer.CreateTagRecorder();
            recorder.BeginPage(page);
            recorder.Finish();

            var untaggedKey = new object();
            var link = page.AddLink(new Rect(0, 0, 10, 10), "https://example.com");

            recorder.LinkAnnotation(untaggedKey, link);

            // No struct element was ever created for this box, so linking must be a pure no-op -
            // no /StructParent assigned, no /Tabs override.
            Assert.False(page.Page.Elements.ContainsKey("/Tabs"));
        }

        [Fact]
        public void Finish_DocumentHasTitle_SetsDisplayDocTitle()
        {
            var composer = new PdfComposer(new PdfRenderContext());
            composer.Document.Info.Title = "A Tagged Document";
            var page = composer.AddPage(595, 842);
            var recorder = composer.CreateTagRecorder();
            recorder.BeginPage(page);

            recorder.Finish();

            Assert.True(composer.Document.Catalog.ViewerPreferences.DisplayDocTitle);
        }

        [Fact]
        public void Finish_DocumentHasNoTitle_LeavesDisplayDocTitleUnset()
        {
            var composer = new PdfComposer(new PdfRenderContext());
            var page = composer.AddPage(595, 842);
            var recorder = composer.CreateTagRecorder();
            recorder.BeginPage(page);

            recorder.Finish();

            Assert.False(composer.Document.Catalog.ViewerPreferences.DisplayDocTitle);
        }

        [Fact]
        public void OpenContentElement_SameBoxTwiceOnSamePage_ReusesElement_BothMcidsUnderSameElement()
        {
            var composer = new PdfComposer(new PdfRenderContext());
            var page = composer.AddPage(595, 842);
            var recorder = composer.CreateTagRecorder();
            recorder.BeginPage(page);

            var key = new object();
            var g = new RecordingGraphics(isOffscreenTile: false);

            using (recorder.OpenContentElement(g, key, "P")) { }
            using (recorder.OpenContentElement(g, key, "P")) { }

            var element = recorder.TryGetStructureElement(key);
            Assert.NotNull(element);

            var kids = PdfStructureElement.GetKids(element!.Elements).ToList();
            // Both kids are bare MCID integers (not further struct elements), so GetKids (which
            // only surfaces dictionary kids) reports none - assert via the raw /K array instead.
            Assert.Empty(kids);
            var array = element.Elements.GetArray(PdfStructureElement.Keys.K);
            Assert.NotNull(array);
            Assert.Equal(2, array!.Elements.Count);
        }

        [Fact]
        public void OpenContentElement_SameBoxOnLaterPage_AddsMarkedContentReference()
        {
            var composer = new PdfComposer(new PdfRenderContext());
            var page1 = composer.AddPage(595, 842);
            var page2 = composer.AddPage(595, 842);
            var recorder = composer.CreateTagRecorder();

            var key = new object();
            var g = new RecordingGraphics(isOffscreenTile: false);

            recorder.BeginPage(page1);
            using (recorder.OpenContentElement(g, key, "P")) { }

            recorder.BeginPage(page2);
            using (recorder.OpenContentElement(g, key, "P")) { }

            var element = recorder.TryGetStructureElement(key);
            Assert.NotNull(element);
            Assert.Same(page1.Page, element!.Page);

            var array = element.Elements.GetArray(PdfStructureElement.Keys.K);
            Assert.NotNull(array);
            Assert.Equal(2, array!.Elements.Count);
            Assert.IsType<PdfMarkedContentReference>(
                ((PdfReference)array.Elements[1]).Value);
        }

        sealed class RecordingGraphics : Canvas
        {
            readonly bool _isOffscreenTile;
            public List<string> Log { get; } = [];

            public RecordingGraphics(bool isOffscreenTile)
                : base(new PdfRenderContext(), new Rect(0, 0, double.MaxValue, double.MaxValue))
            {
                _isOffscreenTile = isOffscreenTile;
            }

            public override bool IsOffscreenTile => _isOffscreenTile;

            public override void BeginMarkedContent(string structureType, int mcid) => Log.Add($"BeginMarkedContent:{structureType}");
            public override void EndMarkedContent() => Log.Add("EndMarkedContent");
            public override void BeginArtifact() => Log.Add("BeginArtifact");
            public override void BeginVariableText() => Log.Add("BeginVariableText");
            public override void EndVariableText() => Log.Add("EndVariableText");

            public override void PushTransform(Matrix3x2 matrix) { }
            public override void PopTransform() { }
            public override void PushBlendMode(PaintBlendMode mode) { }
            public override void PopBlendMode() { }
            public override void PushClip(Rect rect) => _clipStack.Push(rect);
            public override void PushClip(GraphicsPath path) => _clipStack.Push(_clipStack.Peek());
            public override void PopClip() { if (_clipStack.Count > 1) _clipStack.Pop(); }
            public override void PushClipExclude(Rect rect) { }
            public override object SetAntiAliasSmoothingMode() => new object();
            public override void ReturnPreviousSmoothingMode(object? prevMode) { }
            public override GraphicsPath GetGraphicsPath() => null!;

            public override GraphicsPath? GetTextOutline(string str, Font font, PaintPoint baselineOrigin, double letterSpacing = 0, ShapeSettings? features = null) => null;
            public override (Canvas Graphics, Image Image)? CreateTile(double width, double height) => null;
            public override void DrawImageMasked(Image image, Image maskImage, Rect destRect) { }
            public override void DrawImageWithOpacity(Image image, Rect destRect, double opacity, PaintBlendMode blendMode = PaintBlendMode.Normal) { }
            public override void DrawImageWithColorMatrix(Image image, Rect destRect, ColorMatrix matrix) { }
            public override void DrawImageAlphaMasked(Image image, Image maskImage, Rect destRect, bool invert = false) { }
            public override void DrawImageBlendedOver(Image top, Image bottom, Rect destRect, PaintBlendMode blendMode) { }
            public override Size MeasureString(string str, Font font, ShapeSettings? features = null) => new(10, 12);
            public override int CountShapedGlyphs(string str, Font font, ShapeSettings? features = null) => str?.Length ?? 0;
            public override void MeasureString(string str, Font font, double maxWidth, out int charFit, out double charFitWidth)
            {
                charFit = str?.Length ?? 0;
                charFitWidth = maxWidth;
            }
            public override void DrawString(string str, Font font, PaintColor color, PaintPoint point, Size size, double letterSpacing = 0, FontPalette? fontPalette = null, ShapeSettings? features = null) { }
            public override void DrawGlyphs(IReadOnlyList<GlyphPlacement> glyphs, Font font, PaintColor color) { }
            public override void DrawLine(Pen pen, double x1, double y1, double x2, double y2) { }
            public override void DrawRectangle(Pen pen, double x, double y, double width, double height) { }
            public override void DrawRectangle(Brush brush, double x, double y, double width, double height) { }
            public override void DrawImage(Image image, Rect destRect, Rect srcRect) { }
            public override void DrawImage(Image image, Rect destRect) { }
            public override void DrawPath(Pen pen, GraphicsPath path) { }
            public override void DrawPath(Brush brush, GraphicsPath path) { }
            public override void DrawPolygon(Brush brush, PaintPoint[] points) { }
            public override void Dispose() { }
        }
    }
}
