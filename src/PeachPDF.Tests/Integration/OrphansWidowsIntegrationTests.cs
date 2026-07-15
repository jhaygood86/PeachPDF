using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    public class OrphansWidowsIntegrationTests
    {
        [Fact]
        public async Task Orphans_DefaultsToTwo()
        {
            var box = await FindByIdAsync("<p id='p'>text</p>");
            Assert.Equal("2", box.Orphans);
        }

        [Fact]
        public async Task Widows_DefaultsToTwo()
        {
            var box = await FindByIdAsync("<p id='p'>text</p>");
            Assert.Equal("2", box.Widows);
        }

        [Fact]
        public async Task Orphans_ParsesExplicitValue()
        {
            var box = await FindByIdAsync("<p id='p' style='orphans:3'>text</p>");
            Assert.Equal("3", box.Orphans);
        }

        [Fact]
        public async Task Widows_ParsesExplicitValue()
        {
            var box = await FindByIdAsync("<p id='p' style='widows:1'>text</p>");
            Assert.Equal("1", box.Widows);
        }

        [Fact]
        public async Task Orphans_RejectsZero()
        {
            // orphans/widows must be >= 1 per spec; an invalid value leaves the property at its default.
            var box = await FindByIdAsync("<p id='p' style='orphans:0'>text</p>");
            Assert.Equal("2", box.Orphans);
        }

        [Fact]
        public async Task Widows_IsInherited()
        {
            var html = Wrap("<div style='widows:4'><p id='p'>text</p></div>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "p")!;
            Assert.Equal("4", box.Widows);
        }

        [Fact]
        public async Task Orphans_IsInherited()
        {
            var html = Wrap("<div style='orphans:5'><p id='p'>text</p></div>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "p")!;
            Assert.Equal("5", box.Orphans);
        }

        // ─── Break-avoidance behavior ────────────────────────────────────────────
        // Regression coverage for the actual break-avoidance effect: previously orphans/widows were
        // parsed/cascaded/inherited (above) but never consulted by any layout code at all - a full-tree
        // grep found zero non-storage read sites. Wired into plain block-flow pagination, at the same
        // page-boundary check break-inside:avoid already uses (CssBox.PerformLayoutImp). Every scenario
        // uses a 4-line, line-height:20px paragraph, a 100px page height, and a filler <div> before it
        // whose height controls exactly how many lines land before/after the Y=100 page boundary (values
        // below were confirmed empirically, not hand-derived, since exact line positions depend on
        // layout metrics).

        private const string FourLineParagraph =
            "<p id='p' style='width:200px;line-height:20px;margin:0;padding:0'>Line1<br>Line2<br>Line3<br>Line4</p>";

        [Fact]
        public async Task Widows2_ParagraphNudgedWhenOnlyOneLineWouldFollowTheBreak()
        {
            // Natural position (filler=30px) splits the 4 lines 3-before/1-after the Y=100 boundary,
            // violating the default widows:2. The whole paragraph must be pushed to start at Y=100.
            var box = await FindByIdInPagedDocAsync(30, FourLineParagraph);

            Assert.Equal(100, box.Location.Y, 1);
        }

        [Fact]
        public async Task Orphans2_ParagraphNudgedWhenOnlyOneLineWouldPrecedeTheBreak()
        {
            // Natural position (filler=68px) splits the 4 lines 1-before/3-after the boundary, violating
            // the default orphans:2. The whole paragraph must be pushed to start at Y=100.
            var box = await FindByIdInPagedDocAsync(68, FourLineParagraph);

            Assert.Equal(100, box.Location.Y, 1);
        }

        [Fact]
        public async Task OrphansWidows_NoEffect_WhenSplitAlreadySatisfiesBothMinimums_Regression()
        {
            // Natural position (filler=50px) splits the 4 lines cleanly 2-before/2-after the boundary -
            // satisfies both orphans:2 and widows:2 already, so the paragraph must be left exactly where
            // ordinary layout put it (Y=58), not nudged at all.
            var box = await FindByIdInPagedDocAsync(50, FourLineParagraph);

            Assert.Equal(58, box.Location.Y, 1);
        }

        [Fact]
        public async Task TallParagraph_ExceedsOnePage_IsNotNudged()
        {
            // An 8-line paragraph (160px) is taller than the 100px page itself - pushing it whole to the
            // next page can't satisfy orphans/widows anyway (it would just recreate the same violation
            // there), so this is a documented, accepted limitation: it's left exactly where ordinary
            // layout put it, straddling the boundary, rather than being nudged pointlessly.
            const string eightLineParagraph =
                "<p id='p' style='width:200px;line-height:20px;margin:0;padding:0'>" +
                "L1<br>L2<br>L3<br>L4<br>L5<br>L6<br>L7<br>L8</p>";
            var box = await FindByIdInPagedDocAsync(30, eightLineParagraph);

            Assert.Equal(38, box.Location.Y, 1);
        }

        [Fact]
        public async Task Widows2_ParagraphStartingOnSecondPage_StillNudgedCorrectly()
        {
            // Regression coverage for the "own top relative to page" modulo loop only having ever been
            // exercised with a box on the first page (Location.Y < pageHeight, loop body never runs) -
            // a filler tall enough to push the paragraph's own start onto the second page forces that
            // loop to actually iterate.
            var box = await FindByIdInPagedDocAsync(130, FourLineParagraph);

            Assert.Equal(200, box.Location.Y, 1);
        }

        [Fact]
        public async Task Orphans1Widows1_MatchesDictionaryCssValues_NoEffect_Regression()
        {
            // css4.pub's real dictionary sets "widows: 1; orphans: 1" - already maximally permissive
            // (a single line before/after a break is always enough), so this feature should never nudge
            // anything on that document. Confirms explicit orphans:1/widows:1 never triggers a nudge even
            // in a split (1-before/3-after) that WOULD violate the default orphans:2/widows:2.
            const string paragraph =
                "<p id='p' style='width:200px;line-height:20px;margin:0;padding:0;orphans:1;widows:1'>" +
                "Line1<br>Line2<br>Line3<br>Line4</p>";
            var box = await FindByIdInPagedDocAsync(68, paragraph);

            Assert.Equal(76, box.Location.Y, 1);
        }

        private static async Task<CssBox> FindByIdInPagedDocAsync(int fillerHeightPx, string paragraphHtml)
        {
            var html = $"<!DOCTYPE html><html><head></head><body><div style='height:{fillerHeightPx}px'></div>{paragraphHtml}</body></html>";

            var adapter = new PdfSharpAdapter();
            adapter.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(adapter);
            container.MarginTop = 0;
            container.MarginLeft = 0;
            container.MarginRight = 0;
            container.MarginBottom = 0;
            await container.SetHtml(html, null);

            var size = new XSize(400, 100);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return FindById(container.Root!, "p")!;
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body>{body}</body></html>";

        private async Task<CssBox> FindByIdAsync(string fragment)
        {
            var (root, _) = await BuildAndLayout(Wrap(fragment));
            return FindById(root, "p")!;
        }

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string html)
        {
            var adapter = new PdfSharpAdapter();
            adapter.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
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
