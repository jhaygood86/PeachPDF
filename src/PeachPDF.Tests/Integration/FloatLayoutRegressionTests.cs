using System.Diagnostics;
using System.Text;
using PeachPDF;
using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression tests for float layout and the <c>HtmlContainerInt.HasFloatedBoxes</c> short-circuit
    /// added to <c>DomUtils.GetFirstIntersectingFloatBox</c>. That lookup used to walk all the way to
    /// the document root and re-scan every preceding sibling's whole subtree, for every box needing
    /// line layout, at every ancestor level - regardless of whether the document had any floated content
    /// at all. These tests confirm float-avoidance still works correctly when floats ARE present, and
    /// that a float-free document (the common case, and the one the short-circuit targets) still lays
    /// out and renders within a sane time bound.
    /// </summary>
    public class FloatLayoutRegressionTests
    {
        [Fact]
        public async Task Float_PushesFollowingSiblingTextToTheRight()
        {
            var html = Wrap(@"
                <div style='width:300px;'>
                    <div style='float:left; width:100px; height:50px;'></div>
                    <p id='text' style='margin:0;'>Hello world</p>
                </div>");

            var (root, _) = await BuildAndLayout(html);
            var text = FindById(root, "text")!;
            var firstWord = FindFirstWord(text);

            Assert.NotNull(firstWord);
            Assert.True(firstWord!.Rectangle.Left >= 90,
                $"first word should be pushed right past the 100px float, was at {firstWord.Rectangle.Left}");
        }

        [Fact]
        public async Task WithoutFloat_SiblingTextStartsAtContainerEdge()
        {
            // Same shape as the test above but the earlier div is a plain block (no float), so the
            // paragraph's text should start back at the container's left edge - this is the contrast
            // case confirming the previous test's assertion is actually about float avoidance, not
            // some unrelated margin/padding default.
            var html = Wrap(@"
                <div style='width:300px;'>
                    <div style='width:100px; height:50px;'></div>
                    <p id='text' style='margin:0;'>Hello world</p>
                </div>");

            var (root, _) = await BuildAndLayout(html);
            var text = FindById(root, "text")!;
            var firstWord = FindFirstWord(text);

            Assert.NotNull(firstWord);
            Assert.True(firstWord!.Rectangle.Left < 10,
                $"first word should start at the container's left edge without a float, was at {firstWord.Rectangle.Left}");
        }

        [Fact]
        public async Task Float_NarrowsAvailableWidth_SoTextWrapsToMoreLines()
        {
            const string longText =
                "This is a fairly long sentence that should wrap across multiple lines once the available width is narrowed by a floated sibling element.";

            var withFloatHtml = Wrap($@"
                <div style='width:250px;'>
                    <div style='float:left; width:150px; height:40px;'></div>
                    <p id='text' style='margin:0;'>{longText}</p>
                </div>");

            var withoutFloatHtml = Wrap($@"
                <div style='width:250px;'>
                    <p id='text' style='margin:0;'>{longText}</p>
                </div>");

            var (withFloatRoot, _) = await BuildAndLayout(withFloatHtml);
            var (withoutFloatRoot, _) = await BuildAndLayout(withoutFloatHtml);

            var withFloatText = FindById(withFloatRoot, "text")!;
            var withoutFloatText = FindById(withoutFloatRoot, "text")!;

            Assert.True(withFloatText.ActualBoxSizingHeight > withoutFloatText.ActualBoxSizingHeight,
                $"narrowing the line width with a float should force extra line wraps and a taller box " +
                $"(with float: {withFloatText.ActualBoxSizingHeight}, without: {withoutFloatText.ActualBoxSizingHeight})");
        }

        [Fact]
        public async Task ManyNestedBlocksWithoutFloats_RendersWithinASaneTimeBound()
        {
            // Regression guard for the O(document size) walk that GetFirstIntersectingFloatBox used
            // to perform for every box, at every ancestor level, even with zero floats anywhere. This
            // document has no floats, so HasFloatedBoxes should short-circuit the whole thing; if that
            // regresses, this - a fairly modest document - would take dramatically longer than the
            // generous bound below (the original bug scaled towards seconds even for smaller documents
            // than the 100-section one used here).
            var html = BuildRepeatedSectionsHtml(sectionCount: 40);

            var generator = new PdfGenerator();
            var sw = Stopwatch.StartNew();
            var document = await generator.GeneratePdf(html, PageSize.A4, margin: 20);
            sw.Stop();

            Assert.True(document.PageCount > 0);
            // Bound is generous (well beyond the sub-second time this actually takes) to absorb CI
            // noise from shared/contended runners - e.g. the net8.0 and net10.0 TFM test runs executing
            // concurrently in the same job. A real regression back to an O(document size) walk per box
            // scales towards many seconds even for smaller documents than this, so it still trips this.
            Assert.True(sw.ElapsedMilliseconds < 15000,
                $"rendering {40} float-free repeated sections took {sw.ElapsedMilliseconds}ms - " +
                "this should complete in well under a second; a multi-second time suggests the float " +
                "scan short-circuit (HasFloatedBoxes) has regressed back to an O(document size) walk per box.");
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body>{body}</body></html>";

        private static string BuildRepeatedSectionsHtml(int sectionCount)
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><style>");
            sb.Append(".section { border: 1px solid black; padding: 4px; margin-bottom: 8px; }");
            sb.Append("table { width: 100%; border-collapse: collapse; } td { border: 1px solid #ccc; padding: 2px; }");
            sb.Append("</style></head><body>");

            for (var i = 0; i < sectionCount; i++)
            {
                sb.Append($"<div class='section'><h3>Section {i}</h3><table>");
                for (var row = 0; row < 8; row++)
                {
                    sb.Append($"<tr><td>Item {i}-{row}</td><td>Qty {row}</td><td>${row * 10}.00</td></tr>");
                }
                sb.Append("</table></div>");
            }

            sb.Append("</body></html>");
            return sb.ToString();
        }

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

        private static CssRect? FindFirstWord(CssBox box)
        {
            if (box.Words.Count > 0) return box.Words[0];
            foreach (var child in box.Boxes)
            {
                var found = FindFirstWord(child);
                if (found is not null) return found;
            }
            return null;
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            var val = box.HtmlTag?.TryGetAttribute("id", "");
            if (val != null && val.Equals(id, StringComparison.OrdinalIgnoreCase))
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
