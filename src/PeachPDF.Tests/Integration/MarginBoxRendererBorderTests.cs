using PeachPDF;
using PeachPDF.PdfSharpCore;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A page-margin box's own <c>border</c> (closes #943): painted as four independent edge strokes
    /// (<see cref="PeachPDF.Html.Core.Dom.MarginBoxRenderer.PaintBorder"/>, via
    /// <see cref="PeachPDF.Html.Core.Handlers.BordersDrawHandler.DrawCollapsedSegment"/>'s solid-style
    /// path, which paints a 4-point polygon fill - <c>X Y m / X Y l / X Y l / X Y l / h f</c> in the raw
    /// content stream). Verified the same content-stream-regex way as the sibling background/canvas
    /// tests: a solid-color polygon fill's exact vertex coordinates are unambiguous proof, confirmed
    /// against this library's actual coordinate convention (PDF Y is flipped from document Y - a
    /// "border-top" edge ends up at the HIGH end of the box's own PDF-space Y range).
    /// </summary>
    public class MarginBoxRendererBorderTests
    {
        private static async Task<string> GetPdfText(string html, int margin = 40)
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.Undefined,
                ManualPageWidth = 600,
                ManualPageHeight = 800,
                CompressContentStreams = false,
            };
            config.SetMargins(margin);
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        private static Regex PolygonFillPattern(string colorOp, double x1, double y1, double x2, double y2) =>
            new(Regex.Escape(colorOp) + @"\s*\n" +
                $@"{x1}(\.\d+)? {y1}(\.\d+)? m\s*\n" +
                $@"{x2}(\.\d+)? {y1}(\.\d+)? l\s*\n" +
                $@"{x2}(\.\d+)? {y2}(\.\d+)? l\s*\n" +
                $@"{x1}(\.\d+)? {y2}(\.\d+)? l\s*\nh f");

        [Fact]
        public async Task AllFourEdges_PaintTheirOwnIndependentlyResolvedWidthAndColor()
        {
            // bottom-left/-right pin bottom-center's own slot to start at x:60. width:200pt is the
            // CONTENT-box dimension (css-page-3 §5.3.2) - border is additive on top of it, so with a 6pt
            // left / 2pt right border the box's own OUTER (= border-box, no margin declared) slot is
            // 200+6+2=208pt wide, x:60..268, not 60..260. mB=40 -> doc-space Y 760..800, which this
            // library's PDF Y-flip maps to PDF-space Y 0..40 (Y = pageHeight - docY).
            var pdfText = await GetPdfText(
                "<!DOCTYPE html><html><head><style>@page { @bottom-left { content: \"L\"; width: 20pt; } " +
                "@bottom-center { content: \"x\"; width: 200pt; " +
                "border-top: 4pt solid rgb(255,0,0); border-bottom: 8pt solid rgb(0,0,255); " +
                "border-left: 6pt solid rgb(0,255,0); border-right: 2pt solid rgb(0,0,0); } " +
                "@bottom-right { content: \"R\"; width: 20pt; } }</style></head>" +
                "<body><p>short</p></body></html>");

            // top: full border-box width (60..268), PDF Y from 40 (border-box top) down to 40-4=36.
            Assert.Matches(PolygonFillPattern("1 0 0 rg", 60, 40, 268, 36), pdfText);
            // bottom: full border-box width, PDF Y from 8 (border-box bottom + bottom width) down to 0.
            Assert.Matches(PolygonFillPattern("0 0 1 rg", 60, 8, 268, 0), pdfText);
            // left: PDF X from 60 (border-box left) to 60+6=66, spanning the full box height.
            Assert.Matches(PolygonFillPattern("0 1 0 rg", 60, 40, 66, 0), pdfText);
            // right: PDF X from 268-2=266 to 268 (border-box right).
            Assert.Matches(PolygonFillPattern("0 0 0 rg", 266, 40, 268, 0), pdfText);
        }

        [Fact]
        public async Task UnsetBorderStyle_PaintsNoEdge_EvenWithColorAndWidthDeclared()
        {
            var pdfText = await GetPdfText(
                "<!DOCTYPE html><html><head><style>@page { @bottom-center { content: \"x\"; width: 200pt; " +
                "border-top-width: 10pt; border-top-color: red; } }</style></head>" +
                "<body><p>short</p></body></html>");

            Assert.DoesNotContain("1 0 0 rg", pdfText);
        }

        [Fact]
        public async Task ContentElementMarginBox_PaintsTheRuleOwnBackgroundAndBorder()
        {
            // The content: element() path (HtmlContainerInt.LayoutMarginBoxes / PdfGenerator.PaintElementMarginBoxes)
            // is a separate pipeline from the text-content path above - this exercises that the margin-box
            // RULE's own background/border (not the running element's) is threaded through it too.
            var pdfText = await GetPdfText(
                "<!DOCTYPE html><html><head><style>" +
                "@page { @top-center { content: element(heading); background-color: rgb(255,0,255); border-bottom: 3pt solid black; } }" +
                "h1 { position: running(heading); }" +
                "</style></head><body><h1>Chapter</h1><p>short</p></body></html>");

            // Background: a solid magenta fill somewhere on the page (the margin box's own border-box rect).
            Assert.Contains("1 0 1 rg", pdfText);
            // Border: a black polygon fill (border-bottom).
            Assert.Contains("0 0 0 rg", pdfText);
        }

        [Fact]
        public async Task ContentElementMarginBox_PaintsItsOwnBackground_EvenWhenTheRunningElementNeverResolves()
        {
            // A margin box's background/border paints "unconditionally", independent of content - the
            // text-content path already proves that for an ordinary box with content:none. This is the
            // same guarantee for a content:element(name) box where no position:running(name) exists
            // anywhere in the document at all (a typo, or simply not authored yet): HtmlContainerInt.
            // LayoutMarginBoxes creates no MarginBoxFragment for it in that case, so
            // PdfGenerator.PaintElementMarginBoxes must still find and paint the rule's own
            // background/border by walking the applicable margin rules themselves, not just the
            // fragments that happened to resolve.
            var pdfText = await GetPdfText(
                "<!DOCTYPE html><html><head><style>" +
                "@page { @top-center { content: element(nonexistent); background-color: rgb(255,0,255); } " +
                // A second, ordinary text-content rule on the same page - PaintElementMarginBoxes' second
                // loop must skip this one (it isn't an element() rule) rather than double-painting it.
                "@bottom-center { content: \"footer\"; } }" +
                "</style></head><body><p>short</p></body></html>");

            Assert.Contains("1 0 1 rg", pdfText);
        }
    }
}
