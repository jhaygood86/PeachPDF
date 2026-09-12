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
    /// <para>
    /// Every fixture here embeds a <b>bundled</b> font rather than naming <c>monospace</c> (or leaving a
    /// CJK character to whatever the host resolves): which words land on the justified line, and how wide
    /// a natural word space is, are both font-metric questions, and a bare family keyword answers them
    /// differently on each platform - macOS's CoreText resolves both to narrower glyphs than Windows or
    /// Ubuntu do. That is issue #956's trap, and <see cref="BundledFonts.FontFaceRule"/> is the
    /// infrastructure this repo already built for it.
    /// </para>
    /// <para>
    /// No assertion states an absolute width even so. Where a test needs to know what a natural word
    /// space measures, it lays the same document out <c>text-align: left</c> and reads it back
    /// (<see cref="NaturalGapBeforeAsync"/>), which says what the test actually means - "justification
    /// widened the real space" - rather than a number that would have to be re-derived by hand every
    /// time the fixture or the embedded font changed.
    /// </para>
    /// <para>
    /// The behaviour itself was measured in Chromium through the repo's own Playwright dependency
    /// (<c>page.Locator(...).BoundingBoxAsync()</c> over one span per word), at <c>font: 16px
    /// monospace</c> in a <c>200pt</c>-wide block: <c>A&lt;span&gt;B&lt;/span&gt;</c> contiguous at a 0
    /// gap, every inter-word gap on the line identical (10.094px), and a lone overflowing word on a
    /// justified non-last line left at the line's start edge.
    /// </para>
    /// </remarks>
    public class JustificationOpportunityTests
    {
        private const string MonoFamily = "JustifyTestMono";
        private const string CjkFamily = "JustifyTestCjk";

        /// <summary>Source Code Pro - monospaced, Latin, every advance 600/1000 em.</summary>
        private static string MonoDoc(string content, string extraStyle = "", string width = "200pt") =>
            Document(BundledFonts.Otf, "font/opentype", MonoFamily, content, extraStyle, width);

        /// <summary>
        /// Noto Sans JP subset - CJK ideographs (你 好 書 縦, each a full 1000/1000-em advance) and the
        /// basic Latin alphabet in one face, so every character in a mixed fixture resolves to the same
        /// font and nothing here depends on per-codepoint fallback.
        /// </summary>
        private static string CjkDoc(string content, string extraStyle = "", string width = "150pt") =>
            Document(BundledFonts.Cjk, "font/truetype", CjkFamily, content, extraStyle, width);

        private static string Document(string fontPath, string mimeType, string family, string content,
            string extraStyle, string width) =>
            $"<!DOCTYPE html><html><head><style>{BundledFonts.FontFaceRule(fontPath, family, mimeType)}</style></head>" +
            $"<body style='margin:0'><div id='d' style=\"margin:0;width:{width};font:16px '{family}';" +
            $"text-align:justify;{extraStyle}\">{content}</div></body></html>";

        private static IReadOnlyList<double> GapsOn(CssLineBox line) =>
            line.Words.Skip(1).Select((word, index) => word.Left - line.Words[index].Right).ToList();

        private static async Task<CssBox> BlockOfAsync(string document)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(document);
            return LayoutHarness.FindById(root, "d")!;
        }

        /// <summary>
        /// The gap the flow puts before the first word reading <paramref name="wordText"/> when the same
        /// document is left-aligned - its natural, unjustified advance. Reading it back rather than
        /// hard-coding it is what keeps these tests independent of the embedded font's own metrics.
        /// </summary>
        private static async Task<double> NaturalGapBeforeAsync(string justifiedDocument, string wordText)
        {
            var block = await BlockOfAsync(justifiedDocument.Replace("text-align:justify", "text-align:left"));
            var words = block.LineBoxes[0].Words;
            var index = words.FindIndex(w => w.Text == wordText);

            Assert.True(index > 0, $"left-aligned control must place '{wordText}' on line 0 with a word before it");
            return words[index].Left - words[index - 1].Right;
        }

        [Fact]
        public async Task AdjacentInlineBoxesWithNoSourceSpace_GetNoExpansion()
        {
            // The issue's own repro. `A<span>B</span>` is one run with no white space in it, so the
            // boundary is not a justification opportunity and the two words render contiguous - exactly
            // as they do under text-align: left, and as Chromium renders them.
            var d = await BlockOfAsync(MonoDoc(
                "A<span id='s'>B</span> CD EF GH IJ KL MN OP QR ST UV WX YZ AB CD EF GH IJ"));
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
            var document = MonoDoc("A<span>B</span> CD EF GH IJ KL MN OP QR ST UV WX YZ AB CD EF GH IJ");

            var d = await BlockOfAsync(document);
            var line = d.LineBoxes[0];

            // Gap 0 is the non-opportunity A|B boundary; every remaining gap is a word separator.
            var separatorGaps = GapsOn(line).Skip(1).ToList();

            Assert.True(separatorGaps.Count >= 3, "fixture must hold several word separators to compare");
            Assert.All(separatorGaps, gap => Assert.Equal(separatorGaps[0], gap, 3));

            // §6.4.1: the distributed space is *in addition to* the natural word space, never a
            // replacement for it - so every separator gap must come out wider than the flow's own.
            var naturalGap = await NaturalGapBeforeAsync(document, "CD");
            Assert.True(separatorGaps[0] > naturalGap,
                $"each separator gap must exceed the natural space it expands from " +
                $"(justified={separatorGaps[0]:F3}, natural={naturalGap:F3})");
            Assert.Equal(d.ClientRight, line.Words[^1].Right, 3);
        }

        [Fact]
        public async Task WhitespaceOnlyInlineBoxBetweenTwoWords_IsAJustificationOpportunity()
        {
            // `<span>AA</span> <span>BB</span>`: the space belongs to neither word - CssBox.ParseToWords
            // emits no word for a collapsible-whitespace-only text node, and FlowBox instead advances the
            // cursor for the box. Without CssRect.PrecededByWordSeparator recording that advance, this
            // very ordinary markup would read as contiguous and get no expansion at all.
            var d = await BlockOfAsync(MonoDoc(
                "<span>AA</span> <span>BB</span> CD EF GH IJ KL MN OP QR ST UV WX YZ AB"));
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
            var d = await BlockOfAsync(MonoDoc(
                "well-known AA BB CC DD EE FF GG HH II JJ KK LL MM NN OO"));
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
            // line justify - and Chromium does justify it, widening each character's advance by an equal
            // share.
            var d = await BlockOfAsync(CjkDoc("你好書縦你好書縦你好書縦你好書縦你好書縦你好書縦你好書縦你好書縦"));
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
            // at a CJK boundary and on top of a space on the equivalent fixture, which is what makes a
            // single count and a single share the right model here.
            var document = CjkDoc("你好AB書縦 CD 你好書縦你好書縦你好書縦你好書縦");

            var d = await BlockOfAsync(document);
            var line = d.LineBoxes[0];
            var words = line.Words;

            Assert.True(d.LineBoxes.Count >= 2, "fixture must wrap so that line 0 is genuinely justified");

            // ParseToWords breaks *after* the first ideograph it meets, so a Latin run followed by one
            // comes out as a single word ("AB書") - the boundary inside it is invisible to every layer,
            // not just to justification. 好|AB書 is still a real boundary, and the one this asserts on.
            var latinIndex = words.FindIndex(w => w.Text is { Length: > 1 } t && t.StartsWith("AB"));
            var cdIndex = words.FindIndex(w => w.Text == "CD");
            Assert.True(latinIndex > 0 && cdIndex > 0, "fixture must put both Latin runs on the justified line");

            // Two opportunities of different kinds, one share each: an ideograph-to-ideograph boundary
            // (你|好) and an ideograph-to-Latin one (好|AB書) carry the distributed share alone, while the
            // word-separator gap before CD carries that same share on top of its natural space.
            var interCharacterGap = words[1].Left - words[0].Right;
            var cjkToLatinGap = words[latinIndex].Left - words[latinIndex - 1].Right;
            var separatorGap = words[cdIndex].Left - words[cdIndex - 1].Right;
            var naturalSpace = await NaturalGapBeforeAsync(document, "CD");

            Assert.True(interCharacterGap > 0,
                $"expected the ideographic boundary to expand (gap={interCharacterGap:F3})");
            Assert.Equal(interCharacterGap, cjkToLatinGap, 3);
            Assert.Equal(interCharacterGap, separatorGap - naturalSpace, 3);
            Assert.Equal(d.ClientRight, words[^1].Right, 3);
        }

        [Fact]
        public async Task LineWithNoJustificationOpportunityAtAll_IsLeftStartAligned()
        {
            // §6.4.3 unexpandable text: with nothing to expand, the line aligns as text-align-last,
            // whose initial `auto` under justify is start. A <br> makes line 0 a non-last line that holds
            // one contiguous run - the shape that used to be flushed to the end edge instead.
            var d = await BlockOfAsync(MonoDoc("A<span>B</span><br>CD EF"));
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
            var d = await BlockOfAsync(MonoDoc(
                "A<svg id='g' width='12' height='12'><rect width='12' height='12' fill='red'/></svg>B " +
                "CD EF GH IJ KL MN OP QR ST UV WX YZ AB CD EF"));
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
            var d = await BlockOfAsync(MonoDoc(
                "AA BB CC DD EE FF GG HH II JJ KK LL MM NN OO PP QQ RR", "white-space:pre-wrap"));
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
            var d = await BlockOfAsync(MonoDoc(
                "A<span>B</span> CD EF GH IJ KL MN OP QR ST UV WX YZ",
                "writing-mode:vertical-rl;height:150pt"));

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
