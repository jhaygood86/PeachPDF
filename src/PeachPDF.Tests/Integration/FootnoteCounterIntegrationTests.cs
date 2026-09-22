using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

using static PeachPDF.Tests.TestSupport.LayoutHarness;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// The footnote counter, bridged into the ordinary cascade: <c>counter-reset</c> on <c>@page</c>
    /// controls where each page's numbering starts (or whether it restarts at all),
    /// <c>counter-increment</c> on <c>@footnote</c> controls the step, and
    /// <c>content: counter(footnote)</c> on either pseudo-element resolves to the live,
    /// pagination-resolved number.
    /// </summary>
    /// <remarks>
    /// The number is not a document counter: it depends on which page a call lands on, which
    /// <c>CssCounterEngine</c> (resolving purely from DOM position) cannot know. It is resolved during
    /// the footnote convergence loop and published through an ambient context the content engine reads -
    /// the same shape <c>counter(page)</c> already uses inside a running element.
    /// </remarks>
    public class FootnoteCounterIntegrationTests
    {
        private const string TwoPages =
            "<p>One<sup id='fn1' style='float:footnote'>First</sup></p>"
            + "<div style='break-before: page;'>"
            + "<p>Two<sup id='fn2' style='float:footnote'>Second</sup></p>"
            + "</div>";

        [Fact]
        public async Task NoAuthorCounterReset_StillRestartsNumberingOnEveryPage()
        {
            // The byte-identical-default guard: everything else in this file changes numbering, and the
            // one thing that must not change is what a document that says nothing gets.
            var (_, container) = await LayoutAsync(Wrap(TwoPages));

            Assert.Equal(1, NumberOf(container, "fn1"));
            Assert.Equal(1, NumberOf(container, "fn2"));
        }

        [Fact]
        public async Task PageCounterResetNone_NumbersContinuouslyAcrossPages()
        {
            // The headline capability: `counter-reset` is one property, so declaring it at author origin
            // replaces the UA sheet's own `counter-reset: footnote` wholesale - and with nothing resetting
            // the counter, it simply keeps going.
            var (_, container) = await LayoutAsync(PageRule("counter-reset: none", TwoPages));

            Assert.Equal(1, NumberOf(container, "fn1"));
            Assert.Equal(2, NumberOf(container, "fn2"));
        }

        [Fact]
        public async Task PageResettingSomeOtherCounter_AlsoNumbersContinuously()
        {
            // Same cascade rule, reached a different way: an author counter-reset that never mentions
            // `footnote` still replaces the UA declaration that did.
            var (_, container) = await LayoutAsync(PageRule("counter-reset: chapter 1", TwoPages));

            Assert.Equal(1, NumberOf(container, "fn1"));
            Assert.Equal(2, NumberOf(container, "fn2"));
        }

        [Fact]
        public async Task PageCounterResetFootnoteTen_StartsEachPageAtEleven()
        {
            // Reset sets the counter's value; the first note on the page is that plus one step.
            var (_, container) = await LayoutAsync(PageRule("counter-reset: footnote 10", TwoPages));

            Assert.Equal(11, NumberOf(container, "fn1"));
            Assert.Equal(11, NumberOf(container, "fn2"));
        }

        [Fact]
        public async Task FootnoteCounterIncrementTwo_StepsByTwo()
        {
            var html = PageRule(
                "@footnote { counter-increment: footnote 2 }",
                "<p>One<sup id='fn1' style='float:footnote'>First</sup>"
                + "<sup id='fn2' style='float:footnote'>Second</sup></p>");

            var (_, container) = await LayoutAsync(html);

            Assert.Equal(2, NumberOf(container, "fn1"));
            Assert.Equal(4, NumberOf(container, "fn2"));
        }

        [Fact]
        public async Task FootnoteCounterIncrementNone_GivesEveryNoteOnThePageTheSameNumber()
        {
            var html = PageRule(
                "@footnote { counter-increment: none }",
                "<p>One<sup id='fn1' style='float:footnote'>First</sup>"
                + "<sup id='fn2' style='float:footnote'>Second</sup></p>");

            var (_, container) = await LayoutAsync(html);

            Assert.Equal(0, NumberOf(container, "fn1"));
            Assert.Equal(0, NumberOf(container, "fn2"));
        }

        [Fact]
        public async Task CounterResetOnAPageCarryingNoFootnotes_StillTakesEffect()
        {
            // The numbering walk visits every slot up to the last one carrying a call, not just the ones
            // that have footnotes - a reset applies to its page whether or not a note lands there.
            var html = "<!DOCTYPE html><html><head><style>"
                + "@page { counter-reset: none }"
                + "@page renumber { counter-reset: footnote 100 }"
                + "</style></head><body style='margin:0'>"
                + "<p>One<sup id='fn1' style='float:footnote'>First</sup></p>"
                + "<div style='break-before: page; page: renumber;'><p>A page with no footnote of its own.</p></div>"
                + "<div style='break-before: page; page: renumber;'><p>Three<sup id='fn2' style='float:footnote'>Second</sup></p></div>"
                + "</body></html>";

            var (_, container) = await LayoutAsync(html);

            Assert.Equal(1, NumberOf(container, "fn1"));
            // The named page resets the counter to 100 on page 2, which carries no note of its own; page
            // 3 (same named page) then steps from there. A walk that only visited slots with footnotes
            // would never have seen page 2's reset at all.
            Assert.Equal(101, NumberOf(container, "fn2"));
        }

        [Fact]
        public async Task ContentCounterFootnote_ResolvesToTheLiveNumberOnCallAndMarker()
        {
            var html = "<!DOCTYPE html><html><head><style>"
                + "@page { counter-reset: none }"
                + "::footnote-call { content: counter(footnote) }"
                + "::footnote-marker { content: counter(footnote) ') ' }"
                + "</style></head><body style='margin:0'>" + TwoPages + "</body></html>";

            var (_, container) = await LayoutAsync(html);

            var second = CallOf(container, "fn2");
            Assert.Equal("2", second.Text);
            Assert.Equal("2) ", MarkerOf(second).Text);
        }

        [Fact]
        public async Task ContentCounterFootnote_HonoursTheCounterStyleArgument()
        {
            var html = "<!DOCTYPE html><html><head><style>"
                + "@page { counter-reset: none }"
                + "::footnote-call { content: counter(footnote, lower-roman) }"
                + "</style></head><body style='margin:0'>" + TwoPages + "</body></html>";

            var (_, container) = await LayoutAsync(html);

            Assert.Equal("i", CallOf(container, "fn1").Text);
            Assert.Equal("ii", CallOf(container, "fn2").Text);
        }

        [Fact]
        public async Task ContentCountersFootnote_ResolvesToTheLiveNumberToo()
        {
            // A footnote counter has exactly one scope - it is reset by @page, not by a DOM ancestor - so
            // counters(footnote, ...) is just counter(footnote), separator and all.
            var html = "<!DOCTYPE html><html><head><style>"
                + "@page { counter-reset: none }"
                + "::footnote-call { content: counters(footnote, '.') }"
                + "</style></head><body style='margin:0'>" + TwoPages + "</body></html>";

            var (_, container) = await LayoutAsync(html);

            Assert.Equal("2", CallOf(container, "fn2").Text);
        }

        [Fact]
        public async Task ContentLiteralOverride_IsStillNotRenumbered()
        {
            // An author override that cannot name the counter keeps overriding, exactly as before.
            var html = "<!DOCTYPE html><html><head><style>"
                + "::footnote-call { content: '*' }"
                + "</style></head><body style='margin:0'>" + TwoPages + "</body></html>";

            var (_, container) = await LayoutAsync(html);

            Assert.Equal("*", CallOf(container, "fn1").Text);
            Assert.Equal("*", CallOf(container, "fn2").Text);
        }

        [Fact]
        public async Task AWiderNumber_ActuallyMovesTheContentFollowingTheCallOnItsLine()
        {
            // The assertion that proves the number genuinely re-enters layout rather than merely being
            // stored: a two-digit call is wider than a one-digit one, so the text after it on the same
            // line starts further right. A text-only assertion would pass even if that feedback were
            // broken, which is exactly how the convergence loop could otherwise exit with stale numbers.
            const string body =
                "<p>One<sup id='fn1' style='float:footnote'>First</sup> tail</p>"
                + "<div style='break-before: page;'><p>Two<sup id='fn2' style='float:footnote'>Second</sup>"
                + " tail</p></div>";

            var wide = await LayoutAsync(PageRule("counter-reset: footnote 9", body));
            var narrow = await LayoutAsync(PageRule("counter-reset: footnote 0", body));

            Assert.Equal(10, NumberOf(wide.Container, "fn2"));
            Assert.Equal(1, NumberOf(narrow.Container, "fn2"));

            var wideTail = TailWordLeft(wide.Container, "fn2");
            var narrowTail = TailWordLeft(narrow.Container, "fn2");

            Assert.True(wideTail > narrowTail,
                $"a two-digit call should push the following word right: {wideTail} vs {narrowTail}");
        }

        [Fact]
        public async Task NumberingIsStableAcrossRepeatedLayout()
        {
            var numbers = await LayoutRepeatedlyAsync(
                PageRule("counter-reset: none", TwoPages),
                passes: 3,
                (_, container) => NumberOf(container, "fn2"));

            Assert.All(numbers, n => Assert.Equal(2, n));
        }

        private static string PageRule(string declarations, string body) =>
            "<!DOCTYPE html><html><head><style>@page { " + declarations + " }</style></head>"
            + "<body style='margin:0'>" + body + "</body></html>";

        private static CssBoxFootnoteCall CallOf(HtmlContainerInt container, string id) =>
            container.FootnoteCalls.First(c => c.Body.HtmlTag?.TryGetAttribute("id") == id);

        private static int NumberOf(HtmlContainerInt container, string id) => CallOf(container, id).Number;

        private static CssBoxFootnoteMarker MarkerOf(CssBoxFootnoteCall call) =>
            (CssBoxFootnoteMarker)call.Body.Boxes[0];

        /// <summary>
        /// The left edge of the first word laid out after <paramref name="id"/>'s call on its own line -
        /// the geometry a wider call number actually moves.
        /// </summary>
        private static double TailWordLeft(HtmlContainerInt container, string id)
        {
            var call = CallOf(container, id);
            var paragraph = call.ParentBox!;

            var tail = Descendants(paragraph)
                .SelectMany(b => b.Words)
                .Where(w => w.Text is not null && w.Text.Contains("tail"))
                .ToList();

            Assert.NotEmpty(tail);
            return tail[0].Left;
        }
    }
}
