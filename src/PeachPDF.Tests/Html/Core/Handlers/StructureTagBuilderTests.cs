using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.PdfSharpCore.Pdf;
using System.Collections.Generic;
using System.Linq;

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

        sealed class RecordingGraphics : RGraphics
        {
            readonly bool _isOffscreenTile;
            public List<string> Log { get; } = [];

            public RecordingGraphics(bool isOffscreenTile)
                : base(new PdfSharpAdapter(), new RRect(0, 0, double.MaxValue, double.MaxValue))
            {
                _isOffscreenTile = isOffscreenTile;
            }

            public override bool IsOffscreenTile => _isOffscreenTile;

            public override void BeginMarkedContent(string structureType, int mcid) => Log.Add($"BeginMarkedContent:{structureType}");
            public override void EndMarkedContent() => Log.Add("EndMarkedContent");
            public override void BeginArtifact() => Log.Add("BeginArtifact");

            public override void PushTransform(RMatrix matrix) { }
            public override void PopTransform() { }
            public override void PushClip(RRect rect) => _clipStack.Push(rect);
            public override void PushClip(RGraphicsPath path) => _clipStack.Push(_clipStack.Peek());
            public override void PopClip() { if (_clipStack.Count > 1) _clipStack.Pop(); }
            public override void PushClipExclude(RRect rect) { }
            public override object SetAntiAliasSmoothingMode() => new object();
            public override void ReturnPreviousSmoothingMode(object? prevMode) { }
            public override RGraphicsPath GetGraphicsPath() => null!;

            public override RGraphicsPath? GetTextOutline(string str, RFont font, RPoint baselineOrigin, double letterSpacing = 0) => null;
            public override (RGraphics Graphics, RImage Image)? CreateTile(double width, double height) => null;
            public override void DrawImageMasked(RImage image, RImage maskImage, RRect destRect) { }
            public override void DrawImageWithOpacity(RImage image, RRect destRect, double opacity) { }
            public override RSize MeasureString(string str, RFont font) => new(10, 12);
            public override void MeasureString(string str, RFont font, double maxWidth, out int charFit, out double charFitWidth)
            {
                charFit = str?.Length ?? 0;
                charFitWidth = maxWidth;
            }
            public override void DrawString(string str, RFont font, RColor color, RPoint point, RSize size, bool rtl, double letterSpacing = 0, RFontPalette? fontPalette = null) { }
            public override void DrawLine(RPen pen, double x1, double y1, double x2, double y2) { }
            public override void DrawRectangle(RPen pen, double x, double y, double width, double height) { }
            public override void DrawRectangle(RBrush brush, double x, double y, double width, double height) { }
            public override void DrawImage(RImage image, RRect destRect, RRect srcRect) { }
            public override void DrawImage(RImage image, RRect destRect) { }
            public override void DrawPath(RPen pen, RGraphicsPath path) { }
            public override void DrawPath(RBrush brush, RGraphicsPath path) { }
            public override void DrawPolygon(RBrush brush, RPoint[] points) { }
            public override void Dispose() { }
        }
    }
}
