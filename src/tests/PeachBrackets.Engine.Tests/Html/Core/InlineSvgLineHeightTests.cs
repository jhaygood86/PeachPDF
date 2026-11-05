using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;

namespace PeachBrackets.Engine.Tests.Html.Core
{
    /// <summary>
    /// An inline SVG with a height and an automatic width (a site logo: <c>height: 56px; width: auto; vertical-align: middle; margin-bottom: 16px</c>) is an inline
    /// replaced element: the line it sits in is at least as tall as its margin box, so what follows starts below it (CSS 2.1 section 10.8.1).
    /// </summary>
    public class InlineSvgLineHeightTests
    {
        private static CssBox? Find(CssBox box, string id)
        {
            if (box.HtmlTag?.TryGetAttribute("id") == id)
                return box;

            foreach (var child in box.Boxes)
            {
                if (Find(child, id) is { } found)
                    return found;
            }

            return null;
        }

        private static CssBox? Descendant(CssBox box, string tag)
        {
            foreach (var child in box.Boxes)
            {
                if (child.HtmlTag?.Name == tag) return child;
                if (Descendant(child, tag) is { } found) return found;
            }

            return null;
        }

        private static async Task<(double Top, double SvgTop, double NextTop)> LayOutAsync(string svgStyle, string svgAttributes = "")
        {
            var html = $@"<!DOCTYPE html><html><head><style>body {{ margin: 0 }} p {{ margin: 0 }} svg {{ {svgStyle} }}</style></head>
<body><div id='statement'><a href='#'><svg {svgAttributes} viewBox='0 0 436 144.1'><rect width='436' height='144.1'/></svg></a><p id='next'>text</p></div></body></html>";
            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext) { MediaType = MediaType.Screen };
            await container.SetHtml(html, null);

            var size = new Size(600, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);

            var statement = Find(container.Root!, "statement")!;
            var next = Find(container.Root!, "next")!;
            var svg = Descendant(statement, "svg")!;
            return (statement.Location.Y, svg.Words.First().Top, next.Location.Y);
        }

        [Fact]
        public async Task AnInlineSvgWithAHeightAndAutoWidth_MakesItsLineAsTallAsItsMarginBox()
        {
            // 56px = 42pt tall, 12pt margin below: the paragraph after it starts at least 54pt below the top.
            var (top, svgTop, nextTop) = await LayOutAsync("display: inline; height: 56px; width: auto; vertical-align: middle; margin-bottom: 16px");

            Assert.True(nextTop - top >= 53.5, $"the next block starts {nextTop - top:0.#}pt below the line, which is shorter than the logo");

            // The margin box is the tallest thing on the line, so the logo is at its top (a browser puts it within a pixel of it) and ends above what follows.
            Assert.True(svgTop - top <= 3, $"the logo starts {svgTop - top:0.#}pt below the top of its line");
            Assert.True(svgTop + 42 <= nextTop + 0.5, "the logo runs into the next block");
        }

        [Fact]
        public async Task ALogoSizedByUtilityClassesInRemAndMediaQueries_MakesItsLineAsTallAsItsMarginBox()
        {
            // Tailwind's form of the same logo: h-10 md:h-12 lg:h-14 (2.5rem, 3rem, 3.5rem by width) and mb-6 md:mb-4, on a 600pt (800px) wide screen.
            var html = @"<!DOCTYPE html><html><head><style>
body { margin: 0 } p { margin: 0 }
svg { vertical-align: middle }
.inline { display: inline } .h-10 { height: 2.5rem } .mb-6 { margin-bottom: 1.5rem }
@media (min-width: 768px) { .md\:h-12 { height: 3rem } .md\:mb-4 { margin-bottom: 1rem } }
@media (min-width: 1024px) { .lg\:h-14 { height: 3.5rem } }
</style></head>
<body><div id='statement'><a href='#'><svg class='mb-6 inline h-10 md:mb-4 md:h-12 lg:h-14' viewBox='0 0 436 144.1'><rect width='436' height='144.1'/></svg></a><p id='next'>text</p></div></body></html>";
            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext) { MediaType = MediaType.Screen };
            await container.SetHtml(html, null);

            var size = new Size(900, 842); // 1200px
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);

            var statement = Find(container.Root!, "statement")!;
            var next = Find(container.Root!, "next")!;
            Assert.True(next.Location.Y - statement.Location.Y >= 53.5, $"the next block starts {next.Location.Y - statement.Location.Y:0.#}pt below the top; the logo's margin box is 54pt");
        }

        [Fact]
        public async Task AnInlineSvgWithAHeight_WithoutAMargin_MakesItsLineAsTallAsItself()
        {
            var (top, _, nextTop) = await LayOutAsync("display: inline; height: 56px; width: auto");

            Assert.True(nextTop - top >= 41.5, $"the next block starts {nextTop - top:0.#}pt below the line");
        }
    }
}
