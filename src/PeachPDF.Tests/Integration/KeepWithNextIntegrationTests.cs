using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.Integration
{
    // css-break §3.1 keep-with-next: a break-after: avoid on an earlier sibling (or
    // break-before: avoid on the later one) forbids a break between the two. PeachPDF's
    // fragmentation model never inserts a break between siblings on its own - breaks between
    // siblings only appear when a whole-box nudge moves the later sibling to the next page
    // (the whole-table pre-check in CssLayoutEngineTable, break-inside: avoid, orphans/widows).
    // These tests pin the pull-along behavior at those nudge sites, asserting box positions per
    // this repo's layout-test convention. Note the UA default stylesheet already applies
    // `h1-h6 { page-break-after: avoid }` under @media print (which PeachPDF always uses), so
    // the heading tests exercise the exact path every real document hits.
    public class KeepWithNextIntegrationTests
    {
        // A table moved wholesale to the next page (CssLayoutEngineTable's pre-check) must pull
        // its avoid-chained heading along instead of stranding it at the bottom of the old page.
        [Fact]
        public async Task TableMovedToNextPage_PullsAvoidChainedHeadingAlong()
        {
            var html = BuildFillerDocument(@"
<h2 class='heading'>Section heading</h2>
<table class='keep'><tr><td><div style='height: 120px'>swatch</div></td></tr></table>");

            var (rootBox, container) = await BuildCssBoxTree(html);
            var pageHeight = container.PageSize.Height;

            var heading = FindBoxByClass(rootBox, "heading");
            var table = FindBoxByClass(rootBox, "keep");
            Assert.NotNull(heading);
            Assert.NotNull(table);

            var headingPage = Math.Floor(heading.Location.Y / pageHeight);
            var tablePage = Math.Floor(table.Location.Y / pageHeight);

            Assert.True(tablePage >= 1, $"Test setup expects the table to be moved to page 2+, but it is at y={table.Location.Y} (page {tablePage})");
            Assert.Equal(tablePage, headingPage);
            Assert.True(heading.ActualBottom <= table.Location.Y + 1.0,
                $"Heading (bottom={heading.ActualBottom}) must sit above the table (top={table.Location.Y}) after both moved");
        }

        // Without an avoid link (break-after explicitly reset to auto), the heading must stay
        // behind exactly as before - the pull is driven by the avoid chain, not proximity.
        [Fact]
        public async Task TableMovedToNextPage_LeavesNonAvoidHeadingBehind()
        {
            var html = BuildFillerDocument(@"
<h2 class='heading' style='break-after: auto; page-break-after: auto'>Section heading</h2>
<table class='keep'><tr><td><div style='height: 120px'>swatch</div></td></tr></table>");

            var (rootBox, container) = await BuildCssBoxTree(html);
            var pageHeight = container.PageSize.Height;

            var heading = FindBoxByClass(rootBox, "heading");
            var table = FindBoxByClass(rootBox, "keep");
            Assert.NotNull(heading);
            Assert.NotNull(table);

            var headingPage = Math.Floor(heading.Location.Y / pageHeight);
            var tablePage = Math.Floor(table.Location.Y / pageHeight);

            Assert.True(tablePage >= 1, $"Test setup expects the table to be moved to page 2+, but it is at y={table.Location.Y}");
            Assert.Equal(0, headingPage);
        }

        // The showcase "Themeable Card Component" shape: h2 (UA avoid default) → intro paragraph
        // with break-after: avoid → a display:none <style> element → single-row table. The chain
        // walk must skip the display:none sibling and pull BOTH the heading and the intro along
        // when the table pre-check moves the table to the next page.
        [Fact]
        public async Task TableMovedToNextPage_ChainSkipsDisplayNoneSibling_PullsHeadingAndIntroAlong()
        {
            var html = BuildFillerDocument(@"
<h2 class='heading'>Section heading</h2>
<p class='intro' style='margin: 0; break-after: avoid'>Intro paragraph kept with the content below.</p>
<style>.card { color: #222 }</style>
<table class='keep'><tr><td><div style='height: 120px'>swatch</div></td></tr></table>");

            var (rootBox, container) = await BuildCssBoxTree(html);
            var pageHeight = container.PageSize.Height;

            var heading = FindBoxByClass(rootBox, "heading");
            var intro = FindBoxByClass(rootBox, "intro");
            var table = FindBoxByClass(rootBox, "keep");
            Assert.NotNull(heading);
            Assert.NotNull(intro);
            Assert.NotNull(table);

            var tablePage = Math.Floor(table.Location.Y / pageHeight);
            Assert.True(tablePage >= 1, $"Test setup expects the table to be moved to page 2+, but it is at y={table.Location.Y} (page {tablePage})");
            Assert.Equal(tablePage, Math.Floor(heading.Location.Y / pageHeight));
            Assert.Equal(tablePage, Math.Floor(intro.Location.Y / pageHeight));
            Assert.True(heading.ActualBottom <= intro.Location.Y + 1.0,
                $"Heading (bottom={heading.ActualBottom}) must sit above the intro (top={intro.Location.Y}) after both moved");
            Assert.True(intro.ActualBottom <= table.Location.Y + 1.0,
                $"Intro (bottom={intro.ActualBottom}) must sit above the table (top={table.Location.Y}) after both moved");
        }

        // A div pushed by break-inside: avoid must pull its avoid-chained heading the same way.
        [Fact]
        public async Task BreakInsideAvoidBox_PullsAvoidChainedHeadingAlong()
        {
            var html = BuildFillerDocument(@"
<h2 class='heading'>Section heading</h2>
<div class='keep' style='break-inside: avoid; page-break-inside: avoid'>
<div style='height: 150px'>Keep together</div>
</div>");

            var (rootBox, container) = await BuildCssBoxTree(html);
            var pageHeight = container.PageSize.Height;

            var heading = FindBoxByClass(rootBox, "heading");
            var keep = FindBoxByClass(rootBox, "keep");
            Assert.NotNull(heading);
            Assert.NotNull(keep);

            var headingPage = Math.Floor(heading.Location.Y / pageHeight);
            var keepPage = Math.Floor(keep.Location.Y / pageHeight);

            Assert.True(keepPage >= 1, $"Test setup expects the avoid box to be moved to page 2+, but it is at y={keep.Location.Y}");
            Assert.Equal(keepPage, headingPage);
            Assert.True(heading.ActualBottom <= keep.Location.Y + 1.0,
                $"Heading (bottom={heading.ActualBottom}) must sit above the moved box (top={keep.Location.Y})");
        }

        // The canonical real-document case: a heading followed by a plain paragraph. The paragraph
        // is not relocated wholesale - word flow pushes its first LINE to the next page
        // (CssRect.BreakPage) - and the keep-with-next retry must still bring the heading along.
        [Fact]
        public async Task ParagraphFirstLinePushedByWordFlow_PullsAvoidChainedHeadingAlong()
        {
            var html = BuildFillerDocument(@"
<h2 class='heading'>Section heading</h2>
<p class='para' style='margin: 0'>A plain paragraph of body text that follows the heading and whose first line lands across the page boundary.</p>");

            var (rootBox, container) = await BuildCssBoxTree(html);
            var pageHeight = container.PageSize.Height;

            var heading = FindBoxByClass(rootBox, "heading");
            var para = FindBoxByClass(rootBox, "para");
            Assert.NotNull(heading);
            Assert.NotNull(para);

            var headingPage = Math.Floor(heading.Location.Y / pageHeight);
            var paraPage = Math.Floor(para.Location.Y / pageHeight);

            Assert.True(paraPage >= 1, $"Test setup expects the paragraph to start on page 2+, but it is at y={para.Location.Y}");
            Assert.Equal(paraPage, headingPage);
            Assert.True(heading.ActualBottom <= para.Location.Y + 1.0,
                $"Heading (bottom={heading.ActualBottom}) must sit above the paragraph (top={para.Location.Y})");
        }

        // Two consecutive avoid headings (h2 then h3) chain transitively - both move together
        // with the content that triggered the break.
        [Fact]
        public async Task ChainedAvoidHeadings_AllMoveTogether()
        {
            var html = BuildFillerDocument(@"
<h2 class='outer'>Chapter heading</h2>
<h3 class='inner'>Section heading</h3>
<div class='keep' style='break-inside: avoid; page-break-inside: avoid'>
<div style='height: 150px'>Keep together</div>
</div>");

            var (rootBox, container) = await BuildCssBoxTree(html);
            var pageHeight = container.PageSize.Height;

            var outer = FindBoxByClass(rootBox, "outer");
            var inner = FindBoxByClass(rootBox, "inner");
            var keep = FindBoxByClass(rootBox, "keep");
            Assert.NotNull(outer);
            Assert.NotNull(inner);
            Assert.NotNull(keep);

            var keepPage = Math.Floor(keep.Location.Y / pageHeight);
            Assert.True(keepPage >= 1, $"Test setup expects the avoid box to be moved to page 2+, but it is at y={keep.Location.Y}");
            Assert.Equal(keepPage, Math.Floor(outer.Location.Y / pageHeight));
            Assert.Equal(keepPage, Math.Floor(inner.Location.Y / pageHeight));
            Assert.True(outer.ActualBottom <= inner.Location.Y + 1.0);
            Assert.True(inner.ActualBottom <= keep.Location.Y + 1.0);
        }

        // A paragraph relocated by the orphans rule (too few lines would remain before the page
        // boundary) must pull its avoid-chained heading along too - same helper, third nudge site.
        [Fact]
        public async Task OrphansPushedParagraph_PullsAvoidChainedHeadingAlong()
        {
            var html = @"<!DOCTYPE html>
<html>
<head>
<style>
@page { size: A4; margin: 20mm; }
h2 { margin: 6px 0; }
.para { font-size: 9px; line-height: 12px; margin: 0; orphans: 2; widows: 2; }
</style>
</head>
<body>
<div class='filler' style='height: 740px'>filler</div>
<h2 class='heading'>Section heading</h2>
<p class='para'>one<br>two<br>three<br>four<br>five<br>six</p>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html);
            var pageHeight = container.PageSize.Height;

            var heading = FindBoxByClass(rootBox, "heading");
            var para = FindBoxByClass(rootBox, "para");
            Assert.NotNull(heading);
            Assert.NotNull(para);

            var headingPage = Math.Floor(heading.Location.Y / pageHeight);
            var paraPage = Math.Floor(para.Location.Y / pageHeight);

            Assert.True(paraPage >= 1, $"Test setup expects the paragraph to be relocated to page 2+, but it is at y={para.Location.Y}");
            Assert.Equal(paraPage, headingPage);
        }

        // css-break §5.2: a forced break value takes precedence over an avoid on the other side of
        // the same break point - a forced-break pair must never be treated as keep-together, even
        // when the later box is subsequently relocated by break-inside: avoid.
        [Fact]
        public async Task ForcedBreakAfter_TakesPrecedenceOverAvoid_HeadingIsNotPulled()
        {
            var html = BuildFillerDocument(@"
<h2 class='heading' style='break-after: page; page-break-after: always'>Chapter heading</h2>
<div class='keep' style='break-inside: avoid; page-break-inside: avoid; break-before: avoid'>
<div style='height: 900px'>tall keep-together content</div>
</div>");

            var (rootBox, container) = await BuildCssBoxTree(html);
            var pageHeight = container.PageSize.Height;

            var heading = FindBoxByClass(rootBox, "heading");
            var keep = FindBoxByClass(rootBox, "keep");
            Assert.NotNull(heading);
            Assert.NotNull(keep);

            // The forced break puts the keep box on page 2; break-inside then relocates it to page 3
            // (it straddles the page 2/3 boundary). The heading must stay behind on page 1: the
            // forced break between the two forbids keeping them together.
            var headingPage = Math.Floor(heading.Location.Y / pageHeight);
            var keepPage = Math.Floor(keep.Location.Y / pageHeight);

            Assert.True(keepPage >= 2, $"Test setup expects the keep box to be relocated to page 3+, but it is at y={keep.Location.Y}");
            Assert.Equal(0, headingPage);
        }

        // Unsatisfiable avoid at the break-inside site: heading + keep box taller than one page.
        // The avoid is relaxed - the box relocates alone and the heading stays, as before.
        [Fact]
        public async Task UnsatisfiableAvoidAtBreakInsideSite_IsRelaxed_BoxMovesAlone()
        {
            var html = BuildFillerDocument(@"
<h2 class='heading'>Section heading</h2>
<div class='keep' style='break-inside: avoid; page-break-inside: avoid'>
<div style='height: 900px'>taller than the space a heading would leave</div>
</div>");

            var (rootBox, container) = await BuildCssBoxTree(html);
            var pageHeight = container.PageSize.Height;

            var heading = FindBoxByClass(rootBox, "heading");
            var keep = FindBoxByClass(rootBox, "keep");
            Assert.NotNull(heading);
            Assert.NotNull(keep);

            Assert.True(Math.Floor(keep.Location.Y / pageHeight) >= 1,
                $"Test setup expects the keep box to be relocated, but it is at y={keep.Location.Y}");
            Assert.Equal(0, Math.Floor(heading.Location.Y / pageHeight));
        }

        // An unsatisfiable avoid (heading + content taller than one page) is relaxed per spec:
        // the content moves alone and the heading stays, instead of looping or overflowing.
        [Fact]
        public async Task UnsatisfiableAvoid_IsRelaxed_ContentMovesAlone()
        {
            var rows = string.Concat(Enumerable.Range(1, 60).Select(i => $"<tr><td>row {i}</td></tr>"));
            var html = BuildFillerDocument(@"
<h2 class='heading'>Section heading</h2>
<table class='keep'>" + rows + "</table>");

            var (rootBox, container) = await BuildCssBoxTree(html);
            var pageHeight = container.PageSize.Height;

            var heading = FindBoxByClass(rootBox, "heading");
            Assert.NotNull(heading);

            // The heading must not be moved somewhere nonsensical: it stays on page 1.
            var headingPage = Math.Floor(heading.Location.Y / pageHeight);
            Assert.Equal(0, headingPage);
        }

        // break-before: avoid on the later sibling is the symmetric author-side trigger and must
        // chain exactly like break-after: avoid on the earlier one.
        [Fact]
        public async Task BreakBeforeAvoid_OnMovedBox_PullsPrecedingSiblingAlong()
        {
            var html = BuildFillerDocument(@"
<div class='lead' style='break-after: auto'>Lead-in paragraph</div>
<div class='keep' style='break-inside: avoid; page-break-inside: avoid; break-before: avoid'>
<div style='height: 150px'>Keep together</div>
</div>");

            var (rootBox, container) = await BuildCssBoxTree(html);
            var pageHeight = container.PageSize.Height;

            var lead = FindBoxByClass(rootBox, "lead");
            var keep = FindBoxByClass(rootBox, "keep");
            Assert.NotNull(lead);
            Assert.NotNull(keep);

            var keepPage = Math.Floor(keep.Location.Y / pageHeight);
            Assert.True(keepPage >= 1, $"Test setup expects the avoid box to be moved to page 2+, but it is at y={keep.Location.Y}");
            Assert.Equal(keepPage, Math.Floor(lead.Location.Y / pageHeight));
        }

        #region Helpers

        // A fixed-height filler pins the section under test near the bottom of page 1
        // deterministically (page height 842, margins 20 -> content spans y=20..822), so the
        // following keep-together content is forced to relocate to page 2.
        private static string BuildFillerDocument(string section)
        {
            return @"<!DOCTYPE html>
<html>
<head>
<style>
@page { size: A4; margin: 20mm; }
h2, h3 { margin: 6px 0; }
</style>
</head>
<body>
<div class='filler' style='height: 700px'>filler</div>
" + section + @"
</body>
</html>";
        }

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildCssBoxTree(string html)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);

            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MarginTop = 20;
            container.MarginBottom = 20;

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }

        private static CssBox? FindBoxByClass(CssBox root, string className)
        {
            var classAttr = root.HtmlTag?.TryGetAttribute("class", "");
            if (!string.IsNullOrEmpty(classAttr))
            {
                foreach (var cls in classAttr.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (cls == className)
                        return root;
                }
            }

            foreach (var child in root.Boxes)
            {
                var result = FindBoxByClass(child, className);
                if (result != null)
                    return result;
            }

            return null;
        }

        #endregion
    }
}
