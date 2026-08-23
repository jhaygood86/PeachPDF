using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <c>text-align</c> inside a vertical-writing-mode (<c>vertical-rl</c>/<c>vertical-lr</c>) box's own
    /// inline-only content (<c>CssLayoutEngine.ApplyVerticalTextAlignment</c>), issue #768. Unlike
    /// horizontal flow - where natural (pre-alignment) word placement is always flush-left regardless of
    /// <c>direction</c> - a vertical box's own natural placement already flushes to physical-bottom under
    /// <c>direction: rtl</c>, so <c>left</c>/<c>right</c>/<c>start</c>/<c>end</c> don't map onto
    /// "no-op vs. active shift" the same way horizontal's do; every combination below is covered
    /// explicitly rather than assumed to mirror the horizontal case.
    /// </summary>
    public class VerticalTextAlignIntegrationTests
    {
        private static List<CssRect> Words(CssBox el) =>
            el.LineBoxes.SelectMany(l => l.Words).Where(w => !w.IsLineBreak && !w.IsSpaces).ToList();

        [Theory]
        [InlineData("left", false)]
        [InlineData("right", true)]
        [InlineData("start", false)]
        [InlineData("end", true)]
        public async Task VerticalRl_Ltr_TextAlign_FlushesToExpectedEdge(string textAlign, bool expectBottom)
        {
            var html = LayoutHarness.Wrap($"""
                <div id="el" style="writing-mode: vertical-rl; width: 100pt; height: 200pt; text-align: {textAlign}">hi</div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el")!;
            var words = Words(el);
            Assert.NotEmpty(words);

            if (expectBottom)
                Assert.Equal(el.ClientBottom, words.Max(w => w.Bottom), 1);
            else
                Assert.Equal(el.ClientTop, words.Min(w => w.Top), 1);
        }

        [Theory]
        [InlineData("left", false)]
        [InlineData("right", true)]
        [InlineData("start", true)]
        [InlineData("end", false)]
        public async Task VerticalRl_Rtl_TextAlign_FlushesToExpectedEdge(string textAlign, bool expectBottom)
        {
            var html = LayoutHarness.Wrap($"""
                <div id="el" style="writing-mode: vertical-rl; direction: rtl; width: 100pt; height: 200pt; text-align: {textAlign}">hi</div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el")!;
            var words = Words(el);
            Assert.NotEmpty(words);

            if (expectBottom)
                Assert.Equal(el.ClientBottom, words.Max(w => w.Bottom), 1);
            else
                Assert.Equal(el.ClientTop, words.Min(w => w.Top), 1);
        }

        [Fact]
        public async Task VerticalLr_Ltr_TextAlignRight_FlushesToBottom()
        {
            var html = LayoutHarness.Wrap("""
                <div id="el" style="writing-mode: vertical-lr; width: 100pt; height: 200pt; text-align: right">hi</div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el")!;
            var words = Words(el);
            Assert.NotEmpty(words);
            Assert.Equal(el.ClientBottom, words.Max(w => w.Bottom), 1);
        }

        [Fact]
        public async Task VerticalLr_Rtl_TextAlignStart_FlushesToBottom()
        {
            // Confirms vertical-lr's InlineStartIsBottom asymmetry matches vertical-rl's own (same Y-axis
            // logic; only the block/X-axis anchor differs between the two writing modes).
            var html = LayoutHarness.Wrap("""
                <div id="el" style="writing-mode: vertical-lr; direction: rtl; width: 100pt; height: 200pt; text-align: start">hi</div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el")!;
            var words = Words(el);
            Assert.NotEmpty(words);
            Assert.Equal(el.ClientBottom, words.Max(w => w.Bottom), 1);
        }

        [Fact]
        public async Task VerticalRl_TextAlignCenter_CentersContentBetweenTopAndBottom()
        {
            var html = LayoutHarness.Wrap("""
                <div id="el" style="writing-mode: vertical-rl; width: 100pt; height: 200pt; text-align: center">hi</div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el")!;
            var words = Words(el);
            Assert.NotEmpty(words);

            var topGap = words.Min(w => w.Top) - el.ClientTop;
            var bottomGap = el.ClientBottom - words.Max(w => w.Bottom);

            Assert.Equal(topGap, bottomGap, 1);
            Assert.True(topGap > 1, "content should not already sit flush - otherwise this test can't distinguish center from a no-op");
        }

        [Fact]
        public async Task VerticalRl_TextAlignJustify_SpreadsNonLastColumnsAcrossTheFullExtent()
        {
            // A short height forces multiple columns; only the last column is exempt from justification
            // (CSS Text 3 §7.3) - here that's unambiguous since CreateVerticalLineBoxes is always a single
            // monolithic pass, so "last column" needs no blockFinished-style fragmentation caveat.
            var html = LayoutHarness.Wrap("""
                <div id="el" style="writing-mode: vertical-rl; width: 200pt; height: 40pt; text-align: justify">Alpha Beta Gamma Delta Epsilon Zeta.</div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el")!;
            Assert.True(el.LineBoxes.Count >= 2, "expected multiple columns for this much text at 40pt height");

            foreach (var column in el.LineBoxes.Take(el.LineBoxes.Count - 1))
            {
                var columnWords = column.Words.Where(w => !w.IsLineBreak && !w.IsSpaces).OrderBy(w => w.Top).ToList();
                if (columnWords.Count < 2) continue; // nothing to spread with only one word in the column

                Assert.Equal(el.ClientTop, columnWords[0].Top, 1);
                Assert.Equal(el.ClientBottom, columnWords[^1].Bottom, 1);
            }
        }

        [Fact]
        public async Task VerticalRl_Rtl_TextAlignJustify_SpreadsNonLastColumnsAcrossTheFullExtent()
        {
            // direction:rtl exercises ApplyVerticalJustifyAlignment's InlineStartIsBottom branch - the
            // cursor walks from ClientBottom upward instead of from ClientTop downward.
            var html = LayoutHarness.Wrap("""
                <div id="el" dir="rtl" style="writing-mode: vertical-rl; width: 200pt; height: 40pt; text-align: justify">Alpha Beta Gamma Delta Epsilon Zeta.</div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el")!;
            Assert.True(el.LineBoxes.Count >= 2, "expected multiple columns for this much text at 40pt height");

            foreach (var column in el.LineBoxes.Take(el.LineBoxes.Count - 1))
            {
                var columnWords = column.Words.Where(w => !w.IsLineBreak && !w.IsSpaces).OrderBy(w => w.Top).ToList();
                if (columnWords.Count < 2) continue;

                Assert.Equal(el.ClientTop, columnWords[0].Top, 1);
                Assert.Equal(el.ClientBottom, columnWords[^1].Bottom, 1);
            }
        }

        [Fact]
        public async Task VerticalRl_TextAlignJustify_SingleColumn_IsExemptAsTheOnlyLine()
        {
            var html = LayoutHarness.Wrap("""
                <div id="el" style="writing-mode: vertical-rl; width: 200pt; height: 300pt; text-align: justify">Alpha Beta Gamma.</div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el")!;
            Assert.Single(el.LineBoxes);

            var words = Words(el).OrderBy(w => w.Top).ToList();
            Assert.True(words[^1].Bottom < el.ClientBottom - 1,
                "the only column is also the last column, and must not be stretched to fill the box");
        }

        [Fact]
        public async Task VerticalRl_TextAlignJustify_AutoHeight_FillsTheTallestColumnDrivenFinalExtent()
        {
            // Explicit height on the wrapper (not "el") forces two unequal-content columns without
            // exercising the separate, pre-existing RTL auto-height-anchoring bug this fixture avoids by
            // staying LTR; "el" itself keeps width:auto/height:auto so its own final extent is
            // content-driven, and justify must spread against that final (not provisional) extent.
            var html = LayoutHarness.Wrap("""
                <div id="el" style="writing-mode: vertical-rl; width: 200pt; height: 40pt; text-align: justify">Alpha Beta Gamma Delta Epsilon.</div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el")!;
            Assert.True(el.LineBoxes.Count >= 2);

            var firstColumn = el.LineBoxes[0].Words.Where(w => !w.IsLineBreak && !w.IsSpaces).OrderBy(w => w.Top).ToList();
            if (firstColumn.Count >= 2)
            {
                Assert.Equal(el.ClientTop, firstColumn[0].Top, 1);
                Assert.Equal(el.ClientBottom, firstColumn[^1].Bottom, 1);
            }
        }

        [Fact]
        public async Task VerticalRl_Rtl_AutoHeight_PlainText_WordsStayWithinTheBoxsOwnFinalBottomEdge()
        {
            // Issue #797: an auto-height RTL vertical box's own natural word placement is anchored during
            // layout against a large placeholder bottom edge (CreateVerticalLineBoxes's own heightIsAuto
            // wrap-limit fallback). text-align's default "start" resolves to physical-right (flush-bottom)
            // under vertical+rtl (ApplyVerticalTextAlignment), which must re-anchor every word to the box's
            // own real, much closer final ClientBottom once it settles - a guard bug in
            // ApplyVerticalFlushAlignment instead silently skipped that correction, landing every word far
            // outside the box's own bottom edge.
            var html = LayoutHarness.Wrap("""
                <div id="el" style="writing-mode: vertical-rl; direction: rtl; width: 200pt">Some plain text with no other styling.</div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el")!;
            var words = Words(el);
            Assert.NotEmpty(words);

            foreach (var word in words)
            {
                Assert.True(word.Top >= el.ClientTop - 1,
                    $"word '{word.Text}' Top {word.Top} is above ClientTop {el.ClientTop}");
                Assert.True(word.Bottom <= el.ClientBottom + 1,
                    $"word '{word.Text}' Bottom {word.Bottom} is below ClientBottom {el.ClientBottom}");
            }

            // Default text-align:start resolves to physical-right (flush-bottom) under vertical+rtl.
            Assert.Equal(el.ClientBottom, words.Max(w => w.Bottom), 1);
        }

        [Fact]
        public async Task VerticalLr_Rtl_AutoHeight_PlainText_WordsStayWithinTheBoxsOwnFinalBottomEdge()
        {
            // vertical-lr's InlineStartIsBottom asymmetry matches vertical-rl's own (same Y-axis logic;
            // only the block/X-axis anchor differs between the two writing modes).
            var html = LayoutHarness.Wrap("""
                <div id="el" style="writing-mode: vertical-lr; direction: rtl; width: 200pt">Some plain text with no other styling.</div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el")!;
            var words = Words(el);
            Assert.NotEmpty(words);

            foreach (var word in words)
            {
                Assert.True(word.Top >= el.ClientTop - 1);
                Assert.True(word.Bottom <= el.ClientBottom + 1);
            }

            Assert.Equal(el.ClientBottom, words.Max(w => w.Bottom), 1);
        }

        [Fact]
        public async Task VerticalRl_Rtl_AutoHeight_TextAlignRight_WordsFlushToTheBoxsRealBottomEdge()
        {
            // Confirms the fix isn't accidentally keyed off the start-vs-right resolution rather than the
            // underlying ApplyVerticalFlushAlignment guard itself - explicit "right" resolves to the exact
            // same toBottom:true branch as the default "start" does under direction:rtl.
            var html = LayoutHarness.Wrap("""
                <div id="el" style="writing-mode: vertical-rl; direction: rtl; width: 200pt; text-align: right">Some plain text with no other styling.</div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el")!;
            var words = Words(el);
            Assert.NotEmpty(words);
            Assert.Equal(el.ClientBottom, words.Max(w => w.Bottom), 1);
            Assert.True(words.Min(w => w.Top) >= el.ClientTop - 1);
        }

        [Fact]
        public async Task VerticalRl_Rtl_AutoHeight_TextAlignLeft_WordsFlushToTheBoxsRealTopEdge()
        {
            // A regression guard for the toBottom:false branch, which happened to apply its correction
            // even before this fix (its guard's sign coincidentally matched what an auto-height RTL box
            // needs) - no existing test combined this with auto height, only definite height.
            var html = LayoutHarness.Wrap("""
                <div id="el" style="writing-mode: vertical-rl; direction: rtl; width: 200pt; text-align: left">Some plain text with no other styling.</div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var el = LayoutHarness.FindById(root, "el")!;
            var words = Words(el);
            Assert.NotEmpty(words);
            Assert.Equal(el.ClientTop, words.Min(w => w.Top), 1);
            Assert.True(words.Max(w => w.Bottom) <= el.ClientBottom + 1);
        }
    }
}
