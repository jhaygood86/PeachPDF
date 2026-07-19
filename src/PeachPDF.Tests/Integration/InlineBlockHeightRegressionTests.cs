using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.Integration
{
    // Regression for FlowBox's end-of-box "handle height setting" correction
    // (CssLayoutEngine.FlowBox): when an inline(-block) box's flowed content comes out
    // shorter than the box's own ActualHeight (e.g. a padded button whose one small text
    // line is shorter than its vertical padding), MaxBottom must be extended to
    // startY + ActualHeight. The historical form assigned the deficit as an ABSOLUTE
    // document Y (a tiny value near the page top), dragging MaxBottom above the block's
    // own top - the block's ActualBottom then landed above its Location (negative height)
    // and paint-time visibility culling silently dropped the block's entire subtree.
    // This is exactly why the showcase themeable card's "Learn More" <button> never
    // painted. Padding here is deliberately large (100px top+bottom) so the box's
    // ActualHeight exceeds one text line's height under any font metrics, real or
    // fallback; the 300px filler above makes the old absolute-Y assignment land far above
    // the block's top, so the corrupted (negative) height is unambiguous to assert on.
    public class InlineBlockHeightRegressionTests
    {
        [Fact]
        public async Task PaddedInlineBlock_SoleContentOfBlock_BlockHeightCoversPadding()
        {
            const string html = @"<!DOCTYPE html>
<html>
<body style='margin: 0'>
<div style='height: 300px'>filler</div>
<div class='wrapper'><button class='btn' style='padding: 100px 14px'>Go</button></div>
</body>
</html>";

            var (rootBox, _) = await BuildCssBoxTree(html);

            var wrapper = FindBoxByClass(rootBox, "wrapper");
            Assert.NotNull(wrapper);

            var height = wrapper!.ActualBottom - wrapper.Location.Y;
            Assert.True(height > 0,
                $"Block wrapping only a padded inline-block must not collapse to a negative height (top={wrapper.Location.Y}, bottom={wrapper.ActualBottom})");
            Assert.True(height >= 200,
                $"Block height ({height}) must cover the inline-block's own 200px of vertical padding");
        }

        [Fact]
        public async Task PaddedInlineBlock_TallerContentLine_DoesNotShrinkBlock()
        {
            // The correction must only ever GROW MaxBottom: when the flowed line is already
            // taller than the box's ActualHeight, the block keeps its content-driven height.
            const string html = @"<!DOCTYPE html>
<html>
<body style='margin: 0'>
<div style='height: 300px'>filler</div>
<div class='wrapper'><button style='padding: 1px 14px'>Go</button></div>
</body>
</html>";

            var (rootBox, _) = await BuildCssBoxTree(html);

            var wrapper = FindBoxByClass(rootBox, "wrapper");
            Assert.NotNull(wrapper);
            Assert.True(wrapper!.ActualBottom > wrapper.Location.Y,
                $"Block height must stay positive (top={wrapper.Location.Y}, bottom={wrapper.ActualBottom})");
        }

        #region Helpers

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildCssBoxTree(string html)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);

            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }

        private static CssBox? FindBoxByClass(CssBox root, string className)
        {
            var classAttr = root.HtmlTag?.TryGetAttribute("class", "");
            if (!string.IsNullOrEmpty(classAttr))
            {
                foreach (var cls in classAttr.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (cls == className)
                        return root;
                }
            }

            foreach (var child in root.Boxes)
            {
                var result = FindBoxByClass(child, className);
                if (result != null)
                    return result;
            }

            return null;
        }

        #endregion
    }
}
