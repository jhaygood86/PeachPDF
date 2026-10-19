using PeachPDF.Rendering;
using PeachDrawing.Core;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachDrawing.PDF;
using PeachDrawing.PDF.Engine.Drawing;
using System;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <c>revert</c> and <c>revert-layer</c> in an <c>!important</c> custom property or <c>color</c> are left to the cascade's own important phase, not
    /// applied ahead of the animation step: the early application has no revert snapshot to roll back to. PeachPDF's <c>revert</c> rolls back to the
    /// state before that phase (a documented deviation from a browser's rollback to the user-agent origin), so what these tests pin is not Chrome's
    /// value but that the animation sees the SAME value the box has without it - the value an animation's implicit keyframe stands for. Applying
    /// the keyword early (as an unset/initial value) would make the two differ.
    /// </summary>
    public class AnimationImportantRevertTests
    {
        [Theory]
        [InlineData("revert")]
        [InlineData("revert-layer")]
        public async Task ImportantCustomPropertyReverted_TheAnimatedBoxEndsWhereTheStaticOneIs(string keyword)
        {
            var html = $@"<!DOCTYPE html><html><head><style>
                @keyframes k {{ from {{ width: 50px }} }}
                #a {{ --w: 100px; width: var(--w); height: 10px; animation: k 1s linear }}
                #a {{ --w: {keyword} !important }}</style></head><body style=""margin: 0""><div id=""a""></div></body></html>";

            var staticWidth = await WidthAsync(html, null);

            // Progress 1 is the implicit end keyframe: the box's own value, which is what the static box has.
            Assert.Equal(staticWidth, await WidthAsync(html, 1), 1);

            // Half way is the mix of the two ends: neither the rolled-back value alone nor the keyword applied as an unset one.
            Assert.Equal((50 + staticWidth) / 2, await WidthAsync(html, 0.5), 1);
        }

        [Theory]
        [InlineData("revert")]
        [InlineData("revert-layer")]
        public async Task ImportantColorReverted_TheMixUsesTheColorTheStaticBoxHas(string keyword)
        {
            var staticHtml = Html(keyword);
            var staticColor = (await BoxAsync(staticHtml, null)).ActualColor;

            // background-color goes from white to currentcolor: half way is white mixed with whatever color the box really has.
            var background = (await BoxAsync(staticHtml, 0.5)).ActualBackgroundColor;

            Assert.InRange(background.R, (255 + staticColor.R) / 2 - 2, (255 + staticColor.R) / 2 + 2);
            Assert.InRange(background.G, (255 + staticColor.G) / 2 - 2, (255 + staticColor.G) / 2 + 2);
            Assert.InRange(background.B, (255 + staticColor.B) / 2 - 2, (255 + staticColor.B) / 2 + 2);

            static string Html(string keyword) => $@"<!DOCTYPE html><html><head><style>
                @keyframes k {{ from {{ background-color: #fff }} to {{ background-color: currentcolor }} }}
                #p {{ color: #ff8800 }}
                #a {{ color: #0000ff; width: 20px; height: 20px; animation: k 1s linear }}
                #a {{ color: {keyword} !important }}</style></head><body style=""margin: 0""><div id=""p""><div id=""a""></div></div></body></html>";
        }

        private static async Task<double> WidthAsync(string html, double? progress)
        {
            var box = await BoxAsync(html, progress);
            return (box.ActualRight - box.Location.X) / 0.75;
        }

        private static async Task<CssBox> BoxAsync(string html, double? progress)
        {
            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext) { AnimationProgress = progress };
            await container.SetHtml(html, null);

            var size = new Size(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.ToPixels(size, 1.0);

            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return Find(container.Root!, "a") ?? throw new InvalidOperationException("No #a");
        }

        private static CssBox? Find(CssBox box, string id)
        {
            if (box.HtmlTag?.Attributes?.TryGetValue("id", out var boxId) == true && string.Equals(boxId, id, StringComparison.OrdinalIgnoreCase))
                return box;

            foreach (var child in box.Boxes)
            {
                if (Find(child, id) is { } found) return found;
            }

            return null;
        }
    }
}
