using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression coverage for the string-set Y-timing bug: <c>CssNamedStringEngine.ApplyStringSet</c>
    /// (called from <c>CssBox.PerformLayoutImp</c>) reads <c>cssBox.Location.Y</c> before <c>Location</c>
    /// is computed for that layout pass, so every <c>NamedString</c> used to register at Y=0 regardless
    /// of where its element actually landed. <c>MarginBoxRenderer.ResolveNamedString</c>'s page-range
    /// matching (see MarginBoxResolveNamedStringTests) only worked by accident on page 1 as a result.
    /// These tests lay out real HTML and assert directly on the resulting
    /// <see cref="HtmlContainerInt.NamedStrings"/> — no PDF generation or text extraction needed — per
    /// the layout-testing convention in CLAUDE.md.
    /// </summary>
    public class NamedStringLayoutIntegrationTests
    {
        [Fact]
        public async Task NamedString_RegistersActualLocationY_NotZero()
        {
            var html = Wrap("""
                <div style="height:500px"></div>
                <h1 id="term" style="string-set: term content()">Banana</h1>
                """);

            var (root, container) = await BuildAndLayout(html);
            var termBox = FindById(root, "term")!;

            var registered = Assert.Single(container.NamedStrings, s => s.Name == "term");

            // The whole point of the fix: this must reflect the box's real, post-layout position (well
            // past the 500px filler), not the Location struct's (0,0) layout-time default.
            Assert.True(registered.Y > 100, $"expected a Y well past the filler div, got {registered.Y}");
            Assert.Equal(termBox.Location.Y, registered.Y, 1);
        }

        [Fact]
        public async Task NamedString_RegistersZero_OnlyWhenGenuinelyFirstOnPage()
        {
            // The one case where the pre-fix bug's stale Y=0 happened to be "accidentally correct" -
            // confirms the fix doesn't regress the one scenario that used to work.
            var html = Wrap("""<h1 id="term" style="string-set: term content()">Apple</h1>""");

            var (root, container) = await BuildAndLayout(html);
            var termBox = FindById(root, "term")!;

            var registered = Assert.Single(container.NamedStrings, s => s.Name == "term");
            Assert.Equal(termBox.Location.Y, registered.Y, 1);
        }

        [Fact]
        public async Task MultipleNamedStrings_EachRegistersItsOwnDistinctY()
        {
            var html = Wrap("""
                <h1 id="t1" style="string-set: term content()">Apple</h1>
                <div style="height:900px"></div>
                <h1 id="t2" style="string-set: term content()">Banana</h1>
                <div style="height:900px"></div>
                <h1 id="t3" style="string-set: term content()">Cherry</h1>
                """);

            var (root, container) = await BuildAndLayout(html);
            var boxes = new[] { "t1", "t2", "t3" }.Select(id => FindById(root, id)!).ToList();

            var registeredYs = container.NamedStrings.Where(s => s.Name == "term")
                .Select(s => s.Y).OrderBy(y => y).ToList();
            var actualYs = boxes.Select(b => b.Location.Y).OrderBy(y => y).ToList();

            Assert.Equal(3, registeredYs.Count);
            // Every registered Y must be distinct (the pre-fix bug collapsed all three to the same 0).
            Assert.Equal(3, registeredYs.Distinct().Count());
            for (var i = 0; i < 3; i++)
                Assert.Equal(actualYs[i], registeredYs[i], 1);
        }

        [Fact]
        public async Task NestedStringSet_AncestorReadByDescendant_SeesValueDespiteLateYCorrection()
        {
            // Regression guard for the specific risk identified while designing this fix: the value
            // computation and document-list registration for string-set still happen at their original,
            // early call site (unchanged) - only Y gets corrected afterwards, in place, on the same
            // object reference. A descendant's own string-set referencing its ancestor's named string via
            // nested string() must still see the value during the same layout pass.
            var html = Wrap("""
                <div id="chapter" style="string-set: chapter content()">
                    Chapter One
                    <h2 id="section" style="string-set: section string(chapter) ' - ' content()">Section A</h2>
                </div>
                """);

            var (root, container) = await BuildAndLayout(html);
            var sectionBox = FindById(root, "section")!;

            Assert.True(sectionBox.NamedStrings.ContainsKey("section"));
            Assert.Contains("Chapter One", sectionBox.NamedStrings["section"].Value);
            Assert.Contains("Section A", sectionBox.NamedStrings["section"].Value);
        }

        [Fact]
        public async Task NamedString_InlineElement_Registers()
        {
            // Regression coverage for the inline-string-set bug: an element inside a text-flow container
            // (ContainsInlinesOnly, laid out via CssLayoutEngine.FlowBox) never got its own
            // PerformLayoutImp call, so ApplyStringSet never ran for it at all - previously this would
            // have registered zero NamedStrings, failing Assert.Single below.
            var html = Wrap("""<p id="para"><b id="term" style="string-set: term content()">Apple</b> is a fruit.</p>""");

            var (root, container) = await BuildAndLayout(html);
            var paraBox = FindById(root, "para")!;

            var registered = Assert.Single(container.NamedStrings, s => s.Name == "term");
            Assert.Equal("Apple", registered.Value);
            // <b> is the paragraph's first inline content on its first line, so it should land at the
            // same Y as the paragraph's own top (no padding/border in this fixture).
            Assert.Equal(paraBox.Location.Y, registered.Y, 1);
        }

        [Fact]
        public async Task NamedString_InlineElement_RegistersActualLocationY_NotZero()
        {
            var html = Wrap("""
                <div style="height:500px"></div>
                <p><b id="term" style="string-set: term content()">Banana</b> is a fruit.</p>
                """);

            var (root, container) = await BuildAndLayout(html);

            var registered = Assert.Single(container.NamedStrings, s => s.Name == "term");
            Assert.True(registered.Y > 100, $"expected a Y well past the filler div, got {registered.Y}");
        }

        [Fact]
        public async Task NamedString_NestedInlineInInline_Registers()
        {
            var html = Wrap("""<p><b><i id="term" style="string-set: term content()">Cherry</i></b> is a fruit.</p>""");

            var (root, container) = await BuildAndLayout(html);
            Assert.NotNull(FindById(root, "term"));

            var registered = Assert.Single(container.NamedStrings, s => s.Name == "term");
            Assert.Equal("Cherry", registered.Value);
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
