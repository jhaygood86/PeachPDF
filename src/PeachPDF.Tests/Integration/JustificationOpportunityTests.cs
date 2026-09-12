using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression coverage for issue #1013: <c>CssLayoutEngine.ApplyJustifyAlignment</c> spread its
    /// expansion over <i>every</i> word boundary on a justified line instead of over
    /// <see href="https://www.w3.org/TR/css-text-3/#justify-algos">css-text-3 §6.4.5</see>'s
    /// justification opportunities, so it opened a gap between two inline boxes the source has no white
    /// space between at all - and, because it divided the leftover by the word count rather than the
    /// opportunity count and then dumped the shortfall into the last gap, left that final gap roughly
    /// twice as wide as the others.
    /// </summary>
    /// <remarks>
    /// Every expected number here was measured in Chromium through the repo's own Playwright dependency
    /// (<c>page.Locator(...).BoundingBoxAsync()</c> over one span per word), at <c>font: 16px
    /// monospace</c> in a <c>200pt</c>-wide block: <c>A&lt;span&gt;B&lt;/span&gt;</c> contiguous at a 0
    /// gap, every inter-word gap on the line identical (10.094px), and a lone overflowing word on a
    /// justified non-last line left at the line's start edge.
    /// </remarks>
    public class JustificationOpportunityTests
    {
        private const string Measure = "margin:0;width:200pt;font:16px monospace;text-align:justify";

        private static IReadOnlyList<double> GapsOn(CssLineBox line) =>
            line.Words.Skip(1).Select((word, index) => word.Left - line.Words[index].Right).ToList();

        [Fact]
        public async Task AdjacentInlineBoxesWithNoSourceSpace_GetNoExpansion()
        {
            // The issue's own repro. `A<span>B</span>` is one run with no white space in it, so the
            // boundary is not a justification opportunity and the two words render contiguous - exactly
            // as they do under text-align: left, and as Chromium renders them.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='d' style='{Measure}'>A<span id='s'>B</span> CD EF GH IJ KL MN OP QR ST UV WX " +
                "YZ AB CD EF GH IJ</div>"));

            var d = LayoutHarness.FindById(root, "d")!;
            var line = d.LineBoxes[0];

            Assert.True(d.LineBoxes.Count >= 2, "fixture must wrap so that line 0 is genuinely justified");
            Assert.True(line.Words.Count > 2, "fixture must put more than the A/B pair on the justified line");

            var a = line.Words[0];
            var b = line.Words[1];

            Assert.Equal("A", a.Text);
            Assert.Equal("B", b.Text);
            Assert.Equal(a.Right, b.Left, 3);
        }

        [Fact]
        public async Task EveryWordSeparatorOnAJustifiedLine_GetsTheSameExpansion()
        {
            // The leftover is divided by the number of opportunities, not the number of words, so the
            // last gap is the same as the rest. Dividing by the word count left it ~2x the others.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='d' style='{Measure}'>A<span>B</span> CD EF GH IJ KL MN OP QR ST UV WX YZ AB " +
                "CD EF GH IJ</div>"));

            var d = LayoutHarness.FindById(root, "d")!;
            var line = d.LineBoxes[0];

            // Gap 0 is the non-opportunity A|B boundary; every remaining gap is a word separator.
            var separatorGaps = GapsOn(line).Skip(1).ToList();

            Assert.True(separatorGaps.Count >= 3, "fixture must hold several word separators to compare");
            Assert.All(separatorGaps, gap => Assert.Equal(separatorGaps[0], gap, 3));
            Assert.True(separatorGaps[0] > 6.6,
                $"each separator gap must be wider than the natural space it expands from (gap={separatorGaps[0]:F3})");
            Assert.Equal(d.ClientRight, line.Words[^1].Right, 3);
        }

        [Fact]
        public async Task WhitespaceOnlyInlineBoxBetweenTwoWords_IsAJustificationOpportunity()
        {
            // `<span>AA</span> <span>BB</span>`: the space belongs to neither word - CssBox.ParseToWords
            // emits no word for a collapsible-whitespace-only text node, and FlowBox instead advances the
            // cursor for the box. Without CssRect.PrecededByWordSeparator recording that advance, this
            // very ordinary markup would read as contiguous and get no expansion at all.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='d' style='{Measure}'><span>AA</span> <span>BB</span> CD EF GH IJ KL MN OP QR " +
                "ST UV WX YZ AB</div>"));

            var d = LayoutHarness.FindById(root, "d")!;
            var line = d.LineBoxes[0];
            var gaps = GapsOn(line);

            Assert.True(d.LineBoxes.Count >= 2, "fixture must wrap so that line 0 is genuinely justified");
            Assert.True(line.Words[1].PrecededByWordSeparator,
                "the whitespace-only inline box between the two spans is a word separator");
            Assert.All(gaps, gap => Assert.Equal(gaps[0], gap, 3));
            Assert.Equal(d.ClientRight, line.Words[^1].Right, 3);
        }

        [Fact]
        public async Task HyphenSplitInsideAWord_IsNotAJustificationOpportunity()
        {
            // ParseToWords splits `well-known` into `well-` and `known` because a hyphen is a soft wrap
            // opportunity - but §6.4.5 lists word separators and block/clustered-script letters, not every
            // wrap opportunity, so no expansion belongs there. Chromium keeps the two halves contiguous.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='d' style='{Measure}'>well-known AA BB CC DD EE FF GG HH II JJ KK LL MM NN OO</div>"));

            var d = LayoutHarness.FindById(root, "d")!;
            var line = d.LineBoxes[0];

            Assert.True(d.LineBoxes.Count >= 2, "fixture must wrap so that line 0 is genuinely justified");
            Assert.Equal("well-", line.Words[0].Text);
            Assert.Equal("known", line.Words[1].Text);
            Assert.Equal(line.Words[0].Right, line.Words[1].Left, 3);
        }

        [Fact]
        public async Task BoundaryBetweenTwoCjkCharacters_IsAJustificationOpportunity()
        {
            // §6.4.5's second requirement: the boundary between a block-script character and any other
            // one. CJK text carries no word separators at all, so this is the only thing that lets a CJK
            // line justify - and Chromium does justify it (each character's advance widened by an equal
            // share at font: 16px monospace).
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='d' style='margin:0;width:150pt;font:16px monospace;text-align:justify'>" +
                "一二三四五六七八九十一二三四五六七八九十一二三四五六七八九十</div>"));

            var d = LayoutHarness.FindById(root, "d")!;
            var line = d.LineBoxes[0];
            var gaps = GapsOn(line);

            Assert.True(d.LineBoxes.Count >= 2, "fixture must wrap so that line 0 is genuinely justified");
            Assert.True(gaps.Count >= 3, "fixture must put several characters on the justified line");
            Assert.All(gaps, gap => Assert.True(gap > 0, $"expected inter-character expansion (gap={gap:F3})"));
            Assert.All(gaps, gap => Assert.Equal(gaps[0], gap, 3));
            Assert.Equal(d.ClientRight, line.Words[^1].Right, 3);
        }

        [Fact]
        public async Task CjkAndWordSeparatorsOnOneLine_ShareOneExpansionAmount()
        {
            // §6.4.1: all opportunities in one priority level expand equally "regardless of which
            // typographic character units created that opportunity". Chromium measures the same 0.297px
            // at a CJK boundary and on top of a space on this same fixture, which is what makes a single
            // count and a single share the right model here.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='d' style='margin:0;width:150pt;font:16px monospace;text-align:justify'>" +
                "一二AB三四 CD 五六七八九十一二三四五六七八九十</div>"));

            var d = LayoutHarness.FindById(root, "d")!;
            var line = d.LineBoxes[0];
            var words = line.Words;

            Assert.True(d.LineBoxes.Count >= 2, "fixture must wrap so that line 0 is genuinely justified");

            var cd = words.Single(w => w.Text == "CD");
            var cdIndex = words.IndexOf(cd);

            // The Latin/CJK boundaries on this line (二|AB and AB|三) carry only the distributed share;
            // the two word-separator gaps around CD carry that same share on top of a natural space.
            var ab = words.Single(w => w.Text == "AB");
            var abIndex = words.IndexOf(ab);

            var cjkBoundaryGap = ab.Left - words[abIndex - 1].Right;
            var separatorGap = cd.Left - words[cdIndex - 1].Right;
            var naturalSpace = separatorGap - cjkBoundaryGap;

            Assert.True(cjkBoundaryGap > 0, $"expected the CJK/Latin boundary to expand (gap={cjkBoundaryGap:F3})");
            Assert.True(naturalSpace > 6, $"expected the separator gap to also carry its natural space " +
                                          $"(separator={separatorGap:F3}, boundary={cjkBoundaryGap:F3})");
            Assert.Equal(d.ClientRight, words[^1].Right, 3);
        }

        [Fact]
        public async Task LineWithNoJustificationOpportunityAtAll_IsLeftStartAligned()
        {
            // §6.4.3 unexpandable text: with nothing to expand, the line aligns as text-align-last,
            // whose initial `auto` under justify is start. A <br> makes line 0 a non-last line that holds
            // one contiguous run - the shape that used to be flushed to the end edge instead.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='d' style='{Measure}'>A<span>B</span><br>CD EF</div>"));

            var d = LayoutHarness.FindById(root, "d")!;
            var line = d.LineBoxes[0];

            Assert.True(d.LineBoxes.Count >= 2, "fixture must produce a non-last line to justify");
            Assert.Equal(d.ClientLeft, line.Words[0].Left, 3);
            Assert.Equal(line.Words[0].Right, line.Words[1].Left, 3);
            Assert.True(line.Words[^1].Right < d.ClientRight - 100,
                "an unexpandable line must stay at its natural width, not be stretched to the measure");
        }

        [Fact]
        public async Task AtomicInlineWithNoSourceSpaceAroundIt_GetsNoExpansion()
        {
            // The other half of the issue: an inline <svg> (or <img>) with no white space beside it is
            // as contiguous with its neighbours as <span>Y</span><span>X</span> is - css-text-3 §4.1.1 -
            // and issue #1011 already stopped it reserving a natural space of its own. Justification was
            // the one remaining path that put a gap there anyway.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='d' style='{Measure}'>A<svg id='g' width='12' height='12'><rect width='12' " +
                "height='12' fill='red'/></svg>B CD EF GH IJ KL MN OP QR ST UV WX YZ AB CD EF</div>"));

            var d = LayoutHarness.FindById(root, "d")!;
            var line = d.LineBoxes[0];

            Assert.True(d.LineBoxes.Count >= 2, "fixture must wrap so that line 0 is genuinely justified");

            var svgWord = line.Words[1];
            Assert.True(svgWord.IsImage, "fixture must place the inline <svg> as an atomic inline word");
            Assert.Equal(line.Words[0].Right, svgWord.Left, 3);
            Assert.Equal(svgWord.Right, line.Words[2].Left, 3);
            Assert.Equal(d.ClientRight, line.Words[^1].Right, 3);
        }

        [Fact]
        public async Task PreservedWhiteSpaceRun_IsAJustificationOpportunity()
        {
            // Under white-space: pre-wrap the flow emits the space as its own IsSpaces word rather than
            // as an advance, so neither neighbour carries a collapsible-white-space flag. css-text-3
            // §6.1 permits a UA to treat non-collapsible white space as offering no opportunity, but
            // taking that option would stop a pre-wrap block justifying at all - so the boundary after
            // a preserved run counts, once per run.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='d' style='{Measure};white-space:pre-wrap'>AA BB CC DD EE FF GG HH II JJ KK " +
                "LL MM NN OO PP QQ RR</div>"));

            var d = LayoutHarness.FindById(root, "d")!;
            var line = d.LineBoxes[0];
            var content = line.Words.Where(w => !w.IsSpaces).ToList();

            Assert.True(d.LineBoxes.Count >= 2, "fixture must wrap so that line 0 is genuinely justified");
            Assert.Contains(line.Words, w => w.IsSpaces);

            var gaps = content.Skip(1).Select((word, index) => word.Left - content[index].Right).ToList();
            Assert.All(gaps, gap => Assert.Equal(gaps[0], gap, 3));
            Assert.Equal(d.ClientRight, line.Words[^1].Right, 3);
        }

        [Fact]
        public async Task VerticalJustify_ExpandsOnlyAtWordSeparators()
        {
            // The vertical-writing-mode counterpart: ApplyVerticalJustifyAlignment had the identical
            // per-word model, and now shares the same opportunity predicate along the inline (physical Y)
            // axis. `vertical-rl` with direction:ltr runs inline-start from the physical top.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='d' style='margin:0;writing-mode:vertical-rl;width:200pt;height:150pt;" +
                "font:16px monospace;text-align:justify'>A<span>B</span> CD EF GH IJ KL MN OP QR ST UV " +
                "WX YZ</div>"));

            var d = LayoutHarness.FindById(root, "d")!;
            var column = d.LineBoxes[0];
            var words = column.Words.Where(w => !w.IsLineBreak && !w.IsSpaces).ToList();

            Assert.True(d.LineBoxes.Count >= 2, "fixture must wrap so that column 0 is genuinely justified");
            Assert.Equal("A", words[0].Text);
            Assert.Equal("B", words[1].Text);
            Assert.Equal(d.ClientTop, words[0].Top, 3);
            Assert.Equal(words[0].Bottom, words[1].Top, 3);
            Assert.Equal(d.ClientBottom, words[^1].Bottom, 3);
        }
    }
}
