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
<p>Line 21</p><p>Line 22</p><p>Line 23</p><p>Line 24</p>
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
