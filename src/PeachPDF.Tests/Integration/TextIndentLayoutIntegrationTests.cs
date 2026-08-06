using PeachPDF.Html.Core;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <c>text-indent</c>'s <c>hanging</c>/<c>each-line</c> keywords
    /// (<see href="https://www.w3.org/TR/css-text-3/#text-indent-property">CSS Text 3 §3</see>) and its
    /// direction-aware placement under <c>direction: rtl</c>. <c>TextIndent_AppliesToTheFirstLineOnly_
    /// NotTheResumedOne</c> in <see cref="ResumableInlineLayoutIntegrationTests"/> covers the plain
    /// (no-keyword) fragmentation case this file doesn't repeat.
    /// </summary>
    public class TextIndentLayoutIntegrationTests
    {
        [Fact]
        public async Task Initial_IsTreatedAsNoIndent()
        {
            // DomParser resolves the "initial" keyword to the registered initial value ("0") before it
            // ever reaches CssBox.TextIndent, so this exercises that resolution end to end rather than
            // the unparseable-value fallback in NoEmsTextIndent/EnsureTextIndentResolved (which no value
            // reachable through the normal cascade can trigger, since Layer A already validates).
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<p id='p' style='margin:0;font-size:10pt;text-indent:initial'>No indent here at all.</p>"));

            var p = LayoutHarness.FindById(root, "p")!;
            Assert.Equal(0, p.ActualTextIndent);
            Assert.False(p.ActualTextIndentHanging);
            Assert.False(p.ActualTextIndentEachLine);
            Assert.Equal(p.ClientLeft, p.LineBoxes[0].Words.Min(w => w.Left), 2);
        }

        [Fact]
        public async Task Hanging_IndentsEveryLineExceptTheFirst()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<p id='p' style='margin:0;font-size:10pt;width:150pt;text-indent:40pt hanging'>" +
                    "This paragraph has enough words in it to wrap onto at least two separate lines for the test.</p>"));

            var p = LayoutHarness.FindById(root, "p")!;
            Assert.True(p.LineBoxes.Count >= 2, "expected the paragraph to wrap onto at least two lines");

            Assert.Equal(p.ClientLeft, p.LineBoxes[0].Words.Min(w => w.Left), 2);
            Assert.Equal(p.ClientLeft + 40, p.LineBoxes[1].Words.Min(w => w.Left), 2);
        }

        [Fact]
        public async Task EachLine_IndentsTheFirstLineAndEveryLineAfterAForcedBreak_ButNotASoftWrapContinuation()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<p id='p' style='margin:0;font-size:10pt;width:150pt;text-indent:40pt each-line'>" +
                    "AAA<br>BBB<br>a very long line of text that is certain to wrap onto a continuation line<br>CCC</p>"));

            var p = LayoutHarness.FindById(root, "p")!;
            var lines = p.LineBoxes;
            Assert.True(lines.Count > 4, $"expected the long line to wrap, got {lines.Count} lines total");

            foreach (var line in lines)
            {
                var isFirstLine = line == lines[0];
                var expectedLeft = isFirstLine || line.FollowsForcedBreak ? p.ClientLeft + 40 : p.ClientLeft;
                Assert.Equal(expectedLeft, line.Words.Min(w => w.Left), 2);
            }
        }

        [Fact]
        public async Task HangingEachLine_IndentsOnlySoftWrapContinuations()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<p id='p' style='margin:0;font-size:10pt;width:150pt;text-indent:40pt hanging each-line'>" +
                    "AAA<br>BBB<br>a very long line of text that is certain to wrap onto a continuation line<br>CCC</p>"));

            var p = LayoutHarness.FindById(root, "p")!;
            var lines = p.LineBoxes;
            Assert.True(lines.Count > 4, $"expected the long line to wrap, got {lines.Count} lines total");

            foreach (var line in lines)
            {
                var isFirstLine = line == lines[0];
                var expectedLeft = isFirstLine || line.FollowsForcedBreak ? p.ClientLeft : p.ClientLeft + 40;
                Assert.Equal(expectedLeft, line.Words.Min(w => w.Left), 2);
            }
        }

        [Fact]
        public async Task Justify_EachLine_RespectsThePerLineIndentBeforeSpreadingWords()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<p id='p' style='margin:0;font-size:10pt;width:150pt;text-align:justify;text-indent:40pt each-line'>" +
                    "AAA BBB<br>a very long line of text that is certain to wrap onto a continuation line<br>CCC DDD EEE</p>"));

            var p = LayoutHarness.FindById(root, "p")!;
            var lines = p.LineBoxes;
            Assert.True(lines.Count > 3, $"expected the long line to wrap, got {lines.Count} lines total");

            // Justify exempts the block's own last line (CSS Text §7.3) - only lines before it are
            // spread, so only those are asserted on here. A single-word line is also skipped: justify's
            // own "force the last word flush" step (unrelated to text-indent) always wins over that
            // word's indent-derived position when it is simultaneously the line's only word, which is
            // true with or without text-indent involved at all.
            for (var i = 0; i < lines.Count - 1; i++)
            {
                var line = lines[i];
                if (line.Words.Count <= 1) continue;

                var isFirstLine = line == lines[0];
                var expectedLeft = isFirstLine || line.FollowsForcedBreak ? p.ClientLeft + 40 : p.ClientLeft;
                Assert.Equal(expectedLeft, line.Words.Min(w => w.Left), 2);
            }
        }

        [Fact]
        public async Task Rtl_DefaultAlignment_IndentsFromThePhysicalRight()
        {
            // Arabic (a real RTL script) so the line is one genuine RTL bidi run, rather than relying
            // on `direction: rtl` alone to reorder Latin text unpredictably.
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<p id='p' dir='rtl' style='margin:0;font-size:10pt;direction:rtl;text-indent:40pt'>" +
                    "مرحبا بكم في هذا الاختبار</p>"));

            var p = LayoutHarness.FindById(root, "p")!;
            Assert.Single(p.LineBoxes);

            // RTL's default `text-align: start` resolves to `right` (ApplyHorizontalAlignment), which is
            // what makes this line go through ApplyRightAlignment - the path #607 made indent-aware.
            Assert.Equal(p.ClientRight - 40, p.LineBoxes[0].Words.Max(w => w.Right), 2);
        }

        [Fact]
        public async Task Rtl_Justify_IndentsTheFirstLineFromThePhysicalRight()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<p id='p' dir='rtl' style='margin:0;font-size:10pt;width:120pt;direction:rtl;text-align:justify;text-indent:40pt'>" +
                    "مرحبا بكم في هذا الاختبار الطويل الذي يلتف عبر عدة أسطر مختلفة تماما لكي نتأكد من صحة النتيجة</p>"));

            var p = LayoutHarness.FindById(root, "p")!;
            Assert.True(p.LineBoxes.Count >= 2, "expected the paragraph to wrap onto at least two lines");

            Assert.Equal(p.ClientRight - 40, p.LineBoxes[0].Words.Max(w => w.Right), 2);
        }

        [Fact]
        public async Task Rtl_DefaultAlignment_IndentsCorrectlyEvenWhenLineSlackIsSmallerThanTheIndent()
        {
            // A width narrow enough that a line's natural slack (ClientRight - its content width) is
            // routinely smaller than the 40pt indent - exactly the case a superseded flush-target-only
            // RTL approach broke: ApplyRightAlignment's `if (!(diff > 0)) return;` guard swallowed the
            // resulting negative diff and left the line exactly where flow put it, un-aligned. Reserving
            // the indent on the wrap boundary itself (what actually shipped) keeps every width correct.
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<p id='p' dir='rtl' style='margin:0;font-size:10pt;width:150pt;direction:rtl;text-indent:40pt'>" +
                    "مرحبا بكم في هذا الاختبار الطويل الذي يلتف عبر عدة أسطر مختلفة تماما لكي نتأكد من صحة النتيجة</p>"));

            var p = LayoutHarness.FindById(root, "p")!;
            Assert.True(p.LineBoxes.Count >= 2, "expected the paragraph to wrap onto at least two lines");

            foreach (var line in p.LineBoxes)
            {
                var isFirstLine = line == p.LineBoxes[0];
                var expectedRight = isFirstLine ? p.ClientRight - 40 : p.ClientRight;
                Assert.Equal(expectedRight, line.Words.Max(w => w.Right), 2);
            }
        }

        [Fact]
        public async Task Center_ReservesTheFullIndentOnTheLineStartSide_ThenCentersTheRemainder()
        {
            // Regression for issue #623 - the line's own words already start `indent` further right than
            // the client edge (the flow-time CurrentX offset), so the leftover slack ApplyCenterAlignment
            // splits evenly (`diff`) already excludes the indent; the difference between the two final
            // gaps is exactly the indent, not half of it.
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<p id='p' style='margin:0;font-size:10pt;width:150pt;text-align:center;text-indent:40pt'>" +
                    "Short text</p>"));

            var p = LayoutHarness.FindById(root, "p")!;
            Assert.Single(p.LineBoxes);
            var line = p.LineBoxes[0];
            var leftGap = line.Words.Min(w => w.Left) - p.ClientLeft;
            var rightGap = p.ClientRight - line.Words.Max(w => w.Right);

            Assert.Equal(40, leftGap - rightGap, 2);
        }

        [Fact]
        public async Task Rtl_Center_ReservesTheFullIndentOnTheLineStartSide_ThenCentersTheRemainder()
        {
            // RTL's line-start side is the physical right (CSS Text 3 §3) - before this fix,
            // ApplyCenterAlignment didn't inset its flush target for RTL at all (unlike
            // ApplyRightAlignment/ApplyJustifyAlignment), so the indent was centered away to nothing
            // instead of landing on the physical right.
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<p id='p' dir='rtl' style='margin:0;font-size:10pt;width:150pt;direction:rtl;text-align:center;text-indent:40pt'>" +
                    "مرحبا بكم</p>"));

            var p = LayoutHarness.FindById(root, "p")!;
            Assert.Single(p.LineBoxes);
            var line = p.LineBoxes[0];
            var leftGap = line.Words.Min(w => w.Left) - p.ClientLeft;
            var rightGap = p.ClientRight - line.Words.Max(w => w.Right);

            Assert.Equal(40, rightGap - leftGap, 2);
        }

        [Fact]
        public async Task EachLine_StillIndentsAResumedLineThatFollowsAForcedBreakInTheSource()
        {
            // Every line in this paragraph follows a <br> - there is one between every pair of "LineN"
            // words - so under each-line every single line, including whichever ones happen to open a
            // resumed fragmentainer pass, should be indented. The line-in-progress when a fragmentainer
            // break is taken is discarded and rebuilt as a fresh seed line on resume; if that rebuild
            // doesn't carry FollowsForcedBreak forward, whichever "LineN" happens to open page 2 or 3
            // silently loses its indent even though it follows a <br> in the source.
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<p id='p' style='margin:0;line-height:22pt;font-size:10pt;text-indent:40pt each-line'>" +
                    string.Join("<br>", Enumerable.Range(0, 20).Select(i => $"Line{i}")) +
                    "</p>"),
                pageHeight: 200, margin: 20);

            var p = LayoutHarness.FindById(root, "p")!;
            Assert.True(container.FragmentainerPasses > 1, "expected the flow to resume across fragmentainers");

            foreach (var line in p.LineBoxes)
            {
                Assert.Equal(p.ClientLeft + 40, line.Words.Min(w => w.Left), 2);
            }
        }

        [Fact]
        public async Task EmIndent_WithHanging_ResolvesAgainstTheDeclaringElementsFontSize()
        {
            // NoEmsTextIndent isolates the length token from `2em hanging` before converting em to pt,
            // eagerly, at cascade time - against the DECLARING (20pt) element's font-size, per how
            // inheritance is supposed to resolve em units. Without that isolation (the pre-fix `NoEms`
            // alone), `CssLength`'s parser reads only the string's last 2 characters as the unit - "ng",
            // for this value - matches nothing, and silently no-ops, leaving the unresolved "2em hanging"
            // to inherit down and resolve against each descendant's OWN font-size instead.
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='font-size:20pt;text-indent:2em hanging'>" +
                    "<p id='p' style='font-size:10pt;margin:0'>Text</p>" +
                    "</div>"));

            var p = LayoutHarness.FindById(root, "p")!;

            Assert.Equal(40, p.ActualTextIndent);
            Assert.True(p.ActualTextIndentHanging);
        }

        [Fact]
        public async Task Rtl_NoWrapInlineBox_RespectsTheNarrowedWrapBoundaryToo()
        {
            // FlowBox's RTL wrap-boundary narrowing (the mechanism that reserves the indent's space
            // during flow) has its own separate check for a `white-space: nowrap` inline box that
            // doesn't fit the remainder of the current line - a `<span>`, unlike a plain word, either
            // fits whole or moves to a fresh line rather than wrapping internally. That check needs to
            // consult the same narrowed boundary, or a nowrap span could sit flush against the indent
            // zone instead of being pushed off to make room for it.
            const string html =
                "<p id='p' dir='rtl' style='margin:0;font-size:10pt;width:100pt;direction:rtl'>" +
                "test <span style='white-space:nowrap'>nowrap span</span></p>";

            var (unindentedRoot, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html));
            var unindented = LayoutHarness.FindById(unindentedRoot, "p")!;
            Assert.Single(unindented.LineBoxes);

            var (indentedRoot, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(html.Replace("direction:rtl", "direction:rtl;text-indent:40pt")));
            var indented = LayoutHarness.FindById(indentedRoot, "p")!;

            // Same content, same width - the only difference is text-indent narrowing what the nowrap
            // span's line has room for, which is what forces it onto a line of its own.
            Assert.Equal(2, indented.LineBoxes.Count);
        }
    }
}
