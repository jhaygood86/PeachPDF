using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.Integration
{
    public class PageBreakIntegrationTests
    {
        // Reproduces issue #50: page-break-inside: avoid splits content when preceded
        // by an empty page-break-after: always div.
        // Spec: css-break §3.1 — a forced break occurs when break-after on the earlier
        // sibling has a forced break value (page-break-after: always maps to break-after: page).
        [Fact]
        public async Task PageBreakAfter_ForcesBreakBeforeNextSection()
        {
            var html = @"<!DOCTYPE html>
<html>
<head>
<style>
@page { size: A4; margin: 20mm; }
.filler { line-height: 2; }
.bordered { border: 2px solid black; padding: 10px; break-inside: avoid; page-break-inside: avoid; }
</style>
</head>
<body>
<div class='filler'>
<p>Line 1</p><p>Line 2</p><p>Line 3</p><p>Line 4</p><p>Line 5</p>
<p>Line 6</p><p>Line 7</p><p>Line 8</p><p>Line 9</p><p>Line 10</p>
<p>Line 11</p><p>Line 12</p><p>Line 13</p><p>Line 14</p><p>Line 15</p>
<p>Line 16</p><p>Line 17</p><p>Line 18</p><p>Line 19</p><p>Line 20</p>
<p>Line 21</p><p>Line 22</p><p>Line 23</p><p>Line 24</p><p>Line 25</p>
</div>
<div style='page-break-after: always;'></div>
<div class='bordered'>
<p>Section B paragraph 1</p>
<p>Section B paragraph 2</p>
<p>Section B paragraph 3</p>
<p>Section B paragraph 4</p>
</div>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html);
            var pageHeight = container.PageSize.Height;

            var borderedBox = FindBoxByClass(rootBox, "bordered");
            Assert.NotNull(borderedBox);

            // The bordered box must start on page 2 (Location.Y >= pageHeight).
            Assert.True(borderedBox.Location.Y >= pageHeight,
                $"Bordered box should start on page 2 (y >= {pageHeight}) but starts at y={borderedBox.Location.Y}");

            // The bordered box must not be split: its full height fits within the page.
            var boxHeight = borderedBox.ActualBottom - borderedBox.Location.Y;
            var topRelativeToPage = borderedBox.Location.Y % pageHeight;
            Assert.True(topRelativeToPage + boxHeight <= pageHeight,
                $"Bordered box (height={boxHeight}) should not be split across pages (topOnPage={topRelativeToPage}, pageHeight={pageHeight})");
        }

        // Regression: existing break-before: page behaviour must still work after the fix.
        [Fact]
        public async Task PageBreakBefore_ForcesBreak()
        {
            var html = @"<!DOCTYPE html>
<html>
<head>
<style>
@page { size: A4; margin: 20mm; }
.filler { line-height: 2; }
.bordered { border: 2px solid black; padding: 10px; page-break-before: always; break-inside: avoid; }
</style>
</head>
<body>
<div class='filler'>
<p>Line 1</p><p>Line 2</p><p>Line 3</p><p>Line 4</p><p>Line 5</p>
<p>Line 6</p><p>Line 7</p><p>Line 8</p><p>Line 9</p><p>Line 10</p>
</div>
<div class='bordered'>
<p>Section B paragraph 1</p>
<p>Section B paragraph 2</p>
<p>Section B paragraph 3</p>
</div>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html);
            var pageHeight = container.PageSize.Height;

            var borderedBox = FindBoxByClass(rootBox, "bordered");
            Assert.NotNull(borderedBox);

            Assert.True(borderedBox.Location.Y >= pageHeight,
                $"Bordered box with break-before:page should start on page 2 (y >= {pageHeight}) but starts at y={borderedBox.Location.Y}");
        }

        // Verifies that break-inside: avoid repositions a box to the top of the next page
        // (using MarginTop, not MarginBottom) when it would naturally straddle a page boundary.
        [Fact]
        public async Task BreakInside_Avoid_PositionsAtTopOfNextPage()
        {
            var html = @"<!DOCTYPE html>
<html>
<head>
<style>
@page { size: A4; margin: 20mm; }
.filler { line-height: 2; }
.avoid { break-inside: avoid; page-break-inside: avoid; border: 1px solid black; padding: 8px; }
</style>
</head>
<body>
<div class='filler'>
<p>Line 1</p><p>Line 2</p><p>Line 3</p><p>Line 4</p><p>Line 5</p>
<p>Line 6</p><p>Line 7</p><p>Line 8</p><p>Line 9</p><p>Line 10</p>
<p>Line 11</p><p>Line 12</p><p>Line 13</p><p>Line 14</p><p>Line 15</p>
<p>Line 16</p><p>Line 17</p><p>Line 18</p><p>Line 19</p><p>Line 20</p>
<p>Line 21</p><p>Line 22</p><p>Line 23</p><p>Line 24</p><p>Line 25</p>
<p>Line 26</p><p>Line 27</p><p>Line 28</p><p>Line 29</p><p>Line 30</p>
</div>
<div class='avoid'>
<p>Keep together paragraph 1</p>
<p>Keep together paragraph 2</p>
<p>Keep together paragraph 3</p>
<p>Keep together paragraph 4</p>
</div>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html);
            var pageHeight = container.PageSize.Height;
            var marginTop = container.MarginTop;

            var avoidBox = FindBoxByClass(rootBox, "avoid");
            Assert.NotNull(avoidBox);

            // Box must not straddle a page boundary.
            var boxHeight = avoidBox.ActualBottom - avoidBox.Location.Y;
            var topRelativeToPage = avoidBox.Location.Y % pageHeight;
            Assert.True(topRelativeToPage + boxHeight <= pageHeight,
                $"Box with break-inside:avoid (height={boxHeight}) must not be split (topOnPage={topRelativeToPage}, pageHeight={pageHeight})");

            // When relocated to the next page, the box's Y offset within its page should
            // equal MarginTop (matching the BreakBefore positioning formula), not MarginBottom.
            Assert.True(avoidBox.Location.Y >= pageHeight,
                "Test setup expects the avoid box to be relocated to the next page to validate MarginTop positioning.");

            var offsetWithinPage = avoidBox.Location.Y % pageHeight;
            Assert.True(Math.Abs(offsetWithinPage - marginTop) < 1.0,
                $"Relocated box should sit at MarginTop ({marginTop}) within its page, but offset is {offsetWithinPage}");
        }

        // CSS2.1 §13.2: a page break is forced whenever a box's own explicit `page` value differs
        // from the named page currently active in the document flow - independent of
        // break-before/break-after. Real Prince/browser-print authoring relies on this so a chapter
        // heading alone (not every following paragraph) starts a fresh page.
        [Fact]
        public async Task PageNameChange_ForcesBreak()
        {
            var html = @"<!DOCTYPE html>
<html>
<head>
<style>
@page { size: A4; margin: 20mm; }
</style>
</head>
<body>
<div class='first' style='page: alpha;'>First</div>
<div class='second' style='page: beta;'>Second</div>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html);
            var pageHeight = container.PageSize.Height;

            var first = FindBoxByClass(rootBox, "first");
            var second = FindBoxByClass(rootBox, "second");
            Assert.NotNull(first);
            Assert.NotNull(second);

            Assert.True(second.Location.Y >= pageHeight,
                $"Box with a differing `page` value should be pushed to the next page (y >= {pageHeight}) but starts at y={second.Location.Y}");
        }

        [Fact]
        public async Task SamePageName_DoesNotForceBreak()
        {
            var html = @"<!DOCTYPE html>
<html>
<head>
<style>
@page { size: A4; margin: 20mm; }
</style>
</head>
<body>
<div class='first' style='page: alpha;'>First</div>
<div class='second' style='page: alpha;'>Second</div>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html);
            var pageHeight = container.PageSize.Height;

            var second = FindBoxByClass(rootBox, "second");
            Assert.NotNull(second);

            Assert.True(second.Location.Y < pageHeight,
                $"Two boxes sharing the same `page` value must not force a break between them, but second box starts at y={second.Location.Y} (page height {pageHeight})");
        }

        [Fact]
        public async Task UnsetPageName_CarriesForwardWithoutForcingBreak()
        {
            // The used value of `page` (CSS Paged Media Level 3 §3) is tree-based: a box with no
            // explicit `page` uses its parent box's used value. So the ordinary "page: auto" (unset)
            // *descendants* of a named chapter container all carry that container's named page forward
            // and must not each force their own additional break - only the container that actually
            // changes the named page does. (A *following sibling* of the container, by contrast,
            // reverts - see NamedPageReversion tests.)
            var html = @"<!DOCTYPE html>
<html>
<head>
<style>
@page { size: A4; margin: 20mm; }
</style>
</head>
<body>
<div class='chapter' style='page: alpha;'>
<div class='heading'>Chapter</div>
<div class='body1'>Paragraph 1</div>
<div class='body2'>Paragraph 2</div>
</div>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html);
            var pageHeight = container.PageSize.Height;

            var heading = FindBoxByClass(rootBox, "heading");
            var body1 = FindBoxByClass(rootBox, "body1");
            var body2 = FindBoxByClass(rootBox, "body2");
            Assert.NotNull(heading);
            Assert.NotNull(body1);
            Assert.NotNull(body2);

            var headingPage = Math.Floor(heading.Location.Y / pageHeight);
            var body1Page = Math.Floor(body1.Location.Y / pageHeight);
            var body2Page = Math.Floor(body2.Location.Y / pageHeight);

            Assert.Equal(headingPage, body1Page);
            Assert.Equal(headingPage, body2Page);
        }

        // CSS Fragmentation Level 3 §5.2: "When an unforced break occurs before or after a
        // block-level box, any margins adjoining the break are truncated to zero." A margin that
        // stays within the same page as its previous sibling's bottom never triggers this - must
        // behave exactly as before.
        [Fact]
        public async Task Margin_NotCrossingPageBoundary_IsNotTruncated()
        {
            var html = @"<!DOCTYPE html>
<html>
<head>
<style>
@page { size: A4; margin: 20mm; }
body { margin: 0; }
.filler { height: 100pt; margin: 0; padding: 0; border: 0; }
.second { margin-top: 50pt; }
</style>
</head>
<body>
<div class='filler'></div>
<div class='second'>Second</div>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html);

            var filler = FindBoxByClass(rootBox, "filler");
            var second = FindBoxByClass(rootBox, "second");
            Assert.NotNull(filler);
            Assert.NotNull(second);

            // second's margin-top (50) doesn't cross a page boundary from filler's bottom, so second
            // should land at exactly filler.ActualBottom + 50, completely unaffected by truncation.
            Assert.Equal(filler.ActualBottom + 50, second.Location.Y, 0.5);
        }

        // A margin just barely large enough to cross a single page boundary must be discarded
        // entirely - the box lands flush at the top of the very next page (offset = MarginTop),
        // not "partially through" the margin.
        [Fact]
        public async Task Margin_CrossingOnePageBoundary_TruncatesToZero_LandsAtTopOfNextPage()
        {
            var html = @"<!DOCTYPE html>
<html>
<head>
<style>
@page { size: A4; margin: 20mm; }
body { margin: 0; }
.filler { height: 800pt; margin: 0; padding: 0; border: 0; }
.second { margin-top: 80pt; }
</style>
</head>
<body>
<div class='filler'></div>
<div class='second'>Second</div>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html);
            var pageHeight = container.PageSize.Height;
            var marginTop = container.MarginTop;

            var filler = FindBoxByClass(rootBox, "filler");
            var second = FindBoxByClass(rootBox, "second");
            Assert.NotNull(filler);
            Assert.NotNull(second);

            // This harness's own Root.Location starts at y=0 (not MarginTop - see BuildCssBoxTree),
            // so filler's own bottom is ~800 (its height) and the real page-1-content-top boundary
            // (matching HtmlContainerInt.PageTopOf(1)) sits at pageHeight + marginTop, not pageHeight
            // alone - confirm the untruncated math really would cross that real boundary (800 + 80 =
            // 880 > pageHeight + marginTop), so this test is exercising the intended case. A margin
            // that only clears the *raw* pageHeight without also clearing + marginTop (e.g. the
            // previous 50pt here) doesn't actually cross a real page boundary and must NOT truncate -
            // that was itself the bug this fix corrects.
            Assert.True(filler.ActualBottom + 80 > pageHeight + marginTop,
                $"test setup should cross a page boundary: filler.ActualBottom={filler.ActualBottom}, pageHeight={pageHeight}, marginTop={marginTop}");

            Assert.True(second.Location.Y >= pageHeight,
                $"second should land on page 2 (y >= {pageHeight}) but starts at y={second.Location.Y}");

            var offsetWithinPage = second.Location.Y % pageHeight;
            Assert.True(Math.Abs(offsetWithinPage - marginTop) < 1.0,
                $"Truncated margin should leave second flush at MarginTop ({marginTop}) within page 2, but offset is {offsetWithinPage}");
        }

        // Acid2's own actual scenario: a margin so large it would span several page heights with
        // no real content in it at all (e.g. "margin-top: 100em"). Truncation must still land the
        // box on the very NEXT page after its previous sibling - not skip further pages just
        // because the untruncated margin would have reached further.
        [Fact]
        public async Task HugeMultiPageMargin_TruncatesToZero_LandsOnVeryNextPage()
        {
            var html = @"<!DOCTYPE html>
<html>
<head>
<style>
@page { size: A4; margin: 20mm; }
body { margin: 0; }
.filler { height: 50px; margin: 0; padding: 0; border: 0; }
.second { margin-top: 3000px; }
</style>
</head>
<body>
<div class='filler'></div>
<div class='second'>Second</div>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html);
            var pageHeight = container.PageSize.Height;
            var marginTop = container.MarginTop;

            var second = FindBoxByClass(rootBox, "second");
            Assert.NotNull(second);

            // filler (height 50px) ends well within page index 0 - the very next page is page
            // index 1, not some later page the untruncated 3000px margin would have reached on its
            // own (which would be several pages further down).
            var actualPage = Math.Floor((second.Location.Y - marginTop) / pageHeight);
            Assert.Equal(1, actualPage);

            var offsetWithinPage = second.Location.Y % pageHeight;
            Assert.True(Math.Abs(offsetWithinPage - marginTop) < 1.0,
                $"Truncated margin should leave second flush at MarginTop ({marginTop}), but offset is {offsetWithinPage}");
        }

        // A forced break (page-break-before: always) already relocates the previous sibling's
        // bottom to the next page's top - per CSS Fragmentation §5.2, the margin AFTER a forced
        // break is preserved (not truncated), unlike the unforced case above. This confirms the two
        // mechanisms don't double-adjust: the box's own (small, non-crossing) margin-top is added
        // normally on top of the forced-break relocation, not truncated a second time.
        [Fact]
        public async Task ForcedBreak_MarginAfterBreak_IsPreservedNotTruncated()
        {
            var html = @"<!DOCTYPE html>
<html>
<head>
<style>
@page { size: A4; margin: 20mm; }
body { margin: 0; }
.filler { height: 100pt; margin: 0; padding: 0; border: 0; }
.second { page-break-before: always; margin-top: 50pt; }
</style>
</head>
<body>
<div class='filler'></div>
<div class='second'>Second</div>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html);
            var pageHeight = container.PageSize.Height;
            var marginTop = container.MarginTop;

            var second = FindBoxByClass(rootBox, "second");
            Assert.NotNull(second);

            // Forced break puts the (bumped) previous sibling bottom at exactly pageHeight + marginTop;
            // second's own 50pt margin-top should be added normally on top of that, not truncated away.
            Assert.Equal(pageHeight + marginTop + 50, second.Location.Y, 0.5);
        }

        #region Helpers

        private async Task<(CssBox root, HtmlContainerInt container)> BuildCssBoxTree(string html)
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

        private CssBox? FindBoxByClass(CssBox root, string className)
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
