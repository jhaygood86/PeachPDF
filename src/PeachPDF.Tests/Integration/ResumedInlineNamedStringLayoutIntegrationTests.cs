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
