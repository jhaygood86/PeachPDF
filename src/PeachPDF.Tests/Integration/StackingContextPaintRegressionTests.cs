using System.Diagnostics;
using System.Text;
using PeachPDF;
using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.Advanced;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression tests for two paint-pipeline fixes:
    ///
    /// 1. <c>DomUtils.FlattenStackingContext</c> used to recurse into every descendant at every
    ///    ancestor level (not just direct children), so a box was independently re-painted once per
    ///    ancestor between it and the root. Fixed to only hoist genuinely out-of-flow (floated,
    ///    absolutely positioned, fixed) descendants past normal-flow wrapper boxes; normal content is
    ///    now painted exactly once, via its own parent's Paint() call.
    ///
    /// 2. <c>CssBox.Paint</c> used to always treat block-level boxes (Rectangles.Count == 0) as
    ///    visible regardless of the current page's clip rect, so every page walked the entire box
    ///    tree. Fixed to prune using the box's own Bounds instead - but only when the document has no
    ///    out-of-flow content anywhere (see HtmlContainerInt.HasOutOfFlowBoxes), since an out-of-flow
    ///    descendant's visual position can fall outside its "invisible" ancestor's own Bounds.
    ///
    /// Both fixes are scoped to fall back to the original (slower but unconditionally correct)
    /// behaviour whenever the document has any float/absolute/fixed content, so the riskiest case to
    /// regression-test is exactly that: out-of-flow content nested deep inside plain wrapper boxes.
    /// </summary>
    public class StackingContextPaintRegressionTests
    {
        [Fact]
        public async Task PositionAbsolute_DeeplyNestedInPlainWrappers_StillRendersContent()
        {
            // Six levels of plain (non-positioned, non-stacking-context) wrapper divs, with a
            // position:absolute box buried at the bottom. Its containing block is the outermost
            // (position:relative) wrapper, not its immediate DOM parent - it must still be discovered
            // and painted via FlattenStackingContext's out-of-flow hoisting, not silently dropped.
            var html = @"
<!DOCTYPE html>
<html>
<head><style>
    .positioned-root { position: relative; width: 400px; height: 300px; }
    .plain { }
</style></head>
<body>
    <div class='positioned-root'>
        <div class='plain'><div class='plain'><div class='plain'>
            <div class='plain'><div class='plain'><div class='plain'>
                <div style='position:absolute; top:10px; left:10px; width:50px; height:50px; background:red;'></div>
            </div></div></div>
        </div></div></div>
    </div>
</body>
</html>";

            var generator = new PdfGenerator();
            var document = await generator.GeneratePdf(html, PageSize.A4, margin: 20);

            Assert.Equal(1, document.PageCount);
            Assert.True(PageHasContent(document.Pages[0]), "the single page should have content");
        }

        [Fact]
        public async Task ZIndexedPositionedSiblings_RenderWithoutThrowing()
        {
            // Two position:relative siblings with different z-index values are each their own
            // stacking context (per IsStackingContextBox) - confirm they're still found and painted
            // (not excluded as "already handled elsewhere") now that FlattenStackingContext no longer
            // blindly recurses through everything.
            var html = @"
<!DOCTYPE html>
<html>
<head><style>
    .box { position: relative; width: 100px; height: 100px; }
</style></head>
<body>
    <div class='box' style='z-index: 1; background: red;'>Back</div>
    <div class='box' style='z-index: 2; top: -50px; left: 50px; background: blue;'>Front</div>
</body>
</html>";

            var generator = new PdfGenerator();
            var document = await generator.GeneratePdf(html, PageSize.A4, margin: 20);

            Assert.Equal(1, document.PageCount);
            Assert.True(PageHasContent(document.Pages[0]));
        }

        [Fact]
        public async Task MultiPageRepeatedContent_EveryPageHasContent()
        {
            // Regression for the Bounds-based visibility pruning: with many pages of distinct
            // repeated content, every single page must still have content - none should come out
            // blank because an ancestor spanning many pages was wrongly judged invisible for a page
            // it does, in fact, have content on.
            var html = BuildRepeatedPagedSectionsHtml(sectionCount: 30);

            var generator = new PdfGenerator();
            var document = await generator.GeneratePdf(html, PageSize.A4, margin: 20);

            Assert.True(document.PageCount >= 5, $"expected several pages, got {document.PageCount}");

            for (var i = 0; i < document.PageCount; i++)
            {
                Assert.True(PageHasContent(document.Pages[i]), $"page {i + 1} of {document.PageCount} should have content");
            }
        }

        [Fact]
        public async Task ManyNestedNormalFlowBoxes_RenderWithinASaneTimeBound()
        {
            // Regression guard for FlattenStackingContext's old O(depth) blowup: with no out-of-flow
            // content anywhere, every box used to be independently re-painted once per ancestor level
            // between it and the root. This document has real nesting depth (six wrapper levels) times
            // real breadth (many repeated sections), so a regression back to the old behaviour would
            // make this take dramatically longer than the generous bound below.
            var html = BuildDeeplyNestedRepeatedSectionsHtml(sectionCount: 40, nestingDepth: 6);

            var generator = new PdfGenerator();
            var sw = Stopwatch.StartNew();
            var document = await generator.GeneratePdf(html, PageSize.A4, margin: 20);
            sw.Stop();

            Assert.True(document.PageCount > 0);
            Assert.True(sw.ElapsedMilliseconds < 5000,
                $"rendering took {sw.ElapsedMilliseconds}ms - this should complete in well under a second; " +
                "a multi-second time suggests FlattenStackingContext has regressed back to repainting every " +
                "box once per ancestor level.");
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static string BuildRepeatedPagedSectionsHtml(int sectionCount)
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><style>");
            sb.Append("@page { size: A4; margin: 20mm; }");
            sb.Append(".section { page-break-after: always; border: 1px solid black; padding: 8px; }");
            sb.Append("</style></head><body>");

            for (var i = 0; i < sectionCount; i++)
            {
                sb.Append($"<div class='section'><h2>Section {i}</h2><p>Distinct content for section number {i}.</p></div>");
            }

            sb.Append("</body></html>");
            return sb.ToString();
        }

        private static string BuildDeeplyNestedRepeatedSectionsHtml(int sectionCount, int nestingDepth)
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><style>");
            sb.Append(".section { border: 1px solid black; padding: 4px; margin-bottom: 8px; }");
            sb.Append("</style></head><body>");

            for (var i = 0; i < sectionCount; i++)
            {
                sb.Append("<div class='section'>");
                for (var d = 0; d < nestingDepth; d++)
                {
                    sb.Append("<div>");
                }
                sb.Append($"Section {i} content deep inside {nestingDepth} wrapper levels.");
                for (var d = 0; d < nestingDepth; d++)
                {
                    sb.Append("</div>");
                }
                sb.Append("</div>");
            }

            sb.Append("</body></html>");
            return sb.ToString();
        }

        private static bool PageHasContent(PdfPage page)
        {
            try
            {
                var content = page.Contents;
                if (content == null)
                    return false;

                if (content.Elements.Count == 0)
                    return false;

                foreach (var item in content.Elements)
                {
                    if (item is PdfReference { Value: PdfDictionary dict })
                    {
                        var stream = dict.Stream;
                        if (stream?.Value is { Length: > 0 })
                            return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
