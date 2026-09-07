using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// The real-world shape behind the css4.pub Icelandic dictionary running-header bug: a <c>&lt;b&gt;</c>
    /// carrying <c>string-set</c> opens on one page, but its containing paragraph is long enough that a
    /// later page break falls inside the *same* paragraph. Layout fills one fragmentainer per pass (see
    /// <see cref="ResumedInlineDecorationLayoutIntegrationTests"/>), so the pass that resumes the
    /// paragraph on the next page walks its inline content from the top again - reaching the
    /// already-placed <c>&lt;b&gt;</c> a second time even though it did not open on this pass. Without
    /// gating <c>CssLayoutEngine.FlowBox</c>'s string-set application on <c>opensHere</c>, that second
    /// visit re-stamps the box's <c>NamedString</c> using *this* pass's cursor - which sits at the
    /// resumed page's own top, since the walk has not yet advanced past the already-placed word -
    /// discarding the box's true first-page position. This same <c>opensHere</c> gate originally also
    /// covered an inline target's <c>page</c> (named-page) registration - removed entirely since (issue
    /// #149), as an inline-level box never creates the class-A break points css-page-3 §7.2 scopes
    /// <c>page</c> to.
    /// </summary>
    public class ResumedInlineNamedStringLayoutIntegrationTests
    {
        private const double PageHeight = 100;

        [Fact]
        public async Task NamedString_InlineTarget_KeepsTruePositionAcrossResumedContinuation()
        {
            var html = Wrap($"<p id='p'><b id='term'>first</b> {Filler}</p>", stringSet: true);
            var (root, container) = await LayoutHarness.LayoutAsync(html, pageWidth: 200, pageHeight: PageHeight);

            AssertPaginated(container);

            var term = LayoutHarness.FindById(root, "term")!;
            var trueY = AllWords(term).Single().Top;

            // The regression guard: without the opensHere gate, the resumed pass overwrites this with the
            // resumed page's own top Y, and/or leaves a second orphaned entry behind.
            var namedString = Assert.Single(container.NamedStrings, ns => ns.Name == "entry" && ns.Value == "first");
            Assert.Equal(0, container.PageIndexOf(namedString.Y));
            Assert.Equal(trueY, namedString.Y, 1);
        }

        [Fact]
        public async Task NamedString_InlineTargetStraddlingAPageBreak_KeepsItsOwnOpeningPosition()
        {
            // Unlike the fixture above, "term" itself is long enough to straddle the break its own
            // content falls across - CssLayoutEngine.FlowBox's recursive call for "term" hits the break
            // and returns before reaching its own FinalizeFlowBoxExit, on every pass that touches it, so
            // nothing after ApplyStringSet's own (otherwise wrong, box.Location.Y-seeded) registration
            // ever corrects it unless the fix records the box's real opening position up front (#341).
            var html = LayoutHarness.Wrap(
                "<p id='p' style='margin:0;line-height:22pt;font-size:10pt'>" +
                "<span id='term' style='string-set: entry content(text)'>" +
                string.Join("<br>", Enumerable.Range(0, 20).Select(i => $"Line{i}")) +
                "</span></p>");

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageHeight: 200, margin: 20);

            Assert.True(container.FragmentainerPasses > 1,
                $"fixture must paginate, but layout took {container.FragmentainerPasses} pass(es)");

            var term = LayoutHarness.FindById(root, "term")!;
            var words = AllWords(term).ToList();
            var openingY = words.Min(w => w.Top);
            var closingY = words.Max(w => w.Top);

            // The fixture must actually straddle a break, or the assertion below is vacuous.
            Assert.NotEqual(container.PageIndexOf(openingY), container.PageIndexOf(closingY));

            var namedString = Assert.Single(container.NamedStrings, ns => ns.Name == "entry");
            Assert.Equal(container.PageIndexOf(openingY), container.PageIndexOf(namedString.Y));
            Assert.Equal(openingY, namedString.Y, 1);
        }

        [Fact]
        public async Task NamedString_InlineTargetWhoseOwnFirstWordOverflowsOntoALine_KeepsThatLinesPageAttribution()
        {
            // No pagination needed to reach this: "term" opens PrepareFlowBoxEntry with the seed line's
            // own Y (stamped before any of its content is known), but its own first word then overflows
            // onto a second line - the same wrap FlowBox's own FirstHostingLineBox correction handles
            // (#342) needs the mirror correction for a string-set box's NamedStrings.Y too, or it is left
            // naming the line "term" was entered on rather than one its content actually starts on. Only
            // the page attribution is pinned here (not the exact line): FinalizeFlowBoxExit's own,
            // separate exit-side update (unaffected by this fix) still re-stamps Y from wherever "term"'s
            // flow ends up by the time it fully closes, which is a different, pre-existing imprecision
            // that happens not to matter while every line involved shares one page.
            var html = LayoutHarness.Wrap(
                "<p id='p' style='margin:0;width:25pt;font:10pt Arial'>" +
                "<span id='term' style='string-set: entry content(text)'>Wordzero VeryLongWordThatWraps</span></p>");

            var (root, container) = await LayoutHarness.LayoutAsync(html);

            var term = LayoutHarness.FindById(root, "term")!;
            var words = AllWords(term).ToList();

            // The fixture must actually wrap the span's own content onto more than one line, or the
            // assertion below is vacuous.
            Assert.True(words.Select(w => w.Top).Distinct().Count() > 1,
                "fixture must wrap the span's own content onto more than one line");

            var namedString = Assert.Single(container.NamedStrings, ns => ns.Name == "entry");
            Assert.Equal(0, container.PageIndexOf(namedString.Y));
        }

        // 200 short words, comfortably enough to push this paragraph across a page break at PageHeight.
        private static readonly string Filler = string.Join(" ", Enumerable.Repeat("word", 200));

        private static string Wrap(string body, bool stringSet)
        {
            // `page` is no longer registered for an inline target at all (issue #149 - inline-level
            // boxes never create the class-A break points css-page-3 §7.2 scopes `page` to), so this
            // helper's only remaining caller always passes stringSet: true; kept as a parameter rather
            // than inlined in case a future inline-target regression needs the same fixture shape again.
            var declaration = stringSet ? "string-set: entry content(text)" : "page: chapter";
            return $"<!DOCTYPE html><html><head><style>#term {{ {declaration} }}</style></head><body>{body}</body></html>";
        }

        private static void AssertPaginated(HtmlContainerInt container) =>
            Assert.True(container.FragmentainerPasses > 1,
                $"fixture must paginate, but layout took {container.FragmentainerPasses} pass(es)");

        private static System.Collections.Generic.IEnumerable<CssRect> AllWords(CssBox box)
        {
            foreach (var word in box.Words) yield return word;

            foreach (var child in box.Boxes)
            {
                foreach (var word in AllWords(child)) yield return word;
            }
        }
    }
}
