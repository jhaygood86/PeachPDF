using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Verifies <c>:link</c> matches exactly <c>&lt;a href&gt;</c> elements and nothing else, and locks
    /// in the accepted-gap behavior that <c>:visited</c>/<c>:active</c> never match by design (no
    /// browsing history or interaction state exists in a static PDF renderer - see CLAUDE.md's
    /// "Out of scope / accepted gaps"). None of this had a dedicated test before this batch.
    /// </summary>
    public class LinkPseudoClassIntegrationTests
    {
        // Anchors used to verify :link matching deliberately have no id/name attribute -
        // CssBox.IsClickable (the mechanism :link matches against) excludes an <a> that carries either,
        // historically reserved for `<a name="section">`-style fragment targets - so FindByTag (not
        // FindById) locates the anchor in these tests.

        [Fact]
        public async Task Link_MatchesAnchorWithHref()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<style>a:link { color: rgb(1,2,3) }</style><a href='https://example.com'>link</a>"));
            var a = FindByTag(root, "a")!;

            Assert.Equal("rgb(1, 2, 3)", a.Color);
        }

        [Fact]
        public async Task Link_DoesNotMatchAnchorWithoutHref()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<style>a:link { color: rgb(1,2,3) }</style><a>not a link</a>"));
            var a = FindByTag(root, "a")!;

            Assert.NotEqual("rgb(1, 2, 3)", a.Color);
        }

        [Fact]
        public async Task Link_DoesNotMatchNonAnchorElement()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<style>:link { color: rgb(1,2,3) }</style><span id='s' href='https://example.com'>not an anchor</span>"));
            var s = FindById(root, "s")!;

            Assert.NotEqual("rgb(1, 2, 3)", s.Color);
        }

        [Fact]
        public async Task Visited_NeverMatches_ByDesign()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<style>a:visited { color: rgb(1,2,3) }</style><a href='https://example.com'>link</a>"));
            var a = FindByTag(root, "a")!;

            Assert.NotEqual("rgb(1, 2, 3)", a.Color);
        }

        [Fact]
        public async Task Active_NeverMatches_ByDesign()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<style>a:active { color: rgb(1,2,3) }</style><a href='https://example.com'>link</a>"));
            var a = FindByTag(root, "a")!;

            Assert.NotEqual("rgb(1, 2, 3)", a.Color);
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

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

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

        private static CssBox? FindByTag(CssBox box, string tag)
        {
            if (box.HtmlTag?.Name.Equals(tag, System.StringComparison.OrdinalIgnoreCase) == true)
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindByTag(child, tag);
                if (found != null) return found;
            }
            return null;
        }
    }
}
