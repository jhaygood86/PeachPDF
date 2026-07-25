using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end cascade coverage for <c>box-decoration-break</c>
    /// (<see href="https://www.w3.org/TR/css-break-3/#break-decoration">css-break-3 §6.2</see>): an authored
    /// declaration has to survive Layer A's validation and land on the box, the CSS-wide keywords have to
    /// resolve against the initial-value store, and the value has to survive a structural clone of the box.
    /// <para>
    /// Everything here is driven through the real cascade rather than the <see cref="CssUtils"/> setter, so a
    /// missing registry entry or a lost global-keyword chain in the converter fails loudly. Paint is out of
    /// scope: nothing reads the value yet, so these tests assert storage only.
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

            Assert.Equal(expected, box.BoxDecorationBreak);
        }

        [Fact]
        public async Task Cascade_NoDeclaration_LeavesTheInitialValue()
        {
            var box = await FindTargetBox("");

            Assert.Equal(CssConstants.Slice, box.BoxDecorationBreak);
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
            var html = $$"""
                <!DOCTYPE html><html><head><style>
                  div { box-decoration-break: clone; }
                  #target { box-decoration-break: {{keyword}}; }
                </style></head><body><div id="target">text</div></body></html>
                """;

            var box = FindById(await BuildBoxTree(html), "target");

            Assert.NotNull(box);
            Assert.Equal(CssConstants.Slice, box!.BoxDecorationBreak);
        }

        // The companion direction: the lower-specificity "clone" really is what the keyword rolls back, so
        // the test above is not passing merely because nothing ever set the property.
        [Fact]
        public async Task Cascade_LowerSpecificityRule_SetsCloneWhenNotReset()
        {
            var html = """
                <!DOCTYPE html><html><head><style>
                  div { box-decoration-break: clone; }
                </style></head><body><div id="target">text</div></body></html>
                """;

            var box = FindById(await BuildBoxTree(html), "target");

            Assert.NotNull(box);
            Assert.Equal(CssConstants.Clone, box!.BoxDecorationBreak);
        }

        // box-decoration-break is not inherited, so an explicit "inherit" is the only way a child picks up
        // its parent's value...
        [Fact]
        public async Task Inherit_ForcesChildToPickUpParentValue()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="box-decoration-break: clone">
                  <div id="child" style="box-decoration-break: inherit">text</div>
                </div>
                </body></html>
                """;

            var child = FindById(await BuildBoxTree(html), "child");

            Assert.NotNull(child);
            Assert.Equal(CssConstants.Clone, child!.BoxDecorationBreak);
        }

        // ...and without one it stays at the initial value, however the parent is styled.
        [Fact]
        public async Task NoDeclaration_UnderACloneParent_IsNotInherited()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="box-decoration-break: clone">
                  <div id="child">text</div>
                </div>
                </body></html>
                """;

            var child = FindById(await BuildBoxTree(html), "child");

            Assert.NotNull(child);
            Assert.Equal(CssConstants.Slice, child!.BoxDecorationBreak);
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

            var root = await BuildBoxTree(html, pageHeight: 300);
            var table = Descendants(root).FirstOrDefault(b => b.HtmlTag?.Name == "table");
            Assert.NotNull(table);

            var proxies = table!.Boxes.OfType<CssProxyBox>().ToList();
            Assert.NotEmpty(proxies);
            Assert.All(proxies, proxy => Assert.Equal(CssConstants.Clone, proxy.BoxDecorationBreak));
        }

        // DomParser's block-in-inline correction splits one element's box into several. Every resulting
        // fragment box represents the same <span>, so each has to carry the span's own resolved value -
        // exactly the case box-decoration-break: clone is authored for.
        [Fact]
        public async Task BlockInsideInlineSplit_EveryFragmentCarriesTheValue()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <span style="box-decoration-break: clone">before<div>block</div>after</span>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var spanBoxes = Descendants(root).Where(b => b.HtmlTag?.Name == "span").ToList();

            // The correction really did split the span, rather than leaving a single box.
            Assert.True(spanBoxes.Count > 1, $"expected the span to be split, found {spanBoxes.Count} box(es)");
            Assert.All(spanBoxes, b => Assert.Equal(CssConstants.Clone, b.BoxDecorationBreak));
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private static async Task<CssBox> FindTargetBox(string css)
        {
            var html = $$"""
                <!DOCTYPE html><html><head><style>
                  #target { width: 200px; height: 100px; {{css}} }
                </style></head><body><div id="target">text</div></body></html>
                """;

            var box = FindById(await BuildBoxTree(html), "target");
            Assert.NotNull(box);
            return box!;
        }

        private static async Task<CssBox> BuildBoxTree(string html, double pageHeight = 842)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, pageHeight);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container.Root!;
        }

        private static IEnumerable<CssBox> Descendants(CssBox box)
        {
            yield return box;

            foreach (var child in box.Boxes)
                foreach (var descendant in Descendants(child))
                    yield return descendant;
        }

        private static CssBox? FindById(CssBox root, string id) =>
            Descendants(root).FirstOrDefault(b =>
                b.HtmlTag?.Attributes?.TryGetValue("id", out var boxId) == true
                && string.Equals(boxId, id, StringComparison.OrdinalIgnoreCase));
    }
}
