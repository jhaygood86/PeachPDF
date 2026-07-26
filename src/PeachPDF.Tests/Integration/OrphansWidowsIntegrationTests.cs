using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System.Linq;
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
        // uses a 4-line, line-height:20pt paragraph, a 100pt page height, and a filler <div> before it
        // whose height controls exactly how many lines land before/after the Y=100 page boundary (values
        // below were confirmed empirically, not hand-derived, since exact line positions depend on
        // layout metrics). Fixture lengths are authored in pt (1pt = 1 layout unit), and the body margin
        // is pinned to 8pt (the value the UA default `body { margin: 8px }` resolved to when these
        // positions were calibrated, before px became 0.75pt), so the empirical numbers stay exact.

        private const string EightLineParagraph =
            "<p id='p' style='width:200pt;line-height:20pt;margin:0;padding:0'>" +
            "L1<br>L2<br>L3<br>L4<br>L5<br>L6<br>L7<br>L8</p>";

        private const string FourLineParagraph =
            "<p id='p' style='width:200pt;line-height:20pt;margin:0;padding:0'>Line1<br>Line2<br>Line3<br>Line4</p>";

        [Fact]
        public async Task Widows2_ParagraphNudgedWhenOnlyOneLineWouldFollowTheBreak()
        {
            // Natural position (filler=30pt) splits the 4 lines 3-before/1-after the Y=100 boundary,
            // violating the default widows:2. The whole paragraph must be pushed to start at Y=100.
            var box = await FindByIdInPagedDocAsync(30, FourLineParagraph);

            Assert.Equal(100, box.Location.Y, 1);
        }

        [Fact]
        public async Task Orphans2_ParagraphNudgedWhenOnlyOneLineWouldPrecedeTheBreak()
        {
            // Natural position (filler=68pt) splits the 4 lines 1-before/3-after the boundary, violating
            // the default orphans:2. The whole paragraph must be pushed to start at Y=100.
            var box = await FindByIdInPagedDocAsync(68, FourLineParagraph);

            Assert.Equal(100, box.Location.Y, 1);
        }

        [Fact]
        public async Task OrphansWidows_NoEffect_WhenSplitAlreadySatisfiesBothMinimums_Regression()
        {
            // Natural position (filler=50pt) splits the 4 lines cleanly 2-before/2-after the boundary -
            // satisfies both orphans:2 and widows:2 already, so the paragraph must be left exactly where
            // ordinary layout put it (Y=58), not nudged at all.
            var box = await FindByIdInPagedDocAsync(50, FourLineParagraph);

            Assert.Equal(58, box.Location.Y, 1);
        }

        [Fact]
        public async Task TallParagraph_ExceedsOnePage_IsNotNudged()
        {
            // An 8-line paragraph (160pt) is taller than the 100pt page itself - pushing it whole to the
            // next page can't satisfy orphans/widows anyway (it would just recreate the same violation
            // there), so this is a documented, accepted limitation: it's left exactly where ordinary
            // layout put it, straddling the boundary, rather than being nudged pointlessly.
            const string eightLineParagraph =
                "<p id='p' style='width:200pt;line-height:20pt;margin:0;padding:0'>" +
                "L1<br>L2<br>L3<br>L4<br>L5<br>L6<br>L7<br>L8</p>";
            var box = await FindByIdInPagedDocAsync(30, eightLineParagraph);

            Assert.Equal(38, box.Location.Y, 1);
        }

        // orphans decided at the break point rather than afterwards (css-break-3 §5.4). This is the case
        // the whole-box push deliberately skips - moving a paragraph taller than the band cannot help,
        // because it would recreate the violation on the next page - but the *break before it* can fall
        // earlier perfectly well, and with a single line above the boundary orphans:2 says it must.
        [Fact]
        public async Task Orphans2_ParagraphTallerThanTheBand_BreaksBeforeItselfRatherThanStrandingOneLine()
        {
            var box = await FindByIdInPagedDocAsync(70, EightLineParagraph);

            // Nothing of it is left on the first page: it begins at the second page's content top.
            Assert.Equal(100, box.Location.Y, 1);
            Assert.All(WordsOf(box), word => Assert.True(word.Top >= 100, $"word at {word.Top} stayed behind"));
        }

        // The author's own relaxation of the same constraint: orphans:1 permits a single-line fragment, so
        // the paragraph stays exactly where ordinary flow put it and its first line stays on page one.
        [Fact]
        public async Task Orphans1_ParagraphTallerThanTheBand_KeepsItsSingleLineFragment()
        {
            var box = await FindByIdInPagedDocAsync(
                70, EightLineParagraph.Replace("margin:0", "margin:0;orphans:1"));

            Assert.Equal(78, box.Location.Y, 1);
            Assert.Contains(WordsOf(box), word => word.Top < 100);
        }

        // Where the fragment being left already satisfies orphans, nothing moves - the same paragraph one
        // line higher keeps its three lines on the first page and continues on the second.
        [Fact]
        public async Task Orphans2_SatisfiedByTheNaturalBreak_LeavesTheParagraphWhereItIs()
        {
            var box = await FindByIdInPagedDocAsync(30, EightLineParagraph);

            Assert.Equal(38, box.Location.Y, 1);
            Assert.True(WordsOf(box).Count(word => word.Top < 100) >= 2, "expected at least orphans:2 lines to stay");
        }

        // The ladder's fourth tier: with nothing above it in the fragmentainer, moving the box cannot give
        // it more room, so the constraint is given up rather than acted on - which is also what keeps a band
        // too small for `orphans` lines from walking the box down the document one page at a time.
        [Fact]
        public async Task Orphans2_NothingAboveItInTheFragmentainer_IsLeftWhereItIs()
        {
            // A band of 30pt against 20pt lines: wherever this paragraph starts it can only ever keep one
            // line, and it starts at the very top of the band. The zero-height sibling above it is what makes
            // this reachable at all - a box that is its fragmentainer's first child is excluded already.
            var html = "<!DOCTYPE html><html><head></head><body style='margin:0'>" +
                       "<div style='height:0'></div>" +
                       "<p id='p' style='width:200pt;line-height:20pt;margin:0;padding:0'>L1<br>L2<br>L3</p>" +
                       "</body></html>";

            var (root, container) = await BuildAndLayoutPaged(html, pageHeight: 30);
            var box = FindById(root, "p")!;

            Assert.Equal(0, box.Location.Y, 1);
            Assert.True(container.FragmentainerPasses <= 4, $"expected a bounded pass count, got {container.FragmentainerPasses}");
        }

        // One correction per box per layout, not per pass. The decision is taken by the parent's child loop,
        // which runs again on every pass, and a box that travels with its keep-with-next run lands the same
        // distance below the fragmentainer top it did before - so repeated, this walks the box down the
        // document one pass per page, against a driver cap of 100,000 passes.
        [Fact]
        public async Task Orphans2_HeadingAndParagraph_AreCorrectedOnceRatherThanWalkingTheDocument()
        {
            var html = "<!DOCTYPE html><html><head></head><body style='margin:0'>" +
                       "<div style='height:70pt'></div>" +
                       "<h2 style='margin:0;font-size:10pt'>Heading</h2>" +
                       "<p id='p' style='width:200pt;line-height:20pt;margin:0;padding:0'>" +
                       "L1<br>L2<br>L3<br>L4<br>L5<br>L6<br>L7<br>L8</p>" +
                       "</body></html>";

            var (root, container) = await BuildAndLayoutPaged(html, pageHeight: 100);

            Assert.NotNull(FindById(root, "p"));
            Assert.True(container.FragmentainerPasses <= 8,
                $"expected a bounded pass count, got {container.FragmentainerPasses}");
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
                "<p id='p' style='width:200pt;line-height:20pt;margin:0;padding:0;orphans:1;widows:1'>" +
                "Line1<br>Line2<br>Line3<br>Line4</p>";
            var box = await FindByIdInPagedDocAsync(68, paragraph);

            Assert.Equal(76, box.Location.Y, 1);
        }

        private static async Task<CssBox> FindByIdInPagedDocAsync(int fillerHeightPt, string paragraphHtml)
        {
            var html = $"<!DOCTYPE html><html><head></head><body style='margin:8pt'><div style='height:{fillerHeightPt}pt'></div>{paragraphHtml}</body></html>";

            var (root, _) = await BuildAndLayoutPaged(html, pageHeight: 100);

            return FindById(root, "p")!;
        }

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayoutPaged(
            string html, double pageHeight)
        {
            var adapter = new PdfSharpAdapter();
            adapter.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(adapter);
            container.MarginTop = 0;
            container.MarginLeft = 0;
            container.MarginRight = 0;
            container.MarginBottom = 0;
            await container.SetHtml(html, null);

            var size = new XSize(400, pageHeight);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }

        /// <summary>Every word in a box's subtree — a paragraph's text lives in its child boxes.</summary>
        private static System.Collections.Generic.List<CssRect> WordsOf(CssBox box) =>
            [.. LayoutHarness.Descendants(box).SelectMany(b => b.Words)];

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
