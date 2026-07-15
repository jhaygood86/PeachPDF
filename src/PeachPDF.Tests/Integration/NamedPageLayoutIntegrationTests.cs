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
            // past the 500px filler), not the Location struct's (0,0) layout-time default.
            Assert.True(registered.Y > 100, $"expected a Y well past the filler div, got {registered.Y}");
            Assert.Equal(chapterBox.Location.Y, registered.Y, 1);
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
            Assert.Equal(chapterBox.Location.Y, registered.Y, 1);
        }

        [Fact]
        public async Task MultipleNamedPageElements_EachRegistersItsOwnDistinctY()
        {
            var html = Wrap("""
                <h1 id="c1" style="page:chapter">Chapter 1</h1>
                <div style="height:900px"></div>
                <h1 id="c2" style="page:chapter">Chapter 2</h1>
                <div style="height:900px"></div>
                <h1 id="c3" style="page:chapter">Chapter 3</h1>
                """);

            var (root, container) = await BuildAndLayout(html);
            var boxes = new[] { "c1", "c2", "c3" }.Select(id => FindById(root, id)!).ToList();

            var registeredYs = container.NamedPageElements.Where(e => e.Name == "chapter")
                .Select(e => e.Y).OrderBy(y => y).ToList();
            var actualYs = boxes.Select(b => b.Location.Y).OrderBy(y => y).ToList();

            Assert.Equal(3, registeredYs.Count);
            // Every registered Y must be distinct (the pre-fix bug collapsed all three to the same 0).
            Assert.Equal(3, registeredYs.Distinct().Count());
            for (var i = 0; i < 3; i++)
                Assert.Equal(actualYs[i], registeredYs[i], 1);
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
