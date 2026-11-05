using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;

namespace PeachBrackets.Engine.Tests.Html.Core
{
    /// <summary>
    /// <c>align-items: center</c> centres an item on the cross axis even when it is taller than the container: the overflow is split between the two sides
    /// (CSS Box Alignment 3: the default overflow alignment is "unsafe"; only the <c>safe</c> keyword keeps it from overflowing the start edge). A site header
    /// is exactly that: a 40-pixel flex bar with a 65-pixel logo link in it that sticks out above and below.
    /// </summary>
    public class FlexOversizedItemCenteringTests
    {
        private static CssBox? FindById(CssBox box, string id)
        {
            if (box.HtmlTag?.TryGetAttribute("id", "") == id)
                return box;

            foreach (var child in box.Boxes)
            {
                if (FindById(child, id) is { } found)
                    return found;
            }

            return null;
        }

        private static async Task<(CssBox Bar, CssBox Item)> LayOutAsync(string barStyle, string itemStyle)
        {
            var html = $@"<!DOCTYPE html>
<html><head><style>
body {{ margin: 0 }}
#bar {{ {barStyle} }}
#item {{ {itemStyle} }}
</style></head>
<body><div style='height:100pt'></div><header id='bar'><a id='item'><svg width='100' height='65' viewBox='0 0 100 65'><rect width='100' height='65'/></svg></a><nav>Menu</nav></header></body></html>";

            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext);
            await container.SetHtml(html, null);

            var size = new Size(595, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);

            return (FindById(container.Root!, "bar")!, FindById(container.Root!, "item")!);
        }

        [Fact]
        public async Task ATallerItem_IsCentredOnABarWithAFixedHeight_AndStaysCentredWhenItOverflows()
        {
            var (bar, item) = await LayOutAsync("display:flex; height:40px; align-items:center; justify-content:space-between", "display:block");

            var barCenter = bar.Location.Y + ((bar.ActualBottom - bar.Location.Y) / 2);
            var itemCenter = item.Location.Y + ((item.ActualBottom - item.Location.Y) / 2);

            Assert.Equal(30, bar.ActualBottom - bar.Location.Y, 0.5);
            Assert.Equal(barCenter, itemCenter, 0.5);
            Assert.True(item.Location.Y < bar.Location.Y, "the item sticks out above the bar by half the overflow");
        }

        // The same bar as a site writes it: sticky, with margins, and a link whose first child is a visually hidden (absolutely positioned) label.
        private static async Task<(CssBox Bar, CssBox Item, CssBox Svg)> LayOutSiteHeaderAsync(string barExtra)
        {
            var html = $@"<!DOCTYPE html>
<html><head><style>
body {{ margin: 0 }}
.sr-only {{ position: absolute; width: 1px; height: 1px; padding: 0; margin: -1px; overflow: hidden; clip: rect(0,0,0,0); white-space: nowrap; border-width: 0 }}
header {{ display: flex; flex-direction: row; flex-wrap: nowrap; align-items: center; justify-content: space-between; height: 40px; max-width: 72rem;
          margin: 40px auto; padding: 0 20px; background: black; position: sticky; top: 0; {barExtra} }}
svg {{ height: 65px; width: 197px }}
</style></head>
<body><header id='bar'><a id='item' href='/'><span class='sr-only'>Home</span><svg id='logo' viewBox='0 0 100 33'><rect width='100' height='33'/></svg></a><nav>Menu</nav></header></body></html>";

            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext);
            await container.SetHtml(html, null);

            var size = new Size(595, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);

            return (FindById(container.Root!, "bar")!, FindById(container.Root!, "item")!, FindById(container.Root!, "logo")!);
        }

        [Theory]
        [InlineData("")]
        [InlineData("position: static")]
        public async Task ASiteHeadersLogoLink_IsCentredOnItsBar(string barExtra)
        {
            var (bar, item, svg) = await LayOutSiteHeaderAsync(barExtra);

            var barCenter = bar.Location.Y + ((bar.ActualBottom - bar.Location.Y) / 2);
            var itemCenter = item.Location.Y + ((item.ActualBottom - item.Location.Y) / 2);
            Assert.Equal(barCenter, itemCenter, 0.75);

            // And the logo is inside the link, which is as tall as the logo (plus what its line box adds), not pushed down inside it.
            // Where the logo is drawn is where its word was put (an inline replaced box has no rectangle of its own).
            var logo = svg.Words.First();
            var detail = $"bar {bar.Location.Y}..{bar.ActualBottom}, link {item.Location.Y}..{item.ActualBottom}, logo {logo.Top}..{logo.Top + logo.Height}";
            Assert.True(logo.Top >= item.Location.Y - 0.5, detail);
            Assert.True(logo.Top - item.Location.Y < 6, detail);
        }

        [Fact]
        public async Task AnItemThatFits_IsCentredAsBefore()
        {
            var (bar, item) = await LayOutAsync("display:flex; height:120px; align-items:center", "display:block");

            var barCenter = bar.Location.Y + ((bar.ActualBottom - bar.Location.Y) / 2);
            var itemCenter = item.Location.Y + ((item.ActualBottom - item.Location.Y) / 2);

            Assert.Equal(barCenter, itemCenter, 0.5);
        }
    }
}
