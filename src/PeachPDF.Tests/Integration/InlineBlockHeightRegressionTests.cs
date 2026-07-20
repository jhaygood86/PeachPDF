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

        // CSS2.1 §10.8.1: the vertical padding/border of a non-replaced `display: inline` box
        // must NOT influence line box height - it paints (overflowing the line) without taking
        // vertical space. The FlowBox height correction is therefore restricted to atomic
        // inline-level boxes; a plain span's 200px of vertical padding must not grow its block.
        [Fact]
        public async Task PaddedPlainInline_DoesNotGrowBlockHeight()
        {
            const string html = @"<!DOCTYPE html>
<html>
<body style='margin: 0'>
<div style='height: 300px'>filler</div>
<div class='wrapper'><span style='padding: 100px 0'>text</span></div>
</body>
</html>";

            var (rootBox, _) = await BuildCssBoxTree(html);

            var wrapper = FindBoxByClass(rootBox, "wrapper");
            Assert.NotNull(wrapper);

            var height = wrapper!.ActualBottom - wrapper.Location.Y;
            Assert.True(height > 0,
                $"Block height must stay positive (top={wrapper.Location.Y}, bottom={wrapper.ActualBottom})");
            Assert.True(height < 200,
                $"Plain inline vertical padding must not grow the block (CSS2.1 §10.8.1) - got height {height}");
        }

        // CSS2.1 §8.1: an atomic inline-level box's content is laid out inside its padding box,
        // so the label of a padded button must start border+padding-top BELOW the box's top
        // edge (and end padding-bottom above its bottom edge) - not hug the top border with the
        // background expanding upward around it.
        [Fact]
        public async Task PaddedInlineBlock_WordsSitInsidePaddingBox()
        {
            const string html = @"<!DOCTYPE html>
<html>
<body style='margin: 0'>
<button class='btn' style='padding: 6px 14px'>Go</button>
</body>
</html>";

            var (rootBox, _) = await BuildCssBoxTree(html);

            var button = FindBoxByClass(rootBox, "btn");
            Assert.NotNull(button);

            var rect = Assert.Single(button!.Rectangles).Value;
            var word = FindFirstWord(button);
            Assert.NotNull(word);

            Assert.True(word!.Top >= rect.Top + 6 - 0.1,
                $"Button label (top={word.Top}) must sit at least padding-top (6) below the box top ({rect.Top})");
            Assert.True(word.Bottom <= rect.Bottom - 6 + 0.1,
                $"Button label (bottom={word.Bottom}) must end at least padding-bottom (6) above the box bottom ({rect.Bottom})");
        }

        // CssLineBox.UpdateRectangle historically expanded the rect's bottom edge by
        // padding-TOP instead of padding-bottom - invisible with symmetric padding, wrong for
        // asymmetric. With only padding-bottom set, the rect must extend below the words by
        // that amount while the top edge stays at the word band.
        [Fact]
        public async Task AsymmetricPaddingBottom_ExpandsRectBottomByPaddingBottom()
        {
            const string html = @"<!DOCTYPE html>
<html>
<body style='margin: 0'>
<button class='btn' style='padding: 0 0 30px 0'>Go</button>
</body>
</html>";

            var (rootBox, _) = await BuildCssBoxTree(html);

            var button = FindBoxByClass(rootBox, "btn");
            Assert.NotNull(button);

            var rect = Assert.Single(button!.Rectangles).Value;
            var word = FindFirstWord(button);
            Assert.NotNull(word);

            Assert.True(rect.Bottom >= word!.Bottom + 30 - 0.1,
                $"Box rect bottom ({rect.Bottom}) must extend padding-bottom (30) below the words (bottom={word.Bottom})");
            Assert.True(Math.Abs(rect.Top - word.Top) < 0.1,
                $"With no padding-top the rect top ({rect.Top}) must coincide with the word band top ({word.Top})");
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

        private static CssRect? FindFirstWord(CssBox box)
        {
            if (box.Words.Count > 0)
                return box.Words[0];

            foreach (var child in box.Boxes)
            {
                var word = FindFirstWord(child);
                if (word != null)
                    return word;
            }

            return null;
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
