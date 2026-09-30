using PeachDrawing.Text.Shaping;
using PeachPDF.Adapters;
using PeachDrawing.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.PdfSharpCore.Pdf;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace PeachPDF.Tests.Html.Core.Handlers
{
    public class StructureTagBuilderTests
    {
        [Fact]
        public void OpenContentElement_OffscreenTile_AllocatesNoMcid_AndDoesNotEmitMarkedContent()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            var builder = new StructureTagBuilder(doc);
            builder.BeginPage(page);

            var box = new CssBox(null, null);
            var g = new RecordingGraphics(isOffscreenTile: true);

            using (builder.OpenContentElement(g, box, "P"))
            {
            }

            Assert.Empty(g.Log);

            // The struct element is still created (keeps the tree shape well-formed) even though
            // no MCID/BDC was emitted for this tile-painted occurrence.
            Assert.NotNull(builder.TryGetStructureElement(box));
        }

        [Fact]
        public void OpenArtifact_OffscreenTile_DoesNotEmitBeginArtifact()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            var builder = new StructureTagBuilder(doc);
            builder.BeginPage(page);

            var g = new RecordingGraphics(isOffscreenTile: true);

            using (builder.OpenArtifact(g))
            {
            }

            Assert.Empty(g.Log);
        }

        [Fact]
        public void LinkAnnotationToStructureElement_BoxNeverTagged_IsNoOp()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            var builder = new StructureTagBuilder(doc);
            builder.BeginPage(page);
            builder.Finish();

            var untaggedBox = new CssBox(null, null);
            var annotation = page.AddWebLink(
                new PdfRectangle(new PeachPDF.PdfSharpCore.Drawing.XRect(0, 0, 10, 10)), "https://example.com");

            builder.LinkAnnotationToStructureElement(untaggedBox, page, annotation);

            // No struct element was ever created for this box, so linking must be a pure no-op -
            // no /StructParent assigned, no /Tabs override.
            Assert.False(page.Elements.ContainsKey("/Tabs"));
        }

        [Fact]
        public void Finish_DocumentHasTitle_SetsDisplayDocTitle()
        {
            var doc = new PdfDocument();
            doc.Info.Title = "A Tagged Document";
            var page = doc.AddPage();
            var builder = new StructureTagBuilder(doc);
            builder.BeginPage(page);

            builder.Finish();

            Assert.True(doc.Catalog.ViewerPreferences.DisplayDocTitle);
        }

        [Fact]
        public void Finish_DocumentHasNoTitle_LeavesDisplayDocTitleUnset()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            var builder = new StructureTagBuilder(doc);
            builder.BeginPage(page);

            builder.Finish();

            Assert.False(doc.Catalog.ViewerPreferences.DisplayDocTitle);
        }

        [Fact]
        public void OpenContentElement_SameBoxTwiceOnSamePage_ReusesElement_BothMcidsUnderSameElement()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            var builder = new StructureTagBuilder(doc);
            builder.BeginPage(page);

            var box = new CssBox(null, null);
            var g = new RecordingGraphics(isOffscreenTile: false);

            using (builder.OpenContentElement(g, box, "P")) { }
            using (builder.OpenContentElement(g, box, "P")) { }

            var element = builder.TryGetStructureElement(box);
            Assert.NotNull(element);

            var kids = PeachPDF.PdfSharpCore.Pdf.Structure.PdfStructureElement.GetKids(element!.Elements).ToList();
            // Both kids are bare MCID integers (not further struct elements), so GetKids (which
            // only surfaces dictionary kids) reports none - assert via the raw /K array instead.
            Assert.Empty(kids);
            var array = element.Elements.GetArray(PeachPDF.PdfSharpCore.Pdf.Structure.PdfStructureElement.Keys.K);
            Assert.NotNull(array);
            Assert.Equal(2, array!.Elements.Count);
        }

        [Fact]
        public void OpenContentElement_SameBoxOnLaterPage_AddsMarkedContentReference()
        {
            var doc = new PdfDocument();
            var page1 = doc.AddPage();
            var page2 = doc.AddPage();
            var builder = new StructureTagBuilder(doc);

            var box = new CssBox(null, null);
            var g = new RecordingGraphics(isOffscreenTile: false);

            builder.BeginPage(page1);
            using (builder.OpenContentElement(g, box, "P")) { }

            builder.BeginPage(page2);
            using (builder.OpenContentElement(g, box, "P")) { }

            var element = builder.TryGetStructureElement(box);
            Assert.NotNull(element);
            Assert.Same(page1, element!.Page);

            var array = element.Elements.GetArray(PeachPDF.PdfSharpCore.Pdf.Structure.PdfStructureElement.Keys.K);
            Assert.NotNull(array);
            Assert.Equal(2, array!.Elements.Count);
            Assert.IsType<PeachPDF.PdfSharpCore.Pdf.Structure.PdfMarkedContentReference>(
                ((PeachPDF.PdfSharpCore.Pdf.Advanced.PdfReference)array.Elements[1]).Value);
        }

        sealed class RecordingGraphics : Canvas
        {
            readonly bool _isOffscreenTile;
            public List<string> Log { get; } = [];

            public RecordingGraphics(bool isOffscreenTile)
                : base(new PdfSharpAdapter(), new Rect(0, 0, double.MaxValue, double.MaxValue))
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
