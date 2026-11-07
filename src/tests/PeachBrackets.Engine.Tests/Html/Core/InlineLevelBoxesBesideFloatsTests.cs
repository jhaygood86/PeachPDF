using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;

namespace PeachBrackets.Engine.Tests.Html.Core
{
    /// <summary>
    /// The footer of slashdot.org: a full-width bar float, an inline-block quote with side margins, a footer float that clears it, and rows of links that wrap.
    /// Each expectation is what Chromium reports for the same markup (1px is 0.75pt; font sizes are 13px Arial, so the tests that depend on a metric only
    /// assert a relation).
    /// </summary>
    public class InlineLevelBoxesBesideFloatsTests
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

        private static CssRect? FirstWord(CssBox box)
        {
            foreach (var word in box.Words)
            {
                if (!word.IsSpaces && !word.IsLineBreak) return word;
            }

            foreach (var child in box.Boxes)
            {
                if (FirstWord(child) is { } found) return found;
            }

            return null;
        }

        private static async Task<CssBox> LayOutAsync(string body, string style = "")
        {
            var html = $"<!DOCTYPE html><html><head><style>body {{ margin: 0; font: 13px Arial, sans-serif }} {style}</style></head><body>{body}</body></html>";
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

        private const string Style = ".bar { float: left; width: 1174px; height: 36px } .q { display: inline-block; min-width: 478px; margin-left: 120px; margin-right: 336px } .q blockquote { width: 100%; margin: 0; float: left }";

        [Fact]
        public async Task AnInlineBlocksTopMargin_DoesNotMoveTheFloatBeforeIt()
        {
            // Chromium: the float is at 0, the inline-block below it at 36 + 19 = 55. A margin of an inline-level box never collapses with its parent's (§8.3.1),
            // so it cannot move the anonymous block holding both, and the float with it.
            var root = await LayOutAsync("<div class='bar' id='f'></div><section class='q' id='q' style='margin-top:19px'><blockquote><p>x</p></blockquote></section>", Style);

            Assert.Equal(0, Find(root, "f")!.Location.Y, 0.5);
            Assert.Equal(36 + 19, Find(root, "q")!.Location.Y / 0.75, 1);
        }

        [Fact]
        public async Task AnInlineBlockTooWideBesideAFloat_GoesBelowIt_AtItsOwnLeftMargin()
        {
            // Chromium: x = 120 (its margin), y = 36 (below the bar): CSS 2.1 section 9.5, the shortened line is shifted down until the box fits.
            var root = await LayOutAsync("<div class='bar'></div><section class='q' id='q'><blockquote><p>x</p></blockquote></section>", Style);

            var q = Find(root, "q")!;
            Assert.Equal(120, q.Location.X / 0.75, 0.5);
            Assert.Equal(36, q.Location.Y / 0.75, 0.5);
        }

        [Fact]
        public async Task ALeftFloatAfterAnInlineBlockItDoesNotFitBeside_GoesBelowTheLine()
        {
            // Chromium: the footer float, 1157px wide, goes below the line of the quote (at 36 + 19 + the quote's height + the strut), not onto it.
            var root = await LayOutAsync(
                "<div class='bar'></div><section class='q' id='q' style='margin-top:19px'><blockquote><p style='margin:0'>x</p></blockquote></section><div id='ft' style='float:left;width:1157px;clear:both'>footer</div>", Style);

            var q = Find(root, "q")!;
            var ft = Find(root, "ft")!;
            Assert.True(ft.Location.Y >= q.Location.Y + q.ActualBoxSizingHeight - 0.5,
                $"the footer is at {ft.Location.Y / 0.75:0.#}px, over the quote that ends at {(q.Location.Y + q.ActualBoxSizingHeight) / 0.75:0.#}px");
        }

        [Fact]
        public async Task ARelativeOffsetOnAnInlineBlock_MovesItsWords_AndNotTheLine()
        {
            // Chromium: left: 20px; top: 10px moves the box and its text by that much; the line it sits in is as tall as before.
            var plain = await LayOutAsync("<div id='d'>a <span id='s' style='display:inline-block;width:60px;height:12px'>ib</span> b</div><div id='after'>x</div>");
            var moved = await LayOutAsync("<div id='d'>a <span id='s' style='position:relative;left:20px;top:10px;display:inline-block;width:60px;height:12px'>ib</span> b</div><div id='after'>x</div>");

            var before = FirstWord(Find(plain, "s")!)!;
            var after = FirstWord(Find(moved, "s")!)!;
            Assert.Equal(before.Left + 15, after.Left, 0.5);
            Assert.Equal(before.Top + 7.5, after.Top, 0.5);
            Assert.Equal(Find(plain, "after")!.Location.Y, Find(moved, "after")!.Location.Y, 0.01);
        }

        [Fact]
        public async Task ARelativeOffsetOnAnInlineBox_MovesItsText()
        {
            var plain = await LayOutAsync("<div>a <span id='s'>word</span> b</div>");
            var moved = await LayOutAsync("<div>a <span id='s' style='position:relative;left:20px;top:-3px'>word</span> b</div>");

            var before = FirstWord(Find(plain, "s")!)!;
            var after = FirstWord(Find(moved, "s")!)!;
            Assert.Equal(before.Left + 15, after.Left, 0.5);
            Assert.Equal(before.Top - 2.25, after.Top, 0.5);
        }

        [Fact]
        public async Task AnInlineBlockAloneOnItsLine_HasTheStrutsDescentBelowIt()
        {
            // Chromium: a 30px inline-block with nothing else on its line is a 33px line at 13px Arial (the strut hangs below the baseline the box sits on).
            var root = await LayOutAsync("<div id='d'><span style='display:inline-block;width:60px;height:30px'></span></div>");

            Assert.True(Find(root, "d")!.ActualBoxSizingHeight / 0.75 > 31, "the line ends at the box's own bottom edge");
        }

        [Fact]
        public async Task ABoxWithNoLineBoxOfItsOwn_HasTheStrutsDescentBelowIt()
        {
            var root = await LayOutAsync("<div id='d'><span style='display:inline-block;width:60px'><div style='height:30px'></div></span></div>");

            Assert.True(Find(root, "d")!.ActualBoxSizingHeight / 0.75 > 31, "the line ends at the box's own bottom edge");
        }

        [Fact]
        public async Task TheMarginOfAnInlineBoxWhoseFirstWordWrapsToANewLine_StartsThatLine()
        {
            // <li style="display:inline; margin: 0 10px"><a>Fourth</a></li>: Chromium puts the text of the first word of a line at the li's margin (10px), as on the
            // line before. The li holds an empty anonymous box in front of the a, which made the a not "first" and dropped the margin.
            var root = await LayOutAsync(
                "<ul style='margin:0;padding:0;width:200px'>"
                + "<li style='list-style:none;display:inline;margin:0 10px'><a id='w1'>First</a></li>\n<li style='list-style:none;display:inline;margin:0 10px'><a id='w2'>Second</a></li>\n"
                + "<li style='list-style:none;display:inline;margin:0 10px'><a id='w3'>Third</a></li>\n<li style='list-style:none;display:inline;margin:0 10px'><a id='w4'>Fourth</a></li></ul>");

            var first = FirstWord(Find(root, "w1")!)!;
            var wrapped = FirstWord(Find(root, "w4")!)!;
            Assert.True(wrapped.Top > first.Top + 1, "the fourth item is expected on a second line");
            Assert.Equal(first.Left, wrapped.Left, 0.5);
        }

        [Fact]
        public void ASvgBackgroundWithOnlyARatio_IsFittedToTheArea_NotItsViewBoxSize()
        {
            // slashdot.org's logo: a 374 x 53.5 viewBox as the background of a 138 x 20 box, at `background-size: auto`: Chromium draws it 138 wide, 19.7 high.
            var (width, height) = BackgroundLayerResolver.ResolveSize("auto", 138, 20, null, null, 374.3 / 53.5, new CssBox(null, null));

            Assert.Equal(138, width, 0.01);
            Assert.Equal(138 / (374.3 / 53.5), height, 0.01);
        }

        [Fact]
        public void ASvgBackgroundWithOnlyARatio_ThatIsTallerThanTheArea_IsFittedByItsHeight()
        {
            var (width, height) = BackgroundLayerResolver.ResolveSize("auto", 100, 20, null, null, 2.0, new CssBox(null, null));

            Assert.Equal(40, width, 0.01);
            Assert.Equal(20, height, 0.01);
        }
    }
}
