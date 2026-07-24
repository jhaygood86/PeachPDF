using System.Text;
using PeachPDF;
using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.Advanced;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression tests for two paint-pipeline fixes:
    ///
    /// 1. <c>DomUtils.FlattenStackingContext</c> used to recurse into every descendant at every
    ///    ancestor level (not just direct children), so a box was independently re-painted once per
    ///    ancestor between it and the root. Fixed to only hoist genuinely out-of-flow (floated,
    ///    absolutely positioned, fixed) descendants past normal-flow wrapper boxes; normal content is
    ///    now painted exactly once, via its own parent's Paint() call.
    ///
    /// 2. <c>CssBox.Paint</c> used to always treat block-level boxes (Rectangles.Count == 0) as
    ///    visible regardless of the current page's clip rect, so every page walked the entire box
    ///    tree. Fixed to prune using the box's own Bounds instead - but only when the document has no
    ///    out-of-flow content anywhere (see HtmlContainerInt.HasOutOfFlowBoxes), since an out-of-flow
    ///    descendant's visual position can fall outside its "invisible" ancestor's own Bounds.
    ///
    /// Both fixes are scoped to fall back to the original (slower but unconditionally correct)
    /// behaviour whenever the document has any float/absolute/fixed content, so the riskiest case to
    /// regression-test is exactly that: out-of-flow content nested deep inside plain wrapper boxes.
    /// </summary>
    public class StackingContextPaintRegressionTests
    {
        [Fact]
        public async Task PositionAbsolute_DeeplyNestedInPlainWrappers_StillRendersContent()
        {
            // Six levels of plain (non-positioned, non-stacking-context) wrapper divs, with a
            // position:absolute box buried at the bottom. Its containing block is the outermost
            // (position:relative) wrapper, not its immediate DOM parent - it must still be discovered
            // and painted via FlattenStackingContext's out-of-flow hoisting, not silently dropped.
            var html = @"
<!DOCTYPE html>
<html>
<head><style>
    .positioned-root { position: relative; width: 400px; height: 300px; }
    .plain { }
</style></head>
<body>
    <div class='positioned-root'>
        <div class='plain'><div class='plain'><div class='plain'>
            <div class='plain'><div class='plain'><div class='plain'>
                <div style='position:absolute; top:10px; left:10px; width:50px; height:50px; background:red;'></div>
            </div></div></div>
        </div></div></div>
    </div>
</body>
</html>";

            var generator = new PdfGenerator();
            var document = await generator.GeneratePdf(html, PageSize.A4, margin: 20);

            Assert.Equal(1, document.PageCount);
            Assert.True(PageHasContent(document.Pages[0]), "the single page should have content");
        }

        [Fact]
        public async Task ZIndexedPositionedSiblings_RenderWithoutThrowing()
        {
            // Two position:relative siblings with different z-index values are each their own
            // stacking context (per IsStackingContextBox) - confirm they're still found and painted
            // (not excluded as "already handled elsewhere") now that FlattenStackingContext no longer
            // blindly recurses through everything.
            var html = @"
<!DOCTYPE html>
<html>
<head><style>
    .box { position: relative; width: 100px; height: 100px; }
</style></head>
<body>
    <div class='box' style='z-index: 1; background: red;'>Back</div>
    <div class='box' style='z-index: 2; top: -50px; left: 50px; background: blue;'>Front</div>
</body>
</html>";

            var generator = new PdfGenerator();
            var document = await generator.GeneratePdf(html, PageSize.A4, margin: 20);

            Assert.Equal(1, document.PageCount);
            Assert.True(PageHasContent(document.Pages[0]));
        }

        [Fact]
        public async Task MultiPageRepeatedContent_EveryPageHasContent()
        {
            // Regression for the Bounds-based visibility pruning: with many pages of distinct
            // repeated content, every single page must still have content - none should come out
            // blank because an ancestor spanning many pages was wrongly judged invisible for a page
            // it does, in fact, have content on.
            var html = BuildRepeatedPagedSectionsHtml(sectionCount: 30);

            var generator = new PdfGenerator();
            var document = await generator.GeneratePdf(html, PageSize.A4, margin: 20);

            Assert.True(document.PageCount >= 5, $"expected several pages, got {document.PageCount}");

            for (var i = 0; i < document.PageCount; i++)
            {
                Assert.True(PageHasContent(document.Pages[i]), $"page {i + 1} of {document.PageCount} should have content");
            }
        }

        [Fact]
        public async Task ManyNestedNormalFlowBoxes_PaintWorkIsIndependentOfNestingDepth()
        {
            // Deterministic (machine-speed-independent) regression guard for FlattenStackingContext's
            // old O(depth) blowup: with no out-of-flow content anywhere, a single paint pass used to
            // re-paint every box once per ancestor level between it and the root, so the total paint
            // work scaled with nesting depth.
            //
            // The wrapper divs here are empty (no border/background/text), so they emit no draw
            // operations of their own — painting the *same* section content at nesting depth 1 vs
            // depth 8 must therefore emit the exact same number of draw operations. A regression back
            // to per-ancestor repainting would multiply the deep count by roughly the nesting depth.
            //
            // This replaced an earlier wall-clock bound (`elapsed < 5000ms`), which flaked on slow or
            // contended CI runners: a non-regressed render on a busy Windows agent could legitimately
            // exceed the bound. Counting draw operations is exact and independent of how fast the
            // machine is, while still catching the algorithmic regression the timing bound targeted.
            const int sections = 40;
            var shallow = await CountDrawOperationsAsync(BuildDeeplyNestedRepeatedSectionsHtml(sections, nestingDepth: 1));
            var deep = await CountDrawOperationsAsync(BuildDeeplyNestedRepeatedSectionsHtml(sections, nestingDepth: 8));

            Assert.True(shallow > 0, "the document should paint some content");
            Assert.Equal(shallow, deep);
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static string BuildRepeatedPagedSectionsHtml(int sectionCount)
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><style>");
            sb.Append("@page { size: A4; margin: 20mm; }");
            sb.Append(".section { page-break-after: always; border: 1px solid black; padding: 8px; }");
            sb.Append("</style></head><body>");

            for (var i = 0; i < sectionCount; i++)
            {
                sb.Append($"<div class='section'><h2>Section {i}</h2><p>Distinct content for section number {i}.</p></div>");
            }

            sb.Append("</body></html>");
            return sb.ToString();
        }

        private static string BuildDeeplyNestedRepeatedSectionsHtml(int sectionCount, int nestingDepth)
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><style>");
            sb.Append(".section { border: 1px solid black; padding: 4px; margin-bottom: 8px; }");
            sb.Append("</style></head><body>");

            for (var i = 0; i < sectionCount; i++)
            {
                sb.Append("<div class='section'>");
                for (var d = 0; d < nestingDepth; d++)
                {
                    sb.Append("<div>");
                }
                sb.Append($"Section {i} content deep inside {nestingDepth} wrapper levels.");
                for (var d = 0; d < nestingDepth; d++)
                {
                    sb.Append("</div>");
                }
                sb.Append("</div>");
            }

            sb.Append("</body></html>");
            return sb.ToString();
        }

        /// <summary>
        /// Lays out <paramref name="html"/> and paints the whole box tree once through a graphics
        /// stub that counts every draw operation. Painting the root directly (with an all-encompassing
        /// clip) exercises the exact <see cref="PeachPDF.Html.Core.Utils.DomUtils"/> stacking-flatten
        /// path the regression lived in, so the returned count is the total paint work for the document.
        /// </summary>
        private static async Task<int> CountDrawOperationsAsync(string html)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using (var layoutGraphics = new GraphicsAdapter(adapter, measure, 1.0))
            {
                await container.PerformLayout(layoutGraphics);
            }

            var counter = new DrawCountingGraphics(adapter);
            if (container.Root is not null)
            {
                container.Root.ResetPaint();
                await container.Root.Paint(counter);
            }

            return counter.DrawOperations;
        }

        private static bool PageHasContent(PdfPage page)
        {
            try
            {
                var content = page.Contents;
                if (content == null)
                    return false;

                if (content.Elements.Count == 0)
                    return false;

                foreach (var item in content.Elements)
                {
                    if (item is PdfReference { Value: PdfDictionary dict })
                    {
                        var stream = dict.Stream;
                        if (stream?.Value is { Length: > 0 })
                            return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// An <see cref="RGraphics"/> stub that counts every draw call and no-ops everything else,
        /// with an unbounded clip so no box is pruned as off-page. Mirrors the recording-graphics
        /// pattern used elsewhere (e.g. <c>TaggedPdfPaintOrderTests</c>), including returning a real
        /// (no-op) path from <see cref="GetGraphicsPath"/> so border/marker painting doesn't NRE.
        /// </summary>
        private sealed class DrawCountingGraphics : RGraphics
        {
            public int DrawOperations { get; private set; }

            public DrawCountingGraphics(RAdapter adapter)
                : base(adapter, new RRect(0, 0, double.MaxValue, double.MaxValue)) { }

            public override void DrawString(string str, RFont font, RColor color, RPoint point, RSize size, bool rtl, double letterSpacing = 0, RFontPalette? fontPalette = null) => DrawOperations++;
            public override void DrawLine(RPen pen, double x1, double y1, double x2, double y2) => DrawOperations++;
            public override void DrawRectangle(RPen pen, double x, double y, double width, double height) => DrawOperations++;
            public override void DrawRectangle(RBrush brush, double x, double y, double width, double height) => DrawOperations++;
            public override void DrawImage(RImage image, RRect destRect, RRect srcRect) => DrawOperations++;
            public override void DrawImage(RImage image, RRect destRect) => DrawOperations++;
            public override void DrawImageMasked(RImage image, RImage maskImage, RRect destRect) => DrawOperations++;
            public override void DrawImageWithOpacity(RImage image, RRect destRect, double opacity) => DrawOperations++;
            public override void DrawPath(RPen pen, RGraphicsPath path) => DrawOperations++;
            public override void DrawPath(RBrush brush, RGraphicsPath path) => DrawOperations++;
            public override void DrawPolygon(RBrush brush, RPoint[] points) => DrawOperations++;

            public override void PushTransform(RMatrix matrix) { }
            public override void PopTransform() { }
            public override void PushClip(RRect rect) => _clipStack.Push(rect);
            public override void PushClip(RGraphicsPath path) => _clipStack.Push(_clipStack.Peek());
            public override void PopClip() { if (_clipStack.Count > 1) _clipStack.Pop(); }
            public override void PushClipExclude(RRect rect) { }
            public override object SetAntiAliasSmoothingMode() => new object();
            public override void ReturnPreviousSmoothingMode(object? prevMode) { }
            public override RGraphicsPath GetGraphicsPath() => new NoOpGraphicsPath();

            public override RGraphicsPath? GetTextOutline(string str, RFont font, RPoint baselineOrigin, double letterSpacing = 0) => null;
            public override (RGraphics Graphics, RImage Image)? CreateTile(double width, double height) => null;
            public override void BeginMarkedContent(string structureType, int mcid) { }
            public override void EndMarkedContent() { }
            public override void BeginArtifact() { }
            public override RSize MeasureString(string str, RFont font) => new(10, 12);
            public override void MeasureString(string str, RFont font, double maxWidth, out int charFit, out double charFitWidth)
            {
                charFit = str?.Length ?? 0;
                charFitWidth = maxWidth;
            }
            public override void Dispose() { }
        }

        private sealed class NoOpGraphicsPath : RGraphicsPath
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
            public override void Dispose() { }
        }
    }
}
