using PeachPDF.Adapters;
using PeachDrawing.Core;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Auto horizontal margins re-resolve against the content width of the page a box lands on
    /// (css-break-3 §5.1) instead of the one the containing block started on. Same <c>@page :first</c>
    /// harness as <see cref="FloatPerPageMeasureIntegrationTests"/>.
    /// </summary>
    public class AutoMarginPerPageMeasureIntegrationTests
    {
        private const double SheetW = 612;
        private const double SheetH = 792;

        private const string Page = """
            @page { margin: 60pt 50pt; }
            @page :first { margin-left: 0; }
            body { margin: 0; }
            div, p { margin: 0; }
            """;

        private static void AssertCentredOnItsOwnPage(HtmlContainerInt container, CssBox body, CssBox box, double width)
        {
            var expected = (container.PageContentRightOf(box.Location.Y) - body.ClientLeft - width) / 2;
            Assert.Equal(expected, box.Location.X - body.ClientLeft, 0.5);
        }

        [Fact]
        public async Task BothAuto_CentresAgainstEachPagesOwnWidth()
        {
            var container = await BuildLayoutAsync($"""
                <!DOCTYPE html><html><head><style>{Page}</style></head><body>
                <div id='a0' style='width:300pt; height:20pt; margin:0 auto'>t</div>
                <p id='pp' style='page-break-before: always'>page one</p>
                <div id='a1' style='width:300pt; height:20pt; margin:0 auto'>t</div>
                </body></html>
                """);
            var body = FindById(container.Root!, "a0")!.ParentBox!;
            var a0 = FindById(container.Root!, "a0")!;
            var a1 = FindById(container.Root!, "a1")!;

            Assert.Equal(0, container.PageIndexOf(a0.Location.Y));
            Assert.Equal(1, container.PageIndexOf(a1.Location.Y));
            AssertCentredOnItsOwnPage(container, body, a0, 300);
            AssertCentredOnItsOwnPage(container, body, a1, 300);
            Assert.Equal(25, a0.Location.X - a1.Location.X, 0.5);
        }

        [Fact]
        public async Task SingleAutoLeft_PushesToEachPagesOwnEnd()
        {
            var container = await BuildLayoutAsync($"""
                <!DOCTYPE html><html><head><style>{Page}</style></head><body>
                <div id='a0' style='width:300pt; height:20pt; margin-left:auto'>t</div>
                <p id='pp' style='page-break-before: always'>page one</p>
                <div id='a1' style='width:300pt; height:20pt; margin-left:auto'>t</div>
                </body></html>
                """);
            var a0 = FindById(container.Root!, "a0")!;
            var a1 = FindById(container.Root!, "a1")!;

            Assert.Equal(1, container.PageIndexOf(a1.Location.Y));
            Assert.Equal(container.PageContentRightOf(a0.Location.Y), a0.Location.X + 300, 0.5);
            Assert.Equal(container.PageContentRightOf(a1.Location.Y), a1.Location.X + 300, 0.5);
        }

        [Fact]
        public async Task OrdinaryPage_IsUnchanged()
        {
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                body { margin: 0; } div { margin: 0; }
                </style></head><body>
                <div id='a0' style='width:300pt; height:20pt; margin:0 auto'>t</div>
                </body></html>
                """);
            var a0 = FindById(container.Root!, "a0")!;
            var body = a0.ParentBox!;
            Assert.Equal((body.AvailableWidth - 300) / 2, a0.Location.X - body.ClientLeft, 0.5);
        }

        private static async Task<HtmlContainerInt> BuildLayoutAsync(string html, double ppp = 1.0)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = ppp };
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            container.PageSize = new Size(
                SheetW * ppp - container.MarginLeft - container.MarginRight,
                SheetH * ppp - container.MarginTop - container.MarginBottom);
            container.Location = new PaintPoint(container.MarginLeft, container.MarginTop);
            container.MaxSize = new Size(container.PageSize.Width, 0);

            var measure = XGraphics.CreateMeasureContext(
                new XSize(container.PageSize.Width, container.PageSize.Height), XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, ppp);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container;
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            if (string.Equals(box.HtmlTag?.TryGetAttribute("id", ""), id, System.StringComparison.OrdinalIgnoreCase))
                return box;

            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found != null) return found;
            }

            return null;
        }
    }
}
