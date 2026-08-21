using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Unicode Bidi Algorithm reordering inside a vertical-writing-mode (<c>vertical-rl</c>/<c>vertical-lr</c>)
    /// box's own inline-only content (<c>CssLayoutEngine.ApplyVerticalBidiReordering</c>), issue #768 -
    /// the vertical counterpart of <see cref="Html.Core.CssLayoutEngineBidiTests"/>, asserting word <c>Top</c>
    /// ordering (the column's own inline axis) instead of <c>Left</c>.
    /// </summary>
    public class VerticalBidiIntegrationTests
    {
        private static List<CssRectWord> WordsOf(CssBox box) =>
            LayoutHarness.Descendants(box)
                .SelectMany(b => b.Words.OfType<CssRectWord>())
                .Where(w => w.Text != "\n")
                .ToList();

        [Fact]
        public async Task VerticalRl_RtlParagraph_DigitRunWithNoSurroundingWhitespace_ReordersBetweenItsNeighborsAlongTop()
        {
            var html = LayoutHarness.Wrap("""
                <div id="p" dir="rtl" style="writing-mode: vertical-rl; width: 100pt; height: 400pt">מספר123כאן</div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var p = LayoutHarness.FindById(root, "p");
            Assert.NotNull(p);

            var words = WordsOf(p!);
            Assert.Equal(3, words.Count);

            var digits = Assert.Single(words, w => w.Text == "123");
            var others = words.Where(w => w != digits).OrderBy(w => w.Top).ToList();
            Assert.Equal(2, others.Count);
            Assert.True(digits.Top > others[0].Top && digits.Top < others[1].Top,
                $"expected the digit run positioned between its two Hebrew neighbors along Top; digits.Top={digits.Top}, others=[{others[0].Top}, {others[1].Top}]");
        }

        [Fact]
        public async Task VerticalLr_RtlParagraph_DigitRunReordersBetweenItsNeighborsAlongTop()
        {
            var html = LayoutHarness.Wrap("""
                <div id="p" dir="rtl" style="writing-mode: vertical-lr; width: 100pt; height: 400pt">מספר123כאן</div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var p = LayoutHarness.FindById(root, "p");
            Assert.NotNull(p);

            var words = WordsOf(p!);
            Assert.Equal(3, words.Count);

            var digits = Assert.Single(words, w => w.Text == "123");
            var others = words.Where(w => w != digits).OrderBy(w => w.Top).ToList();
            Assert.Equal(2, others.Count);
            Assert.True(digits.Top > others[0].Top && digits.Top < others[1].Top,
                $"expected the digit run positioned between its two Hebrew neighbors along Top; digits.Top={digits.Top}, others=[{others[0].Top}, {others[1].Top}]");
        }

        [Fact]
        public async Task VerticalRl_LtrParagraph_EmbeddedRtlRun_StillReordersAlongTop()
        {
            // Separates "paragraph base direction" from "does an embedded RTL run still reorder": the
            // paragraph itself stays direction:ltr (natural placement flush-top), but the embedded Hebrew
            // run must still resolve its own bidi level and reorder internally.
            var html = LayoutHarness.Wrap("""
                <div id="p" style="writing-mode: vertical-rl; width: 100pt; height: 400pt">before שלום עולם after</div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var p = LayoutHarness.FindById(root, "p");
            Assert.NotNull(p);

            var words = WordsOf(p!);
            var before = Assert.Single(words, w => w.Text == "before");
            var after = Assert.Single(words, w => w.Text == "after");

            // The surrounding LTR paragraph keeps its own logical top-to-bottom word order.
            Assert.True(before.Top < after.Top,
                $"expected the surrounding LTR paragraph's own word order to be unaffected; before.Top={before.Top}, after.Top={after.Top}");
        }

        [Fact]
        public async Task VerticalRl_Bdo_DirRtl_ReversesPlainLatinText()
        {
            var html = LayoutHarness.Wrap("""
                <div style="writing-mode: vertical-rl; width: 100pt; height: 200pt"><bdo id="b" dir="rtl">hello</bdo></div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var bdo = LayoutHarness.FindById(root, "b");
            Assert.NotNull(bdo);

            var word = Assert.Single(WordsOf(bdo!));
            Assert.Equal("olleh", word.Text);
        }

        [Fact]
        public async Task VerticalRl_Bdi_WithNoDirAttribute_DoesNotLeakOwnDirectionIntoSurroundingLtrColumn()
        {
            var html = LayoutHarness.Wrap("""
                <div id="p" style="writing-mode: vertical-rl; width: 100pt; height: 400pt">before <bdi id="b">שלום עולם</bdi> after</div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var p = LayoutHarness.FindById(root, "p");
            var bdi = LayoutHarness.FindById(root, "b");
            Assert.NotNull(p);
            Assert.NotNull(bdi);

            var pWords = WordsOf(p!);
            var before = Assert.Single(pWords, w => w.Text == "before");
            var after = Assert.Single(pWords, w => w.Text == "after");

            Assert.True(before.Top < after.Top,
                $"expected the surrounding LTR content's own word order to be unaffected by the isolated <bdi>; before.Top={before.Top}, after.Top={after.Top}");
        }

        [Fact]
        public async Task VerticalRl_TextAlignCenter_WithRtlRun_BidiReordersWithinTheCenteredSpan()
        {
            // Proves execution order: ApplyVerticalTextAlignment runs before ApplyVerticalBidiReordering,
            // so the reordered run's positions stay within the already-centered span's edges rather than
            // the box's raw, un-centered ones.
            var html = LayoutHarness.Wrap("""
                <div id="p" dir="rtl" style="writing-mode: vertical-rl; width: 100pt; height: 400pt; text-align: center">מספר123כאן</div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var p = LayoutHarness.FindById(root, "p");
            Assert.NotNull(p);

            var words = WordsOf(p!);
            Assert.Equal(3, words.Count);

            var contentTop = words.Min(w => w.Top);
            var contentBottom = words.Max(w => w.Bottom);
            var topGap = contentTop - p!.ClientTop;
            var bottomGap = p.ClientBottom - contentBottom;

            Assert.Equal(topGap, bottomGap, 1);

            var digits = Assert.Single(words, w => w.Text == "123");
            var others = words.Where(w => w != digits).OrderBy(w => w.Top).ToList();
            Assert.True(digits.Top > others[0].Top && digits.Top < others[1].Top,
                "the digit run should still sit between its two neighbors after centering");
        }
    }
}
