using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;

namespace PeachBrackets.Engine.Tests.Html.Core
{
    /// <summary>
    /// A news card's text block: a column flex container whose one item holds a headline, a paragraph and a byline row ending in an inline-block icon with a negative top
    /// margin. The item is as tall as the sum of what it holds (a flex item is a block formatting context, so its first child's margin stays inside it).
    /// </summary>
    public class FlexItemWithBylineTests
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

        private static async Task<(double Item, double Meta, double MetaBottom, double ItemBottom)> LayOutAsync(string metaInner, string colStyle = "display: flex; flex-direction: column", string itemStyle = "", string? content = null)
        {
            var inner = content ?? ("<div style='position:absolute;left:0;top:-3px;height:5px;width:25%'></div>"
                + "<h2>Three line headline that wraps across lines here</h2><p>Two lines of a description that is long enough to wrap.</p>"
                + "<div id='meta' class='meta'>" + metaInner + "</div>");
            var html = $@"<!DOCTYPE html><html><head><style>
body {{ margin: 0; font-size: 16px; line-height: 24px }}
.col {{ {colStyle}; padding: 0 20px 20px; width: 371px }}
.rel {{ position: relative }}
h2 {{ margin: 12px 0 8px; font-size: 24px; line-height: 1; font-weight: bold }}
p {{ margin: 0 0 8px; font-size: 18px; line-height: 1.2 }}
.meta {{ font-size: 12px; line-height: 16px; text-transform: uppercase }}
.icon {{ display: inline-block; vertical-align: middle; height: 16px; width: 16px; margin: -4px 2px 0 4px }}
</style></head><body><div class='col'><div id='item' class='rel' style='{itemStyle}'>{inner}</div></div></body></html>";
            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext) { MediaType = MediaType.Screen };
            await container.SetHtml(html, null);

            var size = new Size(600, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);

            var item = Find(container.Root!, "item")!;
            var meta = Find(container.Root!, "meta")!;
            const double k = 4.0 / 3.0;
            return ((item.ActualBottom - item.Location.Y) * k, (meta.ActualBottom - meta.Location.Y) * k, meta.ActualBottom * k, item.ActualBottom * k);
        }

        [Fact]
        public async Task TheItemEndsAtTheBottomOfItsLastChild_WhateverTheBylineHolds()
        {
            var (_, _, metaBottom, itemBottom) = await LayOutAsync("<span>JON BRODKIN</span> <span>- 2:08 PM</span> <a href='#'>60<svg class='icon' viewBox='0 0 16 16'><rect width='16' height='16'/></svg></a>");

            Assert.True(Math.Abs(itemBottom - metaBottom) < 1, $"the item ends at {itemBottom:0.#}px but its last child at {metaBottom:0.#}px");
        }

        [Theory]
        [InlineData("display: block", "overflow: hidden")]
        [InlineData("display: flex", "")]
        [InlineData("display: flex; flex-direction: column", "")]
        [InlineData("display: grid", "")]
        public async Task TheItemOfAnyFormattingContext_KeepsItsFirstChildsMarginInside(string col, string item)
        {
            var (_, _, metaBottom, itemBottom) = await LayOutAsync("JON BRODKIN", col, item);

            Assert.True(Math.Abs(itemBottom - metaBottom) < 1, $"[{col} / {item}] the item ends at {itemBottom:0.#}px but its last child at {metaBottom:0.#}px");
        }

        [Theory]
        [InlineData("<h2>One</h2><div id='meta'>x</div>")]
        [InlineData("<div style='margin-top: 12px'>a</div><div id='meta'>x</div>")]
        [InlineData("<div style='margin-top: 12px' id='meta'>x</div>")]
        [InlineData("<div style='height:0;position:absolute'></div><div style='margin-top: 12px' id='meta'>x</div>")]
        public async Task WhichChildrenKeepTheMarginInside(string content)
        {
            var (_, _, metaBottom, itemBottom) = await LayOutAsync("", "display: flex", "", content);

            Assert.True(Math.Abs(itemBottom - metaBottom) < 1, $"[{content}] the item ends at {itemBottom:0.#}px but its last child at {metaBottom:0.#}px");
        }

        [Fact]
        public async Task TheItemEndsAtTheBottomOfItsLastChild_WithPlainText()
        {
            var (_, _, metaBottom, itemBottom) = await LayOutAsync("JON BRODKIN");

            Assert.True(Math.Abs(itemBottom - metaBottom) < 1, $"the item ends at {itemBottom:0.#}px but its last child at {metaBottom:0.#}px");
        }
    }
}
