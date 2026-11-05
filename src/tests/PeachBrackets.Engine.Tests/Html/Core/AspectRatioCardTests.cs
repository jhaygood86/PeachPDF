using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;

namespace PeachBrackets.Engine.Tests.Html.Core
{
    /// <summary>
    /// A news-site card: a box whose height comes from <c>aspect-ratio: 16 / 9</c> and its width, holding a link with an absolutely positioned image.
    /// The box must be as tall as the ratio says (CSS Sizing 4 section 5), wherever it sits, including in a flex row whose items are half the row.
    /// </summary>
    public class AspectRatioCardTests
    {
        private const string Picture = "data:image/svg+xml;utf8,%3Csvg xmlns='http://www.w3.org/2000/svg' width='768' height='432'%3E%3Crect width='768' height='432' fill='red'/%3E%3C/svg%3E";

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

        private static async Task<CssBox> LayOutAsync(string body, string id)
        {
            var html = $@"<!DOCTYPE html><html><head><style>
body {{ margin: 0 }}
.aspect-video {{ aspect-ratio: 16 / 9 }} .relative {{ position: relative }} .block {{ display: block }} .w-full {{ width: 100% }} .overflow-hidden {{ overflow: hidden }}
.absolute {{ position: absolute }} .min-w-full {{ min-width: 100% }} .min-h-full {{ min-height: 100% }} .h-auto {{ height: auto }}
.row {{ display: flex; flex-direction: row-reverse; width: 600pt }} .half {{ width: 50% }}
img {{ max-width: 100%; height: auto; display: block; vertical-align: middle }}
</style></head><body>{body}</body></html>";

            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext) { MediaType = MediaType.Screen };
            await container.SetHtml(html, null);

            var size = new Size(800, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);

            return Find(container.Root!, id)!;
        }

        private static string Card => $"<div id='av' class='relative block aspect-video w-full overflow-hidden'><a href='#'><img class='absolute min-w-full min-h-full h-auto' src=\"{Picture}\"></a></div>";

        [Fact]
        public async Task InABlock_TheBoxIsAsTallAsTheRatioSays()
        {
            var box = await LayOutAsync($"<div style='width: 300pt'>{Card}</div>", "av");

            Assert.Equal(300, box.ActualRight - box.Location.X, 0.5);
            Assert.Equal(168.75, box.ActualBottom - box.Location.Y, 0.5);
        }

        [Fact]
        public async Task TheAbsoluteImageWithNoOffsets_SitsAtTheTopOfTheBox_WhereItsStaticPositionIs()
        {
            var box = await LayOutAsync($"<div style='width: 300pt'>{Card}</div>", "av");
            var word = Descendant(box, "img").Words.First();

            Assert.Equal(box.Location.Y, word.Top, 0.5);
            Assert.Equal(box.Location.X, word.Left, 0.5);
            Assert.Equal(300, word.Width, 0.5);
            Assert.Equal(168.75, word.Height, 0.5);
        }

        private static CssBox Descendant(CssBox box, string tag)
        {
            foreach (var child in box.Boxes)
            {
                if (child.HtmlTag?.Name == tag) return child;
                if (Descendant(child, tag) is { } found) return found;
            }

            return null!;
        }

        [Fact]
        public async Task AsAHalfWidthItemOfAReversedFlexRow_TheBoxIsAsTallAsTheRatioSays()
        {
            var box = await LayOutAsync($"<div class='row'><div class='half'>{Card}</div><div class='half'><p>list</p></div></div>", "av");

            Assert.Equal(300, box.ActualRight - box.Location.X, 0.5);
            Assert.Equal(168.75, box.ActualBottom - box.Location.Y, 0.5);
        }

        [Fact]
        public async Task InsideAnOverflowHiddenFlexColumnItem_TheBoxIsAsTallAsTheRatioSays()
        {
            var box = await LayOutAsync($"<div class='row'><div class='half'><div style='overflow:hidden'>{Card}<div style='display:flex;flex-direction:column;justify-content:space-between;padding-top:12pt'><h2>Title</h2></div></div></div><div class='half'><p>list</p></div></div>", "av");

            Assert.Equal(300, box.ActualRight - box.Location.X, 0.5);
            Assert.Equal(168.75, box.ActualBottom - box.Location.Y, 0.5);
        }
    }
}
