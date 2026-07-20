using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression coverage for the primary named-page bug: <c>CssBox.PerformLayoutImp</c> used to call
    /// <c>HtmlContainer.RegisterNamedPageElement(PageName, Location.Y)</c> before <c>Location</c> was
    /// computed for that layout pass, so every <c>page: &lt;name&gt;</c> element registered at Y=0
    /// regardless of where it actually landed. These tests lay out real HTML and assert directly on the
    /// resulting <see cref="HtmlContainerInt.NamedPageElements"/> — no PDF generation or text extraction
    /// needed — per the layout-testing convention in CLAUDE.md.
    /// </summary>
    public class NamedPageLayoutIntegrationTests
    {
        [Fact]
        public async Task NamedPageElement_RegistersActualLocationY_NotZero()
        {
            var html = Wrap("""
                <div style="height:500px"></div>
                <h1 id="chapter" style="page:chapter">Chapter 2</h1>
                """);

            var (root, container) = await BuildAndLayout(html);
            var chapterBox = FindById(root, "chapter")!;

            var registered = Assert.Single(container.NamedPageElements, e => e.Name == "chapter");

            // The whole point of the fix: this must reflect the box's real, post-layout position (well
            // past the 500px filler), not the Location struct's (0,0) layout-time default. The
            // registration Y is attributed to the top of the pagination slot the box's page starts on
            // (CssBox.NamedPageRegistrationY - the box itself sits its preserved post-break margin
            // below that), so the geometry table's slot-start name attribution sees it.
            Assert.True(registered.Y > 100, $"expected a Y well past the filler div, got {registered.Y}");
            Assert.Equal(
                container.PageTopOf(container.PageIndexOf(chapterBox.Location.Y + HtmlContainerInt.PageBoundaryEpsilon)),
                registered.Y, 1);
        }

        [Fact]
        public async Task NamedPageElement_RegistersZero_OnlyWhenGenuinelyFirstOnPage()
        {
            // The one case where the pre-fix bug's stale Y=0 happened to be "accidentally correct" -
            // confirms the fix doesn't regress the one scenario that used to work.
            var html = Wrap("""<h1 id="chapter" style="page:chapter">Chapter 1</h1>""");

            var (root, container) = await BuildAndLayout(html);
            var chapterBox = FindById(root, "chapter")!;

            var registered = Assert.Single(container.NamedPageElements, e => e.Name == "chapter");
            // First element of the document: its page is slot 0, so the registration lands at the
            // content origin (slot 0's top), not at the box's own margin-shifted Y.
            Assert.Equal(container.PageTopOf(0), registered.Y, 1);
            Assert.Equal(0, container.PageIndexOf(chapterBox.Location.Y + HtmlContainerInt.PageBoundaryEpsilon));
        }

        [Fact]
        public async Task MultipleNamedPageElements_EachRegistersItsOwnDistinctY()
        {
            // Distinct names so each heading changes the active named page and forces a break onto
            // its own fresh page - three genuinely different pages, three distinct registration Ys.
            // (Same-name elements on one shared page now deliberately register at the SAME Y - the
            // top of the slot their common page starts on, per CssBox.NamedPageRegistrationY.)
            var html = Wrap("""
                <h1 id="c1" style="page:chapter1">Chapter 1</h1>
                <div style="height:900px"></div>
                <h1 id="c2" style="page:chapter2">Chapter 2</h1>
                <div style="height:900px"></div>
                <h1 id="c3" style="page:chapter3">Chapter 3</h1>
                """);

            var (root, container) = await BuildAndLayout(html);
            var boxes = new[] { "c1", "c2", "c3" }.Select(id => FindById(root, id)!).ToList();

            var registeredYs = container.NamedPageElements.Where(e => e.Name.StartsWith("chapter"))
                .Select(e => e.Y).OrderBy(y => y).ToList();
            var actualYs = boxes.Select(b => b.Location.Y).OrderBy(y => y).ToList();

            Assert.Equal(3, registeredYs.Count);
            // Every registered Y must be distinct (the pre-fix bug collapsed all three to the same 0).
            Assert.Equal(3, registeredYs.Distinct().Count());
            // Each registration is attributed to the top of the pagination slot its chapter's page
            // starts on (see NamedPageElement_RegistersActualLocationY_NotZero above).
            for (var i = 0; i < 3; i++)
                Assert.Equal(
                    container.PageTopOf(container.PageIndexOf(actualYs[i] + HtmlContainerInt.PageBoundaryEpsilon)),
                    registeredYs[i], 1);
        }

        [Fact]
        public async Task NamedPageElement_InlineElement_Registers()
        {
            // Regression coverage for the inline-element gap shared with string-set: an inline element
            // inside a text-flow container (ContainsInlinesOnly, laid out via CssLayoutEngine.FlowBox)
            // never got its own PerformLayoutImp call, so RegisterNamedPageElement never ran for it -
            // previously this would have registered zero NamedPageElements, failing Assert.Single below.
            var html = Wrap("""<p id="para"><b id="chapter" style="page:chapter">Chapter 2</b> starts here.</p>""");

            var (root, container) = await BuildAndLayout(html);
            var paraBox = FindById(root, "para")!;

            var registered = Assert.Single(container.NamedPageElements, e => e.Name == "chapter");
            // <b> is the paragraph's first inline content on its first line, so it should land at the
            // same Y as the paragraph's own top (no padding/border in this fixture).
            Assert.Equal(paraBox.Location.Y, registered.Y, 1);
        }

        [Fact]
        public async Task NamedPageElement_InlineElement_RegistersActualLocationY_NotZero()
        {
            var html = Wrap("""
                <div style="height:500px"></div>
                <p><b id="chapter" style="page:chapter">Chapter 3</b> starts here.</p>
                """);

            var (root, container) = await BuildAndLayout(html);

            var registered = Assert.Single(container.NamedPageElements, e => e.Name == "chapter");
            Assert.True(registered.Y > 100, $"expected a Y well past the filler div, got {registered.Y}");
        }

        [Fact]
        public async Task NamedString_InlineFlexElement_RegistersActualLocationY()
        {
            // Companion to NamedPageElement_InlineFlexElement_Registers - the same InlineFlex branch in
            // CssLayoutEngine.FlowBox also finalizes string-set eagerly (mirroring CssBox.PerformLayoutImp's
            // own late-stage NamedStrings correction) since its Location is already final at that point.
            var html = Wrap("""<p id="para"><span id="entry" style="display:inline-flex; string-set: term content(text)">Chapter 2</span> starts here.</p>""");

            var (root, container) = await BuildAndLayout(html);
            var entryBox = FindById(root, "entry")!;

            var namedString = Assert.Single(entryBox.NamedStrings.Values);
            Assert.Equal(entryBox.Location.Y, namedString.Y, 1);

            var documentEntry = container.NamedStrings.Single(ns => ns.Name == "term");
            Assert.Equal(entryBox.Location.Y, documentEntry.Y, 1);
        }

        [Fact]
        public async Task NamedPageElement_InlineFlexElement_Registers()
        {
            // display:inline-flex inside a text-flow container is treated as an atomic inline element
            // (CssLayoutEngine.FlowBox's dedicated InlineFlex branch), a separate code path from the
            // plain-inline case above - it finalizes its own Location eagerly rather than deferring to
            // FlowBox's generic "box != blockBox" late correction, so needs its own regression coverage.
            var html = Wrap("""<p id="para"><span id="chapter" style="display:inline-flex; page:chapter">Chapter 2</span> starts here.</p>""");

            var (root, container) = await BuildAndLayout(html);
            var chapterBox = FindById(root, "chapter")!;

            var registered = Assert.Single(container.NamedPageElements, e => e.Name == "chapter");
            Assert.Equal(chapterBox.Location.Y, registered.Y, 1);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body>{body}</body></html>";

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string html)
        {
            var adapter = new PdfSharpAdapter();
            adapter.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 5000); // tall enough that everything lands on one logical page
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize  = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            var val = box.HtmlTag?.TryGetAttribute("id", "");
            if (val != null && val.Equals(id, System.StringComparison.OrdinalIgnoreCase))
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found != null) return found;
            }
            return null;
        }
    }
}
