using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;

namespace PeachBrackets.Engine.Tests.Html.Core
{
    /// <summary>
    /// osnews.com's navigation: <c>nav { float: left } ul.clearfix li { float: left }</c> with a clearing block (<c>.clearfix:after</c>) after the items. Two defects
    /// made it a column: a shrink-to-fit box measured its floats as separate lines whenever a block-level sibling was present, so it was as wide as the widest item
    /// rather than the row of them; and an item's outside marker (even with <c>list-style: none</c>) added its margin to the item's width, so every item was five
    /// pixels wider than its text. Chromium sizes the row below as the sum of its items.
    /// </summary>
    public class FloatedMenuMeasureTests
    {
        private static CssBox? Find(CssBox box, string id)
        {
            if (box.HtmlTag?.TryGetAttribute("id") == id) return box;

            foreach (var child in box.Boxes)
            {
                if (Find(child, id) is { } found) return found;
            }

            return null;
        }

        private static async Task<CssBox> LayOutAsync(string body)
        {
            var html = $"<!DOCTYPE html><html><head><style>body {{ margin: 0; font: 14px Arial, sans-serif }} ul {{ margin: 0; padding: 0; list-style: none }} li {{ float: left }} li a {{ display: inline-block; padding: 0 25px }} .cf:after {{ content: ' '; display: table; clear: both }}</style></head><body>{body}</body></html>";
            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext) { MediaType = MediaType.Screen };
            await container.SetHtml(html, null);

            var size = new Size(900, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);
            return container.Root!;
        }

        private static double Width(CssBox box) => (box.ActualRight - box.Location.X) / 0.75;

        [Theory]
        [InlineData("<nav id='n' style='float:left'><ul id='u'><li id='a'><a>Home</a></li><li id='b'><a>Originals</a></li></ul></nav>")]
        [InlineData("<nav id='n' style='float:left'><ul id='u' class='cf'><li id='a'><a>Home</a></li><li id='b'><a>Originals</a></li></ul></nav>")]
        [InlineData("<nav id='n' style='float:left'><ul id='u'><li id='a'><a>Home</a></li><li id='b'><a>Originals</a></li><div style='height:0'></div></ul></nav>")]
        [InlineData("<nav id='n' style='float:left'><ul id='u'><li id='a'><a>Home</a></li><li id='b'><a>Originals</a></li><div style='clear:both;height:0'></div></ul></nav>")]
        public async Task AShrinkToFitBoxOfFloatedItems_IsAsWideAsTheRowOfThem_WhateverBlockFollows(string body)
        {
            var root = await LayOutAsync(body);

            var items = Width(Find(root, "a")!) + Width(Find(root, "b")!);
            Assert.Equal(items, Width(Find(root, "n")!), 0.6);
            Assert.True(Find(root, "a")!.Location.Y == Find(root, "b")!.Location.Y, "the items are expected on one row");
        }

        [Fact]
        public async Task AFloatedListItem_IsAsWideAsItsText_WithListStyleNone()
        {
            // Chromium: 37.4px for "Home" in 14px Arial, the same as a floated div. The marker's margin (5px) was counted in the item's width.
            var root = await LayOutAsync("<div id='d' style='float:left;clear:left'>Home</div><ul id='u' style='float:left;clear:left'><li id='l'>Home</li></ul>");

            Assert.Equal(Width(Find(root, "d")!), Width(Find(root, "l")!), 0.1);
        }

        [Fact]
        public async Task ALoneFloatBetweenBlocks_IsStillOneLineAmongTheirs()
        {
            // Not a row: one float, between two blocks (the acid2 eyes). The container is as wide as the widest of the lines.
            var root = await LayOutAsync("<div id='c' style='float:left'><div style='width:50px'>a</div><div style='float:left;width:120px'>b</div><div style='width:80px'>c</div></div>");

            Assert.Equal(120, Width(Find(root, "c")!), 0.6);
        }
    }
}
