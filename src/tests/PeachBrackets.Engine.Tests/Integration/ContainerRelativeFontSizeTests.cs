using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// A <c>font-size</c> in container-relative units (CSS Containment 3 §6.2) resolves against the nearest size container's
    /// content box. A box holding text reads its font while the document is still being cascaded, before any container has a
    /// layout of its own, so it used to get 0 (invisible text). The layout convergence loop re-cascades with the previous
    /// pass's container sizes, which the font now falls back on.
    /// </summary>
    public class ContainerRelativeFontSizeTests
    {
        private static async Task<CssBox> LayOutAsync(string body)
        {
            var html = $"<!DOCTYPE html><html><head><style>body {{ margin: 0 }} .c {{ container-type: inline-size; width: 400pt }} p, div {{ margin: 0 }}</style></head><body><div id='ref' style='font-size: 20pt'>x</div>{body}</body></html>";
            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext);
            await container.SetHtml(html, null);

            var size = new Size(595, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);

            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);

            _scale = DomUtils.GetBoxById(container.Root!, "ref")!.ActualFont.Size / 20;
            return container.Root!;
        }

        private static double _scale;

        private static CssBox Box(CssBox root, string id) => DomUtils.GetBoxById(root, id)!;

        [Theory]
        [InlineData("10cqw", 40)]
        [InlineData("5cqi", 20)]
        [InlineData("calc(1em + 5cqw)", 30)]
        [InlineData("calc(50% + 1cqw)", 9)]
        public async Task FontSize_InContainerUnits_OfABoxWithText_ResolvesAgainstTheContainer(string fontSize, double expectedPt)
        {
            var root = await LayOutAsync($"<div class='c' style='font-size: 10pt'><p id='t' style='font-size: {fontSize}'>Ab</p></div>");

            Assert.Equal(expectedPt * _scale, Box(root, "t").ActualFont.Size, 4);
        }

        [Fact]
        public async Task FontSize_InContainerUnits_IsInheritedAsTheComputedSize()
        {
            var root = await LayOutAsync("<div class='c'><div id='m' style='font-size: 10cqw'><p id='leaf'>Ab</p></div></div>");

            Assert.Equal(40 * _scale, Box(root, "m").ActualFont.Size, 4);
            Assert.Equal(40 * _scale, Box(root, "leaf").ActualFont.Size, 4);
        }

        [Fact]
        public async Task FontSize_InContainerUnits_FollowsTheContainersWidth()
        {
            var root = await LayOutAsync("<div class='c' style='width: 200pt'><p id='t' style='font-size: 10cqw'>Ab</p></div>");

            Assert.Equal(20 * _scale, Box(root, "t").ActualFont.Size, 4);
        }
    }
}
