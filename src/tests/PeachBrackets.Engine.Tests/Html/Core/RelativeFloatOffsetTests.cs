using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;

namespace PeachBrackets.Engine.Tests.Html.Core
{
    /// <summary>
    /// A float is offset by <c>position: relative</c> like any other box (CSS 2.1 section 9.4.3); only <c>absolute</c> and <c>fixed</c> take it out of the
    /// flow. A float flowed with inline content was left where it fell: slashdot.org's <c>float: left; position: relative; left: 180px</c> topics bar sat at
    /// the page's left edge. Expected values are what Chromium reports (1px is 0.75pt).
    /// </summary>
    public class RelativeFloatOffsetTests
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

        private static async Task<CssBox> LayOutAsync(string body)
        {
            var html = $"<!DOCTYPE html><html><head><style>body {{ margin: 0; font: 13px Arial }}</style></head><body>{body}</body></html>";
            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext) { MediaType = MediaType.Screen };
            await container.SetHtml(html, null);

            var size = new Size(600, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);
            return container.Root!;
        }

        [Fact]
        public async Task AFloatWithInlineContent_IsMovedByItsRelativeOffset()
        {
            // Chromium: left: 180px puts the float's text at x = 180px.
            var root = await LayOutAsync("<div><div id='f' style='float:left; position:relative; left:180px; display:inline-block'>float rel</div></div>");

            Assert.Equal(135, FirstWord(Find(root, "f")!)!.Left, 0.5);
        }

        [Fact]
        public async Task ARightFloat_IsMovedByItsRelativeOffsetToo()
        {
            var plain = await LayOutAsync("<div><div id='f' style='float:right; width:100px'>x</div></div>");
            var moved = await LayOutAsync("<div><div id='f' style='float:right; width:100px; position:relative; left:-30px'>x</div></div>");

            Assert.Equal(FirstWord(Find(plain, "f")!)!.Left - 22.5, FirstWord(Find(moved, "f")!)!.Left, 0.5);
        }

        [Fact]
        public async Task TheOffset_DoesNotMoveTheTextOfABlockAfterTheFloat()
        {
            // Relative positioning is visual only: the text beside a float flows against the float's static position (60px wide here).
            var root = await LayOutAsync("<div><div id='f' style='float:left; position:relative; left:200px; width:60px; height:20px'>f</div><p id='t' style='margin:0'>beside</p></div>");

            Assert.Equal(45, FirstWord(Find(root, "t")!)!.Left, 0.5);
            Assert.Equal(150, FirstWord(Find(root, "f")!)!.Left, 0.5);
        }

        [Fact]
        public async Task TheOffset_DoesNotMoveTheTextBesideAFloatInTheSameLine()
        {
            var root = await LayOutAsync("<div><div id='f' style='float:left; position:relative; left:200px; width:60px; height:20px'>f</div><span id='t'>beside</span></div>");

            Assert.Equal(45, FirstWord(Find(root, "t")!)!.Left, 0.5);
        }

        [Fact]
        public async Task AVerticalOffset_DoesNotMoveTheTextBesideTheFloatEither()
        {
            // top: 40px draws the float 40px lower; the paragraph still flows beside where it is in flow, at the top.
            var root = await LayOutAsync("<div><div id='f' style='float:left; position:relative; top:40px; width:60px; height:20px'>f</div><p id='t' style='margin:0'>beside</p></div>");

            var t = FirstWord(Find(root, "t")!)!;
            Assert.Equal(45, t.Left, 0.5);
            Assert.True(t.Top < 15, $"the text is {t.Top / 0.75:0.#}px down, below where the float is in flow");
        }

        [Fact]
        public async Task AFollowingFloat_GoesBesideTheStaticBox_NotTheMovedOne()
        {
            // The second float is placed against where the first is in flow (0..60px), so it starts at 60px; the first is drawn at 200px.
            var root = await LayOutAsync("<div><div id='f' style='float:left; position:relative; left:200px; width:60px; height:20px'>f</div><div id='g' style='float:left; width:40px; height:20px'>g</div></div>");

            Assert.Equal(45, FirstWord(Find(root, "g")!)!.Left, 0.5);
        }

        [Fact]
        public async Task ABlockWithARelativeOffset_StillMoves()
        {
            var root = await LayOutAsync("<div id='b' style='position:relative; left:50px; width:100px'>block rel</div>");

            Assert.Equal(37.5, FirstWord(Find(root, "b")!)!.Left, 0.5);
        }
    }
}
