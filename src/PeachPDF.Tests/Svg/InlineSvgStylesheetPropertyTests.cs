using PeachDrawing.Core;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PDF.Drawing;
using PeachPDF.PdfBackend;
using PeachPDF.Svg;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// The same stylesheet properties as <see cref="SvgStylesheetPropertyTests"/>, through an inline <c>&lt;svg&gt;</c> whose rules live in the
    /// host document's own <c>&lt;style&gt;</c> (the HTML cascade, not the standalone SVG's).
    /// </summary>
    public class InlineSvgStylesheetPropertyTests
    {
        private static async Task<SvgDocument> BuildAsync(string css, string svgBody)
        {
            var html = $"<!DOCTYPE html><html><head><style>{css}</style></head><body><svg width=\"200\" height=\"100\">{svgBody}</svg></body></html>";
            var renderContext = new PdfRenderContext();
            var container = new HtmlContainerInt(renderContext);
            await container.SetHtml(html, null);

            var size = new Size(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.ToPixels(size, 1.0);

            var measure = PdfGraphics.CreateMeasureContext(size);
            using var graphics = new PdfCanvas(renderContext, measure, 1.0);
            await container.PerformLayout(graphics);

            var svgBox = Assert.IsType<CssBoxSvg>(DomUtils.GetBoxByTagName(container.Root!, "svg"));
            return svgBox.Document!;
        }

        [Fact]
        public async Task UnitlessStrokeWidthAndDashoffset_ReachAnInlineElement()
        {
            var document = await BuildAsync("circle { stroke-width: 6; stroke-dashoffset: 7 } rect { stroke-width: 2px; stroke-dashoffset: -3 }",
                "<circle cx=\"5\" cy=\"5\" r=\"3\"/><rect width=\"10\" height=\"10\"/>");

            var circle = Assert.IsType<SvgCircleElement>(document.Children[0]);
            var rect = Assert.IsType<SvgRectElement>(document.Children[1]);

            Assert.Equal(6, circle.StrokeWidth);
            Assert.Equal(7, circle.StrokeDashOffset);
            Assert.Equal(2, rect.StrokeWidth);
            Assert.Equal(-3, rect.StrokeDashOffset);
        }

        [Fact]
        public async Task AStopRuleInTheHostDocument_ReachesAGradientStop()
        {
            var document = await BuildAsync("stop { stop-color: #0000ff } stop.dim { stop-opacity: .5 }",
                "<defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop class=\"dim\" offset=\"1\" stop-color=\"#ff0000\"/></linearGradient></defs>");

            var stops = document.Gradients["g"].Stops;

            Assert.Equal(PaintColor.FromArgb(255, 0, 0, 255), stops[0].PaintColor);
            Assert.Equal(0, stops[1].PaintColor.R);
            Assert.Equal(255, stops[1].PaintColor.B);
            Assert.InRange(stops[1].PaintColor.A, 127, 128);
        }

        [Fact]
        public async Task AStopColorOfCurrentcolor_IsTheColourOfTheHost()
        {
            // Chrome: `stop { stop-color: currentcolor }` is the stop's `color`, inherited from the svg (here the body's red).
            var document = await BuildAsync("body { color: #336699 } stop { stop-color: currentcolor; stop-opacity: .5 }",
                "<defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#0000ff\"/></linearGradient></defs>");

            var color = document.Gradients["g"].Stops[0].PaintColor;

            Assert.Equal(0x33, color.R);
            Assert.Equal(0x66, color.G);
            Assert.Equal(0x99, color.B);
            Assert.InRange(color.A, 127, 128);
        }

        [Fact]
        public async Task ACssTransform_WithUnits_IsReadOnAnInlineElement()
        {
            var document = await BuildAsync("rect { transform: translateX(30px) rotate(0.25turn) }", "<rect width=\"10\" height=\"10\"/>");

            var transform = document.Children.Single().Transform!.Value;

            // translateX(30px) rotate(0.25turn): Chrome's matrix(0, 1, -1, 0, 30, 0).
            Assert.Equal(0, transform.M11, 3);
            Assert.Equal(1, transform.M12, 3);
            Assert.Equal(-1, transform.M21, 3);
            Assert.Equal(0, transform.M22, 3);
            Assert.Equal(30, transform.M31, 3);
            Assert.Equal(0, transform.M32, 3);
        }
    }
}
