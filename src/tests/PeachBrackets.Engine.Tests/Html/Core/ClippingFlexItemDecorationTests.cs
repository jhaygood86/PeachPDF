using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragments;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;

namespace PeachBrackets.Engine.Tests.Html.Core
{
    /// <summary>
    /// A flex item that clips its overflow is painted at the size it was given. mad4mod.space's gallery cards are
    /// <c>overflow: hidden; flex: 1 1 auto; min-height: 0</c> figures holding a tall photo: the fragment's decoration was grown to where the photo's content ended, so
    /// the figure's background and rounded clip covered the caption below it, and the photo overlapped the next row of the grid.
    /// </summary>
    public class ClippingFlexItemDecorationTests
    {
        private static BoxFragment? FindFragment(BoxFragment fragment, string id)
        {
            if (fragment.Box.HtmlTag?.TryGetAttribute("id") == id) return fragment;
            foreach (var child in fragment.Children)
            {
                if (FindFragment(child, id) is { } found) return found;
            }

            return null;
        }

        private static async Task<HtmlContainerInt> LayOutAsync(string body)
        {
            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext) { MediaType = MediaType.Screen };
            await container.SetHtml($"<!DOCTYPE html><html><body style='margin:0'>{body}</body></html>", null);
            var size = new Size(900, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);
            return container;
        }

        [Theory]
        [InlineData("overflow:hidden")]
        [InlineData("overflow:hidden;flex:1 1 auto;min-height:0")]
        [InlineData("height:100px;overflow-y:clip")]
        public async Task AClippingFlexItem_IsPaintedAtItsOwnHeight_NotItsContents(string itemStyle)
        {
            var container = await LayOutAsync(
                $"<div style='display:flex;flex-direction:column;width:100px;height:100px'><div id='i' style='{itemStyle};background:#fc0'><div style='height:400px'></div></div></div>");

            var fragment = FindFragment(container.FragmentTree!.Fragmentainers[0].Root, "i")!;

            Assert.Equal(100 * 0.75, fragment.Box.ActualBoxSizingHeight, 0.5);
            Assert.Equal(100 * 0.75, fragment.Rect.Height, 0.5);
        }
    }
}
