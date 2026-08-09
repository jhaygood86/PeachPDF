using PeachPDF.Adapters;
using PeachPDF.CSS;
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

        // A tighter companion to the test above: asserts the block's height against the button's own
        // measured insets and word height, not just a loose lower bound - a lower bound alone cannot
        // catch OVER-counting. This is the shape that let the pre-shift design (issue #333) land with
        // FinalizeFlowBoxExit's "handle height setting" fallback silently double-counting the button's
        // own top inset (border-top+padding-top) for a box reached through FlowBox's recursive branch:
        // `startY`, captured at that nested call's own entry, already named the content-box top (this
        // issue's pre-shift), while ActualHeight is a border-box measurement, so summing them counted
        // the top inset twice. `height >= 200` alone does not notice a result that is too large.
        [Fact]
        public async Task PaddedInlineBlock_SoleContentOfBlock_BlockHeightMatchesInsetsExactly()
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
            var button = FindBoxByClass(rootBox, "btn");
            Assert.NotNull(wrapper);
            Assert.NotNull(button);

            var word = FindFirstWord(button!);
            Assert.NotNull(word);

            var expectedHeight = button!.ActualBorderTopWidth + button.ActualPaddingTop
                + word!.Height
                + button.ActualBorderBottomWidth + button.ActualPaddingBottom;
            var actualHeight = wrapper!.ActualBottom - wrapper.Location.Y;

            Assert.True(Math.Abs(actualHeight - expectedHeight) < 0.5,
                $"Block height ({actualHeight}) must equal the button's own top inset + word height + bottom inset ({expectedHeight}) exactly, not merely cover it - a value this far off (e.g. the top inset counted twice) would still pass a '>=' check");
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
<button class='btn' style='padding: 6pt 14pt'>Go</button>
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
<button class='btn' style='padding: 0 0 30pt 0'>Go</button>
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

        // A ::before/::after pseudo-element carries its generated text directly on its own box
        // (no anonymous child), which flows through FlowBox's direct-words branch - the
        // CSS2.1 §8.1 content inset must apply there exactly as it does for a real element
        // like <button> (docs bless display: inline-block on pseudo-elements).
        [Fact]
        public async Task PaddedInlineBlockPseudoElement_WordsSitInsidePaddingBox()
        {
            const string html = @"<!DOCTYPE html>
<html>
<head><style>
.host::before { content: 'NEW'; display: inline-block; padding: 6pt 14pt; }
</style></head>
<body style='margin: 0'>
<div class='host'>after marker</div>
</body>
</html>";

            var (rootBox, _) = await BuildCssBoxTree(html);

            var host = FindBoxByClass(rootBox, "host");
            Assert.NotNull(host);
            var pseudo = FindFirst(host!, b => b.Display.Value == DisplayMode.InlineBlock);
            Assert.NotNull(pseudo);

            var rect = Assert.Single(pseudo!.Rectangles).Value;
            var word = FindFirstWord(pseudo);
            Assert.NotNull(word);

            Assert.True(word!.Top >= rect.Top + 6 - 0.1,
                $"Pseudo-element label (top={word.Top}) must sit at least padding-top (6) below the box top ({rect.Top})");
        }

        // The §8.1 inset shift runs after word flow already page-broke each word - a padded
        // line that legitimately fit above a page boundary must be re-checked after the shift,
        // or its glyphs paint sliced across two pages (css-break §4: a monolithic line lands
        // fully within one fragmentainer).
        [Fact]
        public async Task InsetShiftNearPageBoundary_WordsDoNotStraddleThePage()
        {
            // The filler parks the line so the un-inset words fit above the 842px boundary but
            // the 100px padding-top shift pushes them across it.
            const string html = @"<!DOCTYPE html>
<html>
<body style='margin: 0'>
<div style='height: 760px'>filler</div>
<div><button style='padding: 100px 14px'>Go</button></div>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html);
            var pageHeight = container.PageSize.Height;

            var button = FindFirst(rootBox, b => b.HtmlTag?.Name == "button");
            Assert.NotNull(button);
            var word = FindFirstWord(button!);
            Assert.NotNull(word);

            var topPage = Math.Floor(word!.Top / pageHeight);
            var bottomPage = Math.Floor((word.Bottom - 0.01) / pageHeight);
            Assert.True(topPage == bottomPage,
                $"Inset-shifted word must not straddle a page boundary (top={word.Top:F1}, bottom={word.Bottom:F1}, pageHeight={pageHeight})");
        }

        // The multi-column analog of InsetShiftNearPageBoundary_WordsDoNotStraddleThePage: under the
        // break-token model (issue #333) a fragmentainer straddle discovered by the inset shift must
        // move the WHOLE line to column 2 - landing at that column's own content top - rather than
        // merely avoid straddling within column 1's own band (which the pre-#333 per-word relocation
        // could do just as well, and would make an assertion of "not straddling" alone pass for the
        // wrong reason). Column 1 comfortably fits the un-inset word; only the padding-top shift pushes
        // it past the column's bottom.
        [Fact]
        public async Task InsetShiftNearColumnBoundary_WholeLineMovesToNextColumn()
        {
            var html = @"<!DOCTYPE html>
<html>
<body style='margin: 0'>
<div id='mc' style='columns: 2; column-gap: 0; column-fill: auto; width: 200pt'>
<div style='height: 220pt'>filler</div>
<button id='btn' style='padding-top: 60pt'>Go</button>
</div>
</body>
</html>";

            var (root, _) = await BuildCssBoxTree(html, width: 200, height: 300);

            var mc = FindFirst(root, b => b.HtmlTag?.TryGetAttribute("id", "") == "mc");
            var button = FindFirst(root, b => b.HtmlTag?.TryGetAttribute("id", "") == "btn");
            Assert.NotNull(mc);
            Assert.NotNull(button);

            var word = FindFirstWord(button!);
            Assert.NotNull(word);

            var columnWidth = (mc!.ClientRight - mc.ClientLeft) / 2;
            Assert.True(word!.Left >= mc.ClientLeft + columnWidth - 0.1,
                $"expected the inset-shifted line to move whole to column 2 (columnWidth={columnWidth}), word is at x={word.Left}");
            // The button's only line never got kept anywhere, so this pass genuinely opens the box's
            // content from its own top - the 60pt padding-top applies here exactly as it would have in
            // column 1, landing the word at column 2's content top plus its own inset, not at Y~0.
            Assert.True(Math.Abs(word.Top - 60) < 5,
                $"expected padding-top (60) to apply once, at column 2's own content top, word top={word.Top}");
        }

        // An inline-block's content can wrap onto more than one internal line (CssLayoutEngine.FlowBox
        // recurses into the SAME blockBox.LineBoxes list for it) - the pre-shift design this issue
        // introduced has to get every such line right individually, not just the first, since
        // CreateLineBoxes can only discard one in-progress line at a time. Here the first internal line
        // (after the padding-top shift) still fits column 1; only the second, forced-break line straddles
        // and moves alone. Distinct from the previous test in a way worth pinning: this resumed pass is a
        // CONTINUATION of the box's content (its first line already landed in column 1), so padding-top
        // must NOT apply a second time - only a pass that genuinely opens the box's own content does that.
        // orphans/widows are relaxed to 1 to isolate that mechanism from this one: the UA default of 2
        // would otherwise refuse to leave a single orphan line behind and move the whole two-line box,
        // which is a different (and already-tested) mechanism, not the one under test here.
        [Fact]
        public async Task InsetShiftedInlineBlock_SecondInternalLine_MovesAloneToNextColumn()
        {
            var html = @"<!DOCTYPE html>
<html>
<body style='margin: 0'>
<div id='mc' style='columns: 2; column-gap: 0; column-fill: auto; width: 200pt; orphans: 1; widows: 1'>
<div style='height: 150pt'>filler</div>
<span id='btn' style='display:inline-block; padding-top: 60pt'>First<br>Second</span>
</div>
</body>
</html>";

            var (root, _) = await BuildCssBoxTree(html, width: 200, height: 300);

            var mc = FindFirst(root, b => b.HtmlTag?.TryGetAttribute("id", "") == "mc");
            var button = FindFirst(root, b => b.HtmlTag?.TryGetAttribute("id", "") == "btn");
            Assert.NotNull(mc);
            Assert.NotNull(button);

            var firstWord = AllWords(button!).Single(w => w.Text == "First");
            var secondWord = AllWords(button!).Single(w => w.Text == "Second");

            var columnWidth = (mc!.ClientRight - mc.ClientLeft) / 2;

            Assert.True(firstWord.Left < mc.ClientLeft + columnWidth - 0.1,
                $"expected the button's first internal line to stay in column 1, word is at x={firstWord.Left}");
            Assert.True(secondWord.Left >= mc.ClientLeft + columnWidth - 0.1,
                $"expected the button's second internal line to move alone to column 2, word is at x={secondWord.Left}");
            // A continuation, not an opening pass: the box's first line already landed in column 1, so
            // padding-top must not be re-applied here - the word starts at column 2's raw content top.
            Assert.True(secondWord.Top < 15,
                $"expected the resumed (continuation) line to start at column 2's raw content top with no re-applied inset, word top={secondWord.Top}");
        }

        #region Helpers

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildCssBoxTree(string html)
        {
            return await BuildCssBoxTree(html, width: 595, height: 842);
        }

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildCssBoxTree(string html, double width, double height)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            container.MarginTop = 0;
            container.MarginLeft = 0;
            container.MarginRight = 0;
            container.MarginBottom = 0;

            await container.SetHtml(html, null);

            var size = new XSize(width, height);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }

        private static CssBox? FindFirst(CssBox box, Func<CssBox, bool> predicate)
        {
            if (predicate(box)) return box;
            foreach (var child in box.Boxes)
            {
                var found = FindFirst(child, predicate);
                if (found != null) return found;
            }
            return null;
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

        private static List<CssRect> AllWords(CssBox box)
        {
            var words = new List<CssRect>(box.Words);

            foreach (var child in box.Boxes)
            {
                words.AddRange(AllWords(child));
            }

            return words;
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
