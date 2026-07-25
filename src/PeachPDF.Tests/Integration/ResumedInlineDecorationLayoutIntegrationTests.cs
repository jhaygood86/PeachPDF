using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// An inline box whose lines cross a page boundary, under both values of
    /// <c>box-decoration-break</c> (<see href="https://www.w3.org/TR/css-break-3/#break-decoration">css-break-3
    /// §6.2</see>).
    /// <para>
    /// Layout fills one fragmentainer per pass, so a resumed pass walks the block's inline content from
    /// the top again and fast-forwards past the words it already placed. It must not re-open the boxes it
    /// passes through on the way: under <c>slice</c> a continuation carries no leading border, padding or
    /// margin at all, exactly as it does at a line wrap — which is what
    /// <see cref="BoxDecorationBreakLayoutIntegrationTests"/> pins for the wrap axis.
    /// </para>
    /// </summary>
    public class ResumedInlineDecorationLayoutIntegrationTests
    {
        private const double PageHeight = 200;
        private const double Margin = 20;

        /// <summary>2pt border + 5pt padding + 5pt margin on the leading edge of the fixture's span.</summary>
        private const double LeadingSpacing = 12;

        /// <summary>2pt border + 4pt padding on its trailing edge. Deliberately different from the leading
        /// one, so an assertion cannot pass by charging the wrong end.</summary>
        private const double TrailingSpacing = 6;

        // ── slice ────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Slice_InlineCrossingAPageBreak_ResumesAtTheContentEdge()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(DecoratedSpan(clone: false),
                pageHeight: PageHeight, margin: Margin);

            AssertPaginated(container);

            var p = LayoutHarness.FindById(root, "p")!;
            var span = LayoutHarness.FindById(root, "s")!;

            // §6.2 slice: the box's leading spacing exists once, at its true start. Every later line —
            // whether it follows a wrap or a page break — begins at the block's own content edge.
            foreach (var (top, left) in LineStarts(span).Skip(1))
            {
                Assert.Equal(p.ClientLeft, left, 2);
                Assert.True(top > 0);
            }

            // ...and the resumed page really is one of them.
            Assert.Contains(LineStarts(span).Skip(1), line => PageOf(container, line.Top) > 0);
        }

        [Fact]
        public async Task Slice_InlineCrossingAPageBreak_PadsOnlyItsTrueFirstLine()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(DecoratedSpan(clone: false),
                pageHeight: PageHeight, margin: Margin);

            AssertPaginated(container);

            var span = LayoutHarness.FindById(root, "s")!;
            var firstTop = LineStarts(span).First().Top;

            // The rectangle is pulled back over the box's leading border and padding on the line the box
            // starts on, and nowhere else. Asserted separately from the word positions above because the
            // two used to be wrong in opposite directions on a resumed page, which made the rectangle's
            // *value* look right while it was resolved against the wrong line.
            foreach (var (line, rect) in span.Rectangles)
            {
                var words = WordsOn(span, line).ToList();
                if (words.Count == 0) continue;

                var textLeft = words.Min(w => w.Left);
                var expected = rect.Y <= firstTop + 0.01 ? textLeft - (LeadingSpacing - MarginLeftOf(span)) : textLeft;

                Assert.Equal(expected, rect.Left, 2);
            }
        }

        [Fact]
        public async Task Slice_InlineCrossingAPageBreak_CountsItsSpacingOnceAcrossAllFragments()
        {
            // FragmentTreeBuilder builds the unbroken box a sliced decoration resolves against by summing
            // its rectangles' widths, which is only right if each border and padding is counted once. A
            // resumed pass that re-opened the box charged the leading set twice, stretching the strip.
            var (decoratedRoot, decoratedContainer) = await LayoutHarness.LayoutAsync(DecoratedSpan(clone: false),
                pageHeight: PageHeight, margin: Margin);
            var (plainRoot, _) = await LayoutHarness.LayoutAsync(PlainSpan(), pageHeight: PageHeight, margin: Margin);

            AssertPaginated(decoratedContainer);

            var decorated = LayoutHarness.FindById(decoratedRoot, "s")!;
            var plain = LayoutHarness.FindById(plainRoot, "s")!;

            // The two fixtures hold the same <br>-separated lines, so their text measures identically and
            // the whole difference is the decoration - one leading set and one trailing set, total.
            var difference = decorated.Rectangles.Values.Sum(r => r.Width) - plain.Rectangles.Values.Sum(r => r.Width);

            Assert.Equal(LeadingSpacing - MarginLeftOf(decorated) + TrailingSpacing, difference, 2);
        }

        [Fact]
        public async Task Slice_FinishedInline_LeavesNothingBehindOnTheNextPage()
        {
            // The span ends on the first page. A resumed pass still walks through it to count ordinals,
            // and used to charge its margin, border and padding again on the way past.
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<p id='p' style='margin:0;line-height:22pt;font-size:10pt'>" +
                    "<span id='a' style='padding:0 20pt;border:1pt solid #000'>Alpha</span><br>" +
                    string.Join("<br>", Enumerable.Range(1, 19).Select(i => $"Line{i}")) +
                    "</p>"),
                pageHeight: PageHeight, margin: Margin);

            AssertPaginated(container);

            var p = LayoutHarness.FindById(root, "p")!;
            var finished = LayoutHarness.FindById(root, "a")!;

            var laterPageWords = AllWords(p)
                .Where(w => !w.IsLineBreak && PageOf(container, w.Top) > 0)
                .ToList();

            Assert.NotEmpty(laterPageWords);
            Assert.All(laterPageWords, w => Assert.Equal(p.ClientLeft, w.Left, 2));

            // And it must not have minted a decoration rectangle there either: the "handle width setting"
            // branch registers one straight onto the line being built, which for a box the pass only
            // walked through would put a stray painted box on a page its content never reached.
            Assert.All(finished.Rectangles.Values,
                r => Assert.Equal(0, PageOf(container, r.Y)));
        }

        [Fact]
        public async Task Slice_NestedInlinesCrossingAPageBreak_PadNeitherBox()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(NestedSpans(innerClones: false, outerClones: false),
                pageHeight: PageHeight, margin: Margin);

            AssertPaginated(container);

            var p = LayoutHarness.FindById(root, "p")!;
            var outer = LayoutHarness.FindById(root, "outer")!;
            var inner = LayoutHarness.FindById(root, "inner")!;

            // Neither level re-opens, so the drift cannot accumulate per nesting level.
            var resumed = LineStarts(inner).First(line => PageOf(container, line.Top) > 0);
            Assert.Equal(p.ClientLeft, resumed.Left, 2);

            foreach (var box in new[] { outer, inner })
            {
                var laterRects = box.Rectangles.Where(kv => PageOf(container, kv.Value.Y) > 0).ToList();
                Assert.NotEmpty(laterRects);
                Assert.All(laterRects, kv => Assert.Equal(p.ClientLeft, kv.Value.Left, 2));
            }
        }

        [Fact]
        public async Task Slice_BreakFallingAtAnInlinesFirstWord_PadsItOnce()
        {
            // BubbleRectangles compensates for a box whose leading spacing landed on a line its content
            // then wrapped off - there the spacing was never applied on the line the box really starts
            // on, and UpdateRectangle alone would leave no room for it. It used to fire on any line but
            // the block's first, which on a resumed pass includes the one the box genuinely opens on:
            // there the spacing *was* applied to the words, so taking it off twice puts the rectangle a
            // whole leading set left of its own text.
            //
            // Reaching that needs the resume point to be an inline's own first word, with the wrap that
            // put it there coming from overflow rather than a <br> (a <br> word sits on the line it
            // opens, so it, not the following box's word, is that line's first).
            var (root, container) = await LayoutHarness.LayoutAsync(WrappingSpans(),
                pageHeight: PageHeight, margin: Margin);

            AssertPaginated(container);

            var p = LayoutHarness.FindById(root, "p")!;

            // Each span opens on the line holding its own first word, which is the lowest of its
            // rectangles - a span that also has a continuation line correctly carries no leading spacing
            // there, so only the opening one says anything about this.
            var opening = LayoutHarness.Descendants(p)
                .Where(box => box.HtmlTag?.TryGetAttribute("class") == "s" && box.Rectangles.Count > 0)
                .Select(span => (Span: span, Entry: span.Rectangles.OrderBy(kv => kv.Value.Y).First()))
                .Where(entry => PageOf(container, entry.Entry.Value.Y) > 0)
                .ToList();

            Assert.NotEmpty(opening);

            foreach (var (span, entry) in opening)
            {
                var words = WordsOn(span, entry.Key).ToList();
                Assert.NotEmpty(words);

                // Exactly one leading set left of its own text - never two.
                Assert.Equal(words.Min(w => w.Left) - 20, entry.Value.Left, 2);
            }
        }

        [Fact]
        public async Task Slice_FinishedInlineBlock_IsNotInsetAgainByEveryLaterPass()
        {
            // An inline-block's vertical border and padding are applied by shifting every word it has
            // flowed. A resumed pass that walked back through one placed pages ago would shift those
            // words down again, once more for each fragmentainer after the one they landed in - so the
            // damage grows with the document rather than being a fixed offset.
            // Enough pages that some line is bound to straddle a boundary whatever the platform's font
            // metrics make a line's own height: the band is not a whole number of line heights, so each
            // successive boundary falls at a different phase within a line.
            var (paginated, container) = await LayoutHarness.LayoutAsync(InlineBlockThenLines(60),
                pageHeight: PageHeight, margin: Margin);
            var (single, singleContainer) = await LayoutHarness.LayoutAsync(InlineBlockThenLines(3),
                pageHeight: PageHeight, margin: Margin);

            AssertPaginated(container);
            Assert.Equal(1, singleContainer.FragmentainerPasses);

            // The inline-block sits on the block's first line either way, so the number of lines after
            // it - and therefore the number of passes - must make no difference to where it lands.
            var paginatedTop = AllWords(LayoutHarness.FindById(paginated, "ib")!).Min(w => w.Top);
            var singleTop = AllWords(LayoutHarness.FindById(single, "ib")!).Min(w => w.Top);

            Assert.Equal(singleTop, paginatedTop, 2);
        }

        [Fact]
        public async Task Slice_FinishedGeneratedInlineBlock_IsNotInsetAgainByEveryLaterPass()
        {
            // The same defect on the other of the two paths that apply the inset: a box holding its words
            // directly - a ::before, whose generated text lives on the box itself rather than in an
            // anonymous child - never goes through the FlowBox recursion, so it is inset by its parent's
            // own loop instead.
            var (paginated, container) = await LayoutHarness.LayoutAsync(GeneratedInlineBlockThenLines(60),
                pageHeight: PageHeight, margin: Margin);
            var (single, singleContainer) = await LayoutHarness.LayoutAsync(GeneratedInlineBlockThenLines(3),
                pageHeight: PageHeight, margin: Margin);

            AssertPaginated(container);
            Assert.Equal(1, singleContainer.FragmentainerPasses);

            var paginatedTop = GeneratedWordTop(paginated);
            var singleTop = GeneratedWordTop(single);

            Assert.Equal(singleTop, paginatedTop, 2);
        }

        [Fact]
        public async Task Slice_InlineCrossingAPageBreakBesideAFloat_StillResumesAtTheContentEdge()
        {
            // The float branches re-establish the line start from scratch rather than adjusting it, so
            // they have to re-apply exactly the leading spacing the pass applied - not the box's raw
            // spacing, which is what `slice` suppresses for a box the pass is only walking through.
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='d' style='margin:0'>" +
                    "<div style='float:left;width:60pt;height:900pt;background:#ccc'></div>" +
                    "<p id='p' style='margin:0;line-height:22pt;font-size:10pt'>" +
                    "<span id='s' style='padding-left:20pt;background:#0f0'>" +
                    string.Join("<br>", Enumerable.Range(0, 30).Select(i => $"Line{i}")) +
                    "</span></p></div>"),
                pageHeight: PageHeight, margin: Margin);

            AssertPaginated(container);

            var span = LayoutHarness.FindById(root, "s")!;
            var starts = LineStarts(span);

            // Every line after the first clears the float and starts there, with no leading pad - the
            // float's own right edge, which is the same on the resumed page as on the first.
            var continuation = starts.Skip(1).Select(line => line.Left).Distinct().ToList();
            Assert.Single(continuation);
            Assert.Equal(starts[0].Left - 20, continuation[0], 2);

            // ...and the resumption really happened partway through.
            Assert.Contains(starts.Skip(1), line => PageOf(container, line.Top) > 0);
        }

        [Fact]
        public async Task Slice_FinishedInlineFlex_IsNotRepositionedOntoTheResumedPage()
        {
            // An inline-flex contributes no word ordinals, so nothing else in the walk would notice it
            // belongs to an earlier fragmentainer. Its branch positions the box, re-runs the flex engine
            // and sets the cursor to the box's own right edge.
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<p id='p' style='margin:0;line-height:22pt;font-size:10pt'>" +
                    "<span id='f' style='display:inline-flex;width:40pt'>Flex</span><br>" +
                    string.Join("<br>", Enumerable.Range(1, 59).Select(i => $"Line{i}")) +
                    "</p>"),
                pageHeight: PageHeight, margin: Margin);

            AssertPaginated(container);

            var p = LayoutHarness.FindById(root, "p")!;
            var flex = LayoutHarness.FindById(root, "f")!;

            // It sits on the block's first line and must stay there...
            Assert.Equal(0, PageOf(container, flex.Location.Y));

            // ...and must not push the pages it was never on to the right by its own width.
            var laterPageWords = AllWords(p)
                .Where(w => !w.IsLineBreak && PageOf(container, w.Top) > 0)
                .ToList();

            Assert.NotEmpty(laterPageWords);
            Assert.All(laterPageWords, w => Assert.Equal(p.ClientLeft, w.Left, 2));
        }

        // ── clone (the control) ──────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Clone_InlineCrossingAPageBreak_ReopensWithItsOwnLeadingSpacing()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(DecoratedSpan(clone: true),
                pageHeight: PageHeight, margin: Margin);

            AssertPaginated(container);

            var p = LayoutHarness.FindById(root, "p")!;
            var span = LayoutHarness.FindById(root, "s")!;

            // Under clone every fragment is wrapped on its own, so a page break re-opens the box exactly
            // as a line wrap does - the case the slice tests above are the control for.
            foreach (var (top, left) in LineStarts(span))
            {
                Assert.Equal(p.ClientLeft + LeadingSpacing, left, 2);
                Assert.True(top > 0);
            }

            Assert.All(span.Rectangles.Values,
                r => Assert.Equal(p.ClientLeft + MarginLeftOf(span), r.Left, 2));
        }

        [Fact]
        public async Task Clone_NestedInlinesCrossingAPageBreak_ReserveBothTheirSpacings()
        {
            // The reservation is summed over every cloning box the break fell inside, so a page break
            // inside the inner span has to re-open both. Only the outer one clones in the control, which
            // is what makes the difference attributable to the inner one.
            var (bothRoot, bothContainer) = await LayoutHarness.LayoutAsync(NestedSpans(innerClones: true, outerClones: true),
                pageHeight: PageHeight, margin: Margin);
            var (outerRoot, outerContainer) = await LayoutHarness.LayoutAsync(NestedSpans(innerClones: false, outerClones: true),
                pageHeight: PageHeight, margin: Margin);

            AssertPaginated(bothContainer);
            AssertPaginated(outerContainer);

            var both = ResumedTextLeft(LayoutHarness.FindById(bothRoot, "inner")!, bothContainer);
            var outerOnly = ResumedTextLeft(LayoutHarness.FindById(outerRoot, "inner")!, outerContainer);

            Assert.Equal(InnerSpacing, both - outerOnly, 2);
        }

        // ── fixtures and helpers ─────────────────────────────────────────────────────────────────────

        private const double InnerSpacing = 7;  // the nested fixture's inner span: 2pt border + 5pt padding

        /// <summary>
        /// Twenty short <c>&lt;br&gt;</c>-separated lines inside one decorated span. At 22pt each they
        /// overflow the fixture's 160pt band several times over, and none lands flush on a boundary, so
        /// the flow genuinely has to stop and resume.
        /// </summary>
        private static string DecoratedSpan(bool clone) => LayoutHarness.Wrap(
            "<p id='p' style='margin:0;line-height:22pt;font-size:10pt'>" +
            "<span id='s' style='border-left:2pt solid #000;padding-left:5pt;margin-left:5pt;" +
            "border-right:2pt solid #000;padding-right:4pt;background:#0f0" +
            (clone ? ";box-decoration-break:clone" : "") + "'>" +
            string.Join("<br>", Enumerable.Range(0, 20).Select(i => $"Line{i}")) +
            "</span></p>");

        /// <summary>
        /// A vertically-padded inline-block on the block's first line, followed by enough lines to
        /// control how many fragmentainers the block spans.
        /// </summary>
        private static string InlineBlockThenLines(int lines) => LayoutHarness.Wrap(
            "<p id='p' style='margin:0;line-height:22pt;font-size:10pt'>" +
            "<span id='ib' style='display:inline-block;padding:9pt 0;border-top:3pt solid #000'>Alpha</span><br>" +
            string.Join("<br>", Enumerable.Range(1, lines).Select(i => $"Line{i}")) +
            "</p>");

        /// <summary>
        /// The same shape as <see cref="InlineBlockThenLines"/>, but the vertically-padded inline-block is
        /// a <c>::before</c>, so its text lives on the box itself.
        /// </summary>
        private static string GeneratedInlineBlockThenLines(int lines) => LayoutHarness.Wrap(
            "<style>#p::before { content:'Alpha'; display:inline-block; padding:9pt 0; " +
            "border-top:3pt solid #000 }</style>" +
            "<p id='p' style='margin:0;line-height:22pt;font-size:10pt'>" +
            string.Join("<br>", Enumerable.Range(1, lines).Select(i => $"Line{i}")) +
            "</p>");

        /// <summary>Where the generated <c>::before</c> text sits.</summary>
        private static double GeneratedWordTop(CssBox root) =>
            LayoutHarness.Descendants(LayoutHarness.FindById(root, "p")!)
                .SelectMany(b => b.Words)
                .Where(w => w.Text == "Alpha")
                .Min(w => w.Top);

        /// <summary>The same content with no decoration at all, so the difference between the two is the
        /// decoration and nothing else.</summary>
        private static string PlainSpan() => LayoutHarness.Wrap(
            "<p id='p' style='margin:0;line-height:22pt;font-size:10pt'>" +
            "<span id='s' style='background:#0f0'>" +
            string.Join("<br>", Enumerable.Range(0, 20).Select(i => $"Line{i}")) +
            "</span></p>");

        /// <summary>
        /// Spans too wide to share a line, so each one's first word starts a line by overflowing rather
        /// than after a <c>&lt;br&gt;</c>. Each holds a text run plus an element child, so its words live
        /// in an anonymous first child box — which is the box <c>BubbleRectangles</c>' compensation is
        /// keyed on (a span whose only content is one text run holds its words itself).
        /// </summary>
        private static string WrappingSpans() => LayoutHarness.Wrap(
            "<p id='p' style='margin:0;width:70pt;line-height:22pt;font:10pt Arial'>" +
            string.Join(" ", Enumerable.Range(0, 20).Select(i =>
                $"<span class='s' style='padding-left:20pt'>Wordzero{i}<b>.</b></span>")) +
            "</p>");

        private static string NestedSpans(bool innerClones, bool outerClones) => LayoutHarness.Wrap(
            "<p id='p' style='margin:0;line-height:22pt;font-size:10pt'>" +
            "<span id='outer' style='border-left:3pt solid #000;padding-left:6pt" +
            (outerClones ? ";box-decoration-break:clone" : "") + "'>" +
            "<span id='inner' style='border-left:2pt solid #000;padding-left:5pt" +
            (innerClones ? ";box-decoration-break:clone" : "") + "'>" +
            string.Join("<br>", Enumerable.Range(0, 20).Select(i => $"Line{i}")) +
            "</span></span></p>");

        /// <summary>
        /// The fixture must actually resume, or every assertion below is vacuous.
        /// </summary>
        private static void AssertPaginated(HtmlContainerInt container) =>
            Assert.True(container.FragmentainerPasses > 1,
                $"fixture must paginate, but layout took {container.FragmentainerPasses} pass(es)");

        private static int PageOf(HtmlContainerInt container, double y) =>
            container.PageIndexOf(y + HtmlContainerInt.PageBoundaryEpsilon);

        private static IEnumerable<CssRect> AllWords(CssBox box)
        {
            foreach (var word in box.Words) yield return word;

            foreach (var child in box.Boxes)
            {
                foreach (var word in AllWords(child)) yield return word;
            }
        }

        private static IEnumerable<CssRect> WordsOn(CssBox box, CssLineBox line) =>
            AllWords(box).Where(w => !w.IsLineBreak && line.Words.Contains(w));

        /// <summary>Where the text on each of a box's lines begins, in document order.</summary>
        private static List<(double Top, double Left)> LineStarts(CssBox box) =>
        [
            .. AllWords(box)
                .Where(w => !w.IsLineBreak)
                .GroupBy(w => w.Top)
                .OrderBy(group => group.Key)
                .Select(group => (Top: group.Key, Left: group.Min(w => w.Left)))
        ];

        private static double ResumedTextLeft(CssBox box, HtmlContainerInt container) =>
            LineStarts(box).First(line => PageOf(container, line.Top) > 0).Left;

        /// <summary>
        /// A box's leading margin is part of the room the flow leaves for it, but not of the rectangle
        /// its background and border paint over — <see cref="CssLineBox.UpdateRectangle"/> pulls the
        /// rectangle back over the border and padding only.
        /// </summary>
        private static double MarginLeftOf(CssBox box) => box.ActualMarginLeft;
    }
}
