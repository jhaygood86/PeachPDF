using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Coverage for the survival of <c>break-before</c>/<c>break-after</c>/<c>break-inside</c>
    /// (<see href="https://www.w3.org/TR/css-break-3/#break-between">css-break-3 §3.1/§3.2</see>) across a
    /// <b>structural clone</b> of a box — <see cref="CssBoxProperties.InheritStyle"/>'s
    /// <c>everything: true</c> branch.
    /// <para>
    /// The distinction that branch encodes: the break properties are not inherited, so an ordinary child
    /// must not pick them up from its parent; but a structural duplicate is a <i>fragment of the same
    /// element</i>, and §3 attaches these values to the element rather than to one of its boxes. Both
    /// directions are asserted here, since only asserting the first would pass with the property left out
    /// of the branch entirely.
    /// </para>
    /// <para>
    /// These tests assert storage. What the stored value then does to pagination is
    /// <see cref="StructuralCloneBreakValueBehaviourTests"/> and the existing page-break suites.
    /// </para>
    /// </summary>
    public class BreakValueCascadeTests
    {
        // ── the two structural-clone call sites ────────────────────────────────

        // CssProxyBox's repeated table header: each proxy is a structural duplicate of the same <thead>, so
        // it must carry that element's own resolved break values.
        // The expected break-inside is "avoid" wherever the author declares nothing, because the UA print
        // stylesheet gives thead/tfoot one — css-tables-3 6.2's condition on repeating the group at all.
        // The last case is what keeps this a test of *carrying* rather than of that default: an author
        // override reaches every proxy too.
        [Theory]
        [InlineData("break-inside: avoid", "avoid", "auto", "auto")]
        [InlineData("break-inside: auto", "auto", "auto", "auto")]
        [InlineData("break-after: avoid", "avoid", "auto", "avoid")]
        [InlineData("break-before: page", "avoid", "page", "auto")]
        public async Task RepeatedTableHeaderProxy_CarriesTheSourceBreakValues(
            string css, string expectedInside, string expectedBefore, string expectedAfter)
        {
            var rows = string.Join("", Enumerable.Range(1, 20)
                .Select(i => $"<tr><td>Row {i}, Cell 1</td><td>Row {i}, Cell 2</td></tr>"));

            var html = $$"""
                <!DOCTYPE html><html><head><style>
                  table { width: 100%; border-collapse: collapse; }
                  thead { display: table-header-group; {{css}}; }
                  th, td { border: 1px solid black; padding: 8px; }
                </style></head><body>
                <table>
                  <thead><tr><th>Header 1</th><th>Header 2</th></tr></thead>
                  <tbody>{{rows}}</tbody>
                </table>
                </body></html>
                """;

            var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 300);
            var table = LayoutHarness.Descendants(root).FirstOrDefault(b => b.HtmlTag?.Name == "table");
            Assert.NotNull(table);

            var proxies = table!.Boxes.OfType<CssProxyBox>().ToList();
            Assert.NotEmpty(proxies);

            Assert.All(proxies, proxy =>
            {
                Assert.Equal(expectedInside, proxy.BreakInside);
                Assert.Equal(expectedBefore, proxy.BreakBefore);
                Assert.Equal(expectedAfter, proxy.BreakAfter);
            });
        }

        // DomParser's block-in-inline correction splits one element's box into several. Every resulting box
        // represents the same <span>, so each has to carry the span's own resolved values.
        [Theory]
        [InlineData("break-inside: avoid", "avoid", "auto", "auto")]
        [InlineData("break-after: avoid", "auto", "auto", "avoid")]
        [InlineData("break-before: page", "auto", "page", "auto")]
        public async Task BlockInsideInlineSplit_EveryFragmentCarriesTheBreakValues(
            string css, string expectedInside, string expectedBefore, string expectedAfter)
        {
            var html = LayoutHarness.Wrap(
                $"""<span style="{css}">before<div>block</div>after</span>""");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var spanBoxes = LayoutHarness.Descendants(root).Where(b => b.HtmlTag?.Name == "span").ToList();

            // The correction really did split the span, rather than leaving a single box.
            Assert.True(spanBoxes.Count > 1, $"expected the span to be split, found {spanBoxes.Count} box(es)");

            Assert.All(spanBoxes, b =>
            {
                Assert.Equal(expectedInside, b.BreakInside);
                Assert.Equal(expectedBefore, b.BreakBefore);
                Assert.Equal(expectedAfter, b.BreakAfter);
            });
        }

        // ── the other direction: not inherited ─────────────────────────────────

        // An ordinary child is not a fragment of its parent, so it must not pick the values up. Without
        // this, moving the three copies into InheritStyle's "always" section would pass every test above.
        [Fact]
        public async Task OrdinaryChild_DoesNotInheritItsParentsBreakValues()
        {
            var html = LayoutHarness.Wrap("""
                <div id="parent" style="break-inside: avoid; break-before: page; break-after: avoid">
                  <div id="child">text</div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var child = LayoutHarness.FindById(root, "child");
            Assert.NotNull(child);

            Assert.Equal(CssConstants.Auto, child!.BreakInside);
            Assert.Equal(CssConstants.Auto, child.BreakBefore);
            Assert.Equal(CssConstants.Auto, child.BreakAfter);
        }

        // A generated-content box is a real child of the element, not a duplicate of it, and it is created
        // through the non-"everything" InheritStyle path. Same guard as above at the other call site.
        [Fact]
        public async Task GeneratedContentBox_DoesNotPickUpItsOriginatingElementsBreakValues()
        {
            var html = """
                <!DOCTYPE html><html><head><style>
                  #target { break-inside: avoid; break-before: page; break-after: avoid }
                  #target::before { content: "x" }
                </style></head><body style='margin:0'><div id="target">text</div></body></html>
                """;

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var target = LayoutHarness.FindById(root, "target");
            Assert.NotNull(target);

            var before = LayoutHarness.Descendants(target!).FirstOrDefault(b => b.IsBeforePseudoElement);
            Assert.NotNull(before);

            Assert.Equal(CssConstants.Auto, before!.BreakInside);
            Assert.Equal(CssConstants.Auto, before.BreakBefore);
            Assert.Equal(CssConstants.Auto, before.BreakAfter);
        }

        // And the element itself really does hold the values the two negative tests above are checking are
        // not propagated - so they are not passing merely because the cascade never stored anything.
        [Fact]
        public async Task TheElementItself_HoldsTheValuesTheClonesAreCheckedAgainst()
        {
            var html = LayoutHarness.Wrap(
                """<div id="target" style="break-inside: avoid; break-before: page; break-after: avoid">text</div>""");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var target = LayoutHarness.FindById(root, "target");
            Assert.NotNull(target);

            Assert.Equal(CssConstants.Avoid, target!.BreakInside);
            Assert.Equal(CssConstants.Page, target.BreakBefore);
            Assert.Equal(CssConstants.Avoid, target.BreakAfter);
        }
    }
}
