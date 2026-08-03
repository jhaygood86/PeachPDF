using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end cascade coverage for <c>box-decoration-break</c>
    /// (<see href="https://www.w3.org/TR/css-break-3/#break-decoration">css-break-3 §6.2</see>): an authored
    /// declaration has to survive Layer A's validation and land on the box, the CSS-wide keywords have to
    /// resolve against the initial-value store and the revert snapshot, and the value has to survive a
    /// structural clone of the box.
    /// <para>
    /// Everything here is driven through the real cascade rather than the <see cref="CssUtils"/> setter, so a
    /// missing registry entry or a lost global-keyword chain in the converter fails loudly. These tests assert
    /// storage only — what the stored value then does to the output is
    /// <see cref="BoxDecorationBreakPaintIntegrationTests"/>.
    /// </para>
    /// </summary>
    public class BoxDecorationBreakCascadeTests
    {
        // ── the value set, through the cascade ─────────────────────────────────

        [Theory]
        [InlineData("clone", "clone")]
        [InlineData("slice", "slice")]
        [InlineData("CLONE", "clone")]
        // Outside the value set: invalid CSS, dropped at parse time, so the initial value stands.
        [InlineData("none", "slice")]
        [InlineData("both", "slice")]
        public async Task Cascade_StoresOnlySpecValues(string authored, string expected)
        {
            var box = await FindTargetBox($"box-decoration-break: {authored}");

            Assert.Equal(expected, box.BoxDecorationBreak.ToString());
        }

        [Fact]
        public async Task Cascade_NoDeclaration_LeavesTheInitialValue()
        {
            var box = await FindTargetBox("");

            Assert.Equal(BoxDecorationBreakMode.Slice, box.BoxDecorationBreak.Value);
        }

        // ── the CSS-wide keywords ──────────────────────────────────────────────

        // The whole point of the CssDefaults initial-value entry: without it these four resolve to null and
        // the declaration is silently dropped, leaving the "clone" the lower-specificity rule set. The two
        // rules have to be separate - a second declaration in the same block replaces the first at parse
        // time, so the box would never actually hold "clone" for the keyword to reset.
        [Theory]
        [InlineData("initial")]
        [InlineData("unset")]
        [InlineData("revert")]
        [InlineData("revert-layer")]
        public async Task Cascade_GlobalKeyword_ResetsToSlice(string keyword)
        {
            var box = await FindTargetBox($"box-decoration-break: {keyword}", lowerPriorityCss: "box-decoration-break: clone");

            Assert.Equal(BoxDecorationBreakMode.Slice, box.BoxDecorationBreak.Value);
        }

        // The companion direction: the lower-priority "clone" really is what the keyword rolls back, so the
        // theory above is not passing merely because nothing ever set the property.
        [Fact]
        public async Task Cascade_LowerPriorityRule_SetsCloneWhenNotReset()
        {
            var box = await FindTargetBox("", lowerPriorityCss: "box-decoration-break: clone");

            Assert.Equal(BoxDecorationBreakMode.Clone, box.BoxDecorationBreak.Value);
        }

        // What distinguishes the _knownPropertyNames entry from the initial-value entry: revert-layer rolls
        // back to the *snapshot* taken before the winning layer, not to the initial value. Without the
        // known-name entry there is no snapshot to find, DomParser falls back to the initial value, and this
        // resolves to "slice" instead of the lower layer's "clone" - which the two theories above cannot
        // tell apart, since for them both paths land on "slice" anyway.
        [Fact]
        public async Task RevertLayer_RollsBackToTheLowerLayer_NotTheInitialValue()
        {
            var html = """
                <!DOCTYPE html><html><head><style>
                  @layer base, override;
                  @layer base { #target { box-decoration-break: clone } }
                  @layer override { #target { box-decoration-break: revert-layer } }
                </style></head><body><div id="target">text</div></body></html>
                """;

            var box = await FindById(html, "target");

            Assert.Equal(BoxDecorationBreakMode.Clone, box.BoxDecorationBreak.Value);
        }

        // box-decoration-break is not inherited, so an explicit "inherit" is the only way a child picks up
        // its parent's value...
        [Fact]
        public async Task Inherit_ForcesChildToPickUpParentValue()
        {
            var html = LayoutHarness.Wrap("""
                <div id="parent" style="box-decoration-break: clone">
                  <div id="child" style="box-decoration-break: inherit">text</div>
                </div>
                """);

            var child = await FindById(html, "child");

            Assert.Equal(BoxDecorationBreakMode.Clone, child.BoxDecorationBreak.Value);
        }

        // ...and without one it stays at the initial value, however the parent is styled.
        [Fact]
        public async Task NoDeclaration_UnderACloneParent_IsNotInherited()
        {
            var html = LayoutHarness.Wrap("""
                <div id="parent" style="box-decoration-break: clone">
                  <div id="child">text</div>
                </div>
                """);

            var child = await FindById(html, "child");

            Assert.Equal(BoxDecorationBreakMode.Slice, child.BoxDecorationBreak.Value);
        }

        // ── the two structural-clone call sites ────────────────────────────────

        // CssProxyBox's repeated table header: the proxy is a structural duplicate of the same <thead>, so it
        // must carry that element's own resolved value (InheritStyle's "everything" branch).
        [Fact]
        public async Task RepeatedTableHeaderProxy_CarriesTheSourceValue()
        {
            var rows = string.Join("", Enumerable.Range(1, 20)
                .Select(i => $"<tr><td>Row {i}, Cell 1</td><td>Row {i}, Cell 2</td></tr>"));

            var html = $$"""
                <!DOCTYPE html><html><head><style>
                  table { width: 100%; border-collapse: collapse; }
                  thead { display: table-header-group; box-decoration-break: clone; }
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
            Assert.All(proxies, proxy => Assert.Equal(BoxDecorationBreakMode.Clone, proxy.BoxDecorationBreak.Value));
        }

        // DomParser's block-in-inline correction splits one element's box into several. Every resulting
        // fragment box represents the same <span>, so each has to carry the span's own resolved value -
        // exactly the case box-decoration-break: clone is authored for.
        [Fact]
        public async Task BlockInsideInlineSplit_EveryFragmentCarriesTheValue()
        {
            var html = LayoutHarness.Wrap(
                """<span style="box-decoration-break: clone">before<div>block</div>after</span>""");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var spanBoxes = LayoutHarness.Descendants(root).Where(b => b.HtmlTag?.Name == "span").ToList();

            // The correction really did split the span, rather than leaving a single box.
            Assert.True(spanBoxes.Count > 1, $"expected the span to be split, found {spanBoxes.Count} box(es)");
            Assert.All(spanBoxes, b => Assert.Equal(BoxDecorationBreakMode.Clone, b.BoxDecorationBreak.Value));
        }

        // ── helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Lays out a single <c>#target</c> div whose declarations come from an <c>#target</c> rule, optionally
        /// preceded by a lower-priority (bare type selector) rule the CSS-wide keywords can roll back to.
        /// </summary>
        private static async Task<CssBox> FindTargetBox(string css, string lowerPriorityCss = "")
        {
            var html = $$"""
                <!DOCTYPE html><html><head><style>
                  div { {{lowerPriorityCss}} }
                  #target { width: 200px; height: 100px; {{css}} }
                </style></head><body><div id="target">text</div></body></html>
                """;

            return await FindById(html, "target");
        }

        private static async Task<CssBox> FindById(string html, string id)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var box = LayoutHarness.FindById(root, id);

            Assert.NotNull(box);
            return box!;
        }
    }
}
