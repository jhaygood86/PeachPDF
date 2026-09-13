using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression coverage for issue #1021: <c>text-align: justify</c> stretched the last line
    /// <i>before a forced break</i> to the full measure, because the only line it exempted was the
    /// block's own last one. <see href="https://www.w3.org/TR/css-text-3/#text-align-property">css-text-3
    /// §6.1</see> ends a paragraph at a forced break too - "unless otherwise specified by
    /// <c>text-align-last</c>, the last line before a forced line break is start-aligned" - so a
    /// <c>&lt;br&gt;</c>-separated address block or verse came out justified where a browser leaves it
    /// ragged.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Closing it meant implementing <c>text-align-last</c> (§6.3) as well, since "start-aligned" is only
    /// its <i>initial</i> <c>auto</c> behaviour: both kinds of paragraph-ending line, and §6.4.3's
    /// unexpandable line, now route through it rather than through a hard-coded start alignment.
    /// </para>
    /// <para>
    /// Most fixtures here assert only against the block's own <c>ClientLeft</c>/<c>ClientRight</c> and
    /// against a left-aligned control of the same document, never an absolute width, so nothing depends on
    /// which font the host resolves (issue #956's trap). The two that need to know what a word is
    /// <i>worth</i> - the §6.4.3 pair - derive it from the laid-out word's own width.
    /// </para>
    /// </remarks>
    public class TextAlignLastTests
    {
        private const string Justify = "text-align:justify;font-size:10pt;line-height:18pt;orphans:1;widows:1";

        private static string Words(int count, string prefix = "w") =>
            string.Join(" ", Enumerable.Range(0, count).Select(i => $"{prefix}{i}"));

        private static async Task<(CssBox Block, HtmlContainerInt Container)> BlockAsync(
            string body, double pageWidth = 200, double pageHeight = 800)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(body), pageWidth: pageWidth, pageHeight: pageHeight, margin: 10);

            return (LayoutHarness.FindById(root, "p")!, container);
        }

        /// <summary>Whether <paramref name="line"/> was stretched to its own measure.</summary>
        private static bool IsStretched(CssLineBox line) =>
            line.Words.Count > 0 && line.Words[^1].Right > line.ContentRight - 1;

        private static IReadOnlyList<CssLineBox> NonEmpty(CssBox block) =>
            block.LineBoxes.Where(line => line.Words.Any(w => !w.IsLineBreak)).ToList();

        /// <summary>
        /// Asserts <paramref name="line"/> is centred in its own measure, by the one font-independent
        /// statement of it: the space left of its first word equals the space right of its last.
        /// </summary>
        private static void AssertCentred(CssLineBox line)
        {
            var leading = line.Words[0].Left - line.ContentLeft;
            var trailing = line.ContentRight - line.Words[^1].Right;

            // A stretched line has two zero gutters, which would satisfy the equality on its own - so the
            // gutter has to be real for this to say anything at all.
            Assert.True(leading > 1, $"line is not centred, it fills its measure (leading gutter={leading:F1})");
            Assert.Equal(leading, trailing, 1);
        }

        [Fact]
        public async Task TheLineBeforeAForcedBreak_IsNotJustified()
        {
            // The issue's own shape, widened so the first paragraph also wraps naturally: the wrapped
            // lines are justified as always, and only the one the <br> ends is left ragged.
            var (block, _) = await BlockAsync(
                $"<p id='p' style='{Justify}'>{Words(30)}<br>{Words(30, "x")}</p>");

            var lines = NonEmpty(block);
            var beforeBreak = lines.Single(line => line.PrecedesForcedBreak);

            Assert.True(lines.Count > 3, "fixture must wrap either side of the break");
            Assert.False(IsStretched(beforeBreak),
                $"the line before the <br> was justified: ends at {beforeBreak.Words[^1].Right:F1} "
                + $"against a measure of {beforeBreak.ContentRight:F1}");

            // Every line that neither ends a paragraph nor is the block's last is still justified - the
            // half that must not regress.
            var stillJustified = lines.SkipLast(1).Where(line => !line.PrecedesForcedBreak).ToList();
            Assert.NotEmpty(stillJustified);
            Assert.All(stillJustified, line => Assert.True(IsStretched(line),
                $"an ordinary line stopped justifying: ends at {line.Words[^1].Right:F1}"));
        }

        [Fact]
        public async Task TheLineBeforeAForcedBreak_KeepsTheNaturalSpacingOfALeftAlignedLine()
        {
            // "Not stretched" is the weaker claim; this is the one the reader sees. Word for word, the
            // exempt line sits exactly where text-align: left would have put it.
            const string body = "<p id='p' style='{0}'>AA BB CC DD EE FF<br>{1}</p>";

            var (justified, _) = await BlockAsync(string.Format(body, Justify, Words(30, "x")));
            var (control, _) = await BlockAsync(string.Format(body, Justify + ";text-align:left", Words(30, "x")));

            var exempt = justified.LineBoxes[0];
            var natural = control.LineBoxes[0];

            Assert.True(exempt.PrecedesForcedBreak);
            Assert.Equal(natural.Words.Count, exempt.Words.Count);
            Assert.All(Enumerable.Range(0, exempt.Words.Count),
                i => Assert.Equal(natural.Words[i].Left, exempt.Words[i].Left, 3));
        }

        [Fact]
        public async Task APreservedNewline_ExemptsItsLineJustAsABrDoes()
        {
            // A preserved newline under white-space: pre-line is the same forced break (css-text-3 §5.5),
            // reaching the flow as the same synthetic line-break word.
            var (block, _) = await BlockAsync(
                $"<p id='p' style='{Justify};white-space:pre-line'>AA BB CC DD EE FF\n{Words(12, "x")}</p>");

            var beforeBreak = NonEmpty(block).Single(line => line.PrecedesForcedBreak);

            Assert.False(IsStretched(beforeBreak),
                $"the line before the preserved newline was justified: ends at {beforeBreak.Words[^1].Right:F1}");
        }

        [Fact]
        public async Task AForcedBreakInAPaginatedBlock_ExemptsALineAnEarlierPassAlreadyFinalized()
        {
            // The fragmentation half of the issue. A pass that stops at a page break finalizes its lines
            // with blockFinished: false and cannot see the line the <br> opens - which is why the flow
            // states PrecedesForcedBreak on the line it closes, at the moment it closes it, rather than
            // looking ahead. Without that, this line aligns before its successor exists.
            var (block, container) = await BlockAsync(
                $"<p id='p' style='{Justify}'>{Words(120)}<br>{Words(240, "x")}</p>", pageHeight: 300);

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1,
                "fixture does not paginate, so it asserts nothing");

            var beforeBreak = NonEmpty(block).Single(line => line.PrecedesForcedBreak);
            var lastPage = container.FragmentTree.Fragmentainers.Count - 1;

            Assert.True(container.SlotStartingAt(beforeBreak.Words[0].Top) < lastPage,
                "fixture must put the break's own line on a page an earlier pass already finalized");
            Assert.False(IsStretched(beforeBreak),
                $"the line before the <br> was justified: ends at {beforeBreak.Words[^1].Right:F1}");
        }

        [Fact]
        public async Task TextAlignLastJustify_JustifiesBothKindsOfParagraphEndingLine()
        {
            // §6.3's `justify`: the author asks for the exemption not to apply at all, to the block's own
            // last line and to the line before a forced break alike.
            var (block, _) = await BlockAsync(
                $"<p id='p' style='{Justify};text-align-last:justify'>{Words(30)}<br>{Words(30, "x")}</p>");

            var lines = NonEmpty(block);

            Assert.True(lines.Count > 3, "fixture must wrap either side of the break");
            Assert.All(lines, line => Assert.True(IsStretched(line),
                $"text-align-last: justify left a line ragged: ends at {line.Words[^1].Right:F1}"));
        }

        [Fact]
        public async Task TextAlignLastCenter_CentersBothKindsOfParagraphEndingLine()
        {
            var (block, _) = await BlockAsync(
                $"<p id='p' style='{Justify};text-align-last:center'>{Words(30)}<br>{Words(30, "x")}</p>");

            var lines = NonEmpty(block);

            AssertCentred(lines.Single(l => l.PrecedesForcedBreak));
            AssertCentred(lines[^1]);
        }

        [Fact]
        public async Task TextAlignLastAuto_UnderANonJustifyTextAlign_DefersToTextAlign()
        {
            // §6.3: `auto` means "align as text-align says", and only the justify case is special-cased
            // into start. A centered block therefore centers every line, its last included - the
            // behaviour that must survive routing the last line through text-align-last at all.
            var (block, _) = await BlockAsync(
                $"<p id='p' style='{Justify};text-align:center'>{Words(30)}<br>{Words(30, "x")}</p>");

            var lines = NonEmpty(block);

            Assert.True(lines.Count > 3, "fixture must wrap either side of the break");

            // Every line is centred, and the two that end a paragraph - the ones now routed through
            // text-align-last - are centred with a gutter wide enough to prove it, which a line wrapped
            // nearly to its measure cannot.
            Assert.All(lines, line => Assert.Equal(
                line.Words[0].Left - line.ContentLeft, line.ContentRight - line.Words[^1].Right, 1));
            AssertCentred(lines.Single(l => l.PrecedesForcedBreak));
            AssertCentred(lines[^1]);
        }

        [Theory]
        // Physical keywords mean the same edge whatever the direction; start/end swap with it.
        [InlineData("start", "ltr", false)]
        [InlineData("end", "ltr", true)]
        [InlineData("left", "ltr", false)]
        [InlineData("right", "ltr", true)]
        [InlineData("start", "rtl", true)]
        [InlineData("end", "rtl", false)]
        [InlineData("left", "rtl", false)]
        [InlineData("right", "rtl", true)]
        public async Task TextAlignLast_FlushesTheLastLineToTheEdgeItNames(string value, string direction, bool toRight)
        {
            var (block, _) = await BlockAsync(
                $"<p id='p' style='{Justify};direction:{direction};text-align-last:{value}'>{Words(30)}</p>");

            var last = NonEmpty(block)[^1];

            Assert.True(block.LineBoxes.Count > 1, "fixture must wrap, so the last line is genuinely short");

            // Max/min over the whole line rather than the first/last word: bidi reordering runs after
            // alignment, so document order is not physical order under direction: rtl.
            if (toRight)
                Assert.Equal(last.ContentRight, last.Words.Max(w => w.Right), 1);
            else
                Assert.Equal(last.ContentLeft, last.Words.Min(w => w.Left), 1);
        }

        [Fact]
        public async Task TextAlignLast_IsInherited()
        {
            // css-text-3 §6.3 declares it Inherited: yes, so a declaration on an ancestor reaches the
            // block whose lines it governs.
            var (block, _) = await BlockAsync(
                $"<div style='text-align-last:right'><p id='p' style='{Justify}'>{Words(30)}</p></div>");

            var last = NonEmpty(block)[^1];

            Assert.Equal(last.ContentRight, last.Words[^1].Right, 1);
        }

        [Fact]
        public async Task AnRtlJustifiedBlock_FlushesItsLastLineToTheEndEdge()
        {
            // `auto` under justify is *start*, not physical left - which for direction: rtl is the right
            // edge. Reading it as "leave the line where the flow put it" left an RTL paragraph's last
            // line stranded against the wrong edge.
            var (block, _) = await BlockAsync(
                $"<p id='p' style='{Justify};direction:rtl'>{Words(30)}</p>");

            var last = NonEmpty(block)[^1];

            Assert.True(last.Words.Count > 1, "fixture must put several words on the last line");
            Assert.Equal(last.ContentRight, last.Words.Max(w => w.Right), 1);
        }

        [Fact]
        public async Task AnRtlTextAlignLastJustify_StillStartAlignsAClosingLineItCannotStretch()
        {
            // text-align-last: justify hands the closing line to the justify path, which has nothing to
            // do with a line holding one word. css-text-3 §6.4.3 hands such a line to text-align-last,
            // and its parenthetical asks for centre when that is itself justify; PeachPDF start-aligns
            // instead, matching Chromium/Gecko/WebKit - see the accepted-gap file
            // unexpandable-justified-line-starts-rather-than-centres.md. What this asserts is the part
            // that is not in question: start under RTL is the physical *right* edge, so reading §6.4.3
            // as "leave the line alone" strands it at the left, worse than not declaring the property.
            var (block, _) = await BlockAsync(
                $"<p id='p' style='{Justify};direction:rtl;text-align-last:justify'>A<span>B</span></p>");

            var last = Assert.Single(block.LineBoxes);

            // Two words the source has no white space between: no opportunity, so nothing to stretch,
            // whatever text-align-last asked for.
            Assert.Equal(2, last.Words.Count);
            Assert.Equal(last.Words[0].Right, last.Words[1].Left, 1);
            Assert.Equal(last.ContentRight, last.Words.Max(w => w.Right), 1);
        }

        [Fact]
        public async Task AnRtlOverflowingJustifiedLine_FlushesToTheStartEdgeAndSpillsPastTheOther()
        {
            // §6.1: "if the inline contents of a line box are too long to fit within it, then the
            // contents are start-aligned: any content that doesn't fit overflows the line box's end edge".
            // Under RTL that is flush right, spilling past the left - the same negative-diff shift
            // ApplyRightAlignment performs for a plain RTL paragraph. Leaving such a line untouched put it
            // flush *left*, spilling the wrong way.
            var (block, _) = await BlockAsync(
                $"<p id='p' style='{Justify};direction:rtl;white-space:nowrap'>{Words(30)}</p>");

            var line = Assert.Single(block.LineBoxes);

            Assert.True(line.Words.Max(w => w.Right) - line.Words.Min(w => w.Left) > line.ContentRight - line.ContentLeft,
                "fixture must actually overflow its measure for this test to mean anything");
            Assert.Equal(line.ContentRight, line.Words.Max(w => w.Right), 1);
            Assert.True(line.Words.Min(w => w.Left) < line.ContentLeft,
                "an overflowing RTL line must spill past the physical left edge, not the right");
        }

        [Fact]
        public async Task VerticalJustify_TextAlignLastCentersTheLastColumn()
        {
            // The vertical dispatcher shares ResolveUsedAlignment, so a non-auto text-align-last governs a
            // column along its own inline (physical Y) axis exactly as it governs a horizontal line.
            var (block, _) = await BlockAsync(
                $"<p id='p' style='{Justify};text-align-last:center;writing-mode:vertical-rl;height:200pt'>"
                + $"{Words(30)}</p>", pageWidth: 400);

            var columns = NonEmpty(block);
            var last = columns[^1];

            Assert.True(columns.Count > 1, "fixture must wrap onto several columns");

            var leading = last.Words.Min(w => w.Top) - block.ClientTop;
            var trailing = block.ClientBottom - last.Words.Max(w => w.Bottom);

            Assert.True(leading > 1, $"the last column is not centred, it fills its measure (leading={leading:F1})");
            Assert.Equal(leading, trailing, 1);
        }

        [Fact]
        public async Task AJustifiedLineWithNothingToExpandAt_StaysAtTheStartEdge()
        {
            // §6.4.3 unexpandable text: one word is one word whatever the measure, so the line aligns as
            // text-align-last - start, by default. The control for the pair below.
            var (block, _) = await BlockAsync(
                $"<p id='p' style='{Justify}'>{Unexpandable}</p>");

            var first = block.LineBoxes[0];

            Assert.Equal(2, first.Words.Count);
            Assert.True(block.LineBoxes.Count > 2, "fixture must leave a line after the unexpandable one");
            Assert.Equal(first.ContentLeft, first.Words[0].Left, 1);
            Assert.Equal(first.Words[0].Right, first.Words[1].Left, 1);
        }

        [Fact]
        public async Task AJustifiedLineWithNothingToExpandAt_AlignsAsTextAlignLast()
        {
            // The same §6.4.3 rule with the property actually set: unexpandable text is not merely left
            // alone, it is handed to text-align-last - on a line that is not a paragraph's last at all.
            var (block, _) = await BlockAsync(
                $"<p id='p' style='{Justify};text-align-last:center'>{Unexpandable}</p>");

            var first = block.LineBoxes[0];

            Assert.Equal(2, first.Words.Count);
            Assert.True(block.LineBoxes.Count > 2, "fixture must leave a line after the unexpandable one");
            Assert.Equal(first.Words[0].Right, first.Words[1].Left, 1);
            AssertCentred(first);
        }

        [Fact]
        public async Task VerticalJustify_ExemptsTheColumnBeforeAForcedBreak()
        {
            // CreateVerticalLineBoxes runs its own flow, so it states PrecedesForcedBreak itself; the
            // column a <br> closes ends a paragraph along the inline (physical Y) axis exactly as a
            // horizontal line does.
            var (block, _) = await BlockAsync(
                $"<p id='p' style='{Justify};writing-mode:vertical-rl;height:200pt'>"
                + $"{Words(30)}<br>{Words(30, "x")}</p>", pageWidth: 400);

            var columns = NonEmpty(block);
            var beforeBreak = columns.Single(column => column.PrecedesForcedBreak);

            Assert.True(columns.Count > 3, "fixture must wrap either side of the break");
            Assert.True(beforeBreak.Words.Max(w => w.Bottom) < block.ClientBottom - 1,
                $"the column before the <br> was justified: ends at "
                + $"{beforeBreak.Words.Max(w => w.Bottom):F1} against {block.ClientBottom:F1}");

            var stillJustified = columns.SkipLast(1).Where(column => !column.PrecedesForcedBreak).ToList();
            Assert.NotEmpty(stillJustified);
            Assert.All(stillJustified, column =>
                Assert.Equal(block.ClientBottom, column.Words.Max(w => w.Bottom), 1));
        }

        /// <summary>
        /// §6.4.3's shape: a first line holding two words the source has no white space between - so no
        /// justification opportunity at all - forced to end there by a following token no measure can
        /// hold. Deliberately not a <c>&lt;br&gt;</c>, which would make the line end a paragraph instead
        /// and never reach the unexpandable-text rule.
        /// </summary>
        private static string Unexpandable =>
            "A<span>B</span> " + new string('M', 80) + " " + Words(6, "x");
    }
}
