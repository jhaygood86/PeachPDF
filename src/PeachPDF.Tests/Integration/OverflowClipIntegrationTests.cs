using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Verifies that overflow:hidden clips at the CSS padding edge, not the content edge.
    ///
    /// The bug: table cells inherit overflow:hidden from the PeachPDF default stylesheet.
    /// The old clip used ClientRectangle (content-box) which was occasionally too narrow for
    /// child elements that fill the content area, causing border-radius arcs to be cut off.
    /// The fix expands the clip to the padding-box (ClientRectangle + ActualPadding*).
    /// </summary>
    public class OverflowClipIntegrationTests
    {
        // --- Geometry tests (verify clip bounds after layout) ---

        [Fact]
        public async Task OverflowHidden_WithPadding_ChildBoundsWithinPaddingBoxClip()
        {
            // Container: overflow:hidden, padding:10px, 100px content width.
            // Child fills the content area.
            // New clip right = ClientRight + ActualPaddingRight (= padding-box right).
            var html = @"<!DOCTYPE html><html><head><style>
body { margin: 0; }
.outer { overflow: hidden; padding: 10px; width: 100px; height: 100px; }
.inner { height: 80px; border-radius: 20px; }
</style></head><body><div class='outer'><div class='inner'></div></div></body></html>";

            var root = await GetRootBox(html);
            var outer = FindFirst(root, b => b.HtmlTag?.Name == "div" && b.Overflow == "hidden");
            var inner = FindFirst(outer!, b => b.HtmlTag?.Name == "div" && b != outer);

            Assert.NotNull(outer);
            Assert.NotNull(inner);

            var paddingBoxRight = outer!.ClientRight + outer.ActualPaddingRight;

            Assert.True(inner!.ActualRight <= paddingBoxRight,
                $"Child right ({inner.ActualRight:F3}) exceeds padding-box clip right ({paddingBoxRight:F3})");
        }

        [Fact]
        public async Task RoundedBoxInTableCell_NonUniformRadius_BoundsWithinPaddingBoxClip()
        {
            // Reproduces the original bug: td gets overflow:hidden from the default stylesheet.
            // The div with a non-uniform border-radius (10px 30px) fills the td content area.
            // Its right edge must fit within the td's padding-box clip.
            var html = @"<!DOCTYPE html><html><head><style>
body { margin: 0; }
table { border-collapse: collapse; width: 300px; }
td { padding: 3px; }
</style></head><body>
<table><tr>
  <td><div style='border-radius: 10px 30px; height: 60px; border: 2px solid black;'></div></td>
</tr></table>
</body></html>";

            var root = await GetRootBox(html);
            var td = FindFirst(root, b => b.HtmlTag?.Name == "td");
            var div = FindFirst(td!, b => b.HtmlTag?.Name == "div");

            Assert.NotNull(td);
            Assert.NotNull(div);

            // td has overflow:hidden from the PeachPDF default stylesheet
            Assert.Equal("hidden", td!.Overflow);

            var paddingBoxRight = td.ClientRight + td.ActualPaddingRight;

            Assert.True(div!.ActualRight <= paddingBoxRight,
                $"Div right ({div.ActualRight:F3}) exceeds padding-box clip right ({paddingBoxRight:F3})");
        }

        [Fact]
        public async Task RoundedBoxInTableCell_FourValueRadius_BoundsWithinPaddingBoxClip()
        {
            var html = @"<!DOCTYPE html><html><head><style>
body { margin: 0; }
table { border-collapse: collapse; width: 300px; }
td { padding: 3px; }
</style></head><body>
<table><tr>
  <td><div style='border-radius: 5px 15px 30px 45px; height: 60px; border: 2px solid black;'></div></td>
</tr></table>
</body></html>";

            var root = await GetRootBox(html);
            var td = FindFirst(root, b => b.HtmlTag?.Name == "td");
            var div = FindFirst(td!, b => b.HtmlTag?.Name == "div");

            Assert.NotNull(td);
            Assert.NotNull(div);

            var paddingBoxRight = td!.ClientRight + td.ActualPaddingRight;

            Assert.True(div!.ActualRight <= paddingBoxRight,
                $"Div right ({div.ActualRight:F3}) exceeds padding-box clip right ({paddingBoxRight:F3})");
        }

        [Fact]
        public async Task OverflowHidden_ZeroPadding_ClipEqualsContentBox()
        {
            // When padding is 0, padding-box == content-box.
            // The clip right should equal ClientRight (no expansion).
            var html = @"<!DOCTYPE html><html><head><style>
body { margin: 0; }
.outer { overflow: hidden; padding: 0; width: 100px; height: 100px; }
.inner { height: 80px; }
</style></head><body><div class='outer'><div class='inner'></div></div></body></html>";

            var root = await GetRootBox(html);
            var outer = FindFirst(root, b => b.HtmlTag?.Name == "div" && b.Overflow == "hidden");
            var inner = FindFirst(outer!, b => b.HtmlTag?.Name == "div" && b != outer);

            Assert.NotNull(outer);
            Assert.NotNull(inner);

            // Zero padding: padding-box right = ClientRight + 0 = ClientRight
            Assert.Equal(0.0, outer!.ActualPaddingRight, 3);
            var paddingBoxRight = outer.ClientRight + outer.ActualPaddingRight;
            Assert.Equal(outer.ClientRight, paddingBoxRight, 3);

            // Child still fits
            Assert.True(inner!.ActualRight <= paddingBoxRight + 0.01,
                $"Child right ({inner.ActualRight:F3}) exceeds content-box clip right ({paddingBoxRight:F3})");
        }

        // --- Smoke tests (PDF generation must not throw) ---

        [Fact]
        public async Task RoundedBoxInTableCell_MultipleRadii_GeneratesPdf()
        {
            var html = @"<!DOCTYPE html><html><head><style>
table { border-collapse: collapse; width: 100%; }
td { padding: 3px; }
.rbox { height: 60px; background: steelblue; border: 2px solid #1a6b8a; }
</style></head><body>
<table><tr>
  <td><div class='rbox' style='border-radius: 20px;'></div></td>
  <td><div class='rbox' style='border-radius: 10px 30px;'></div></td>
  <td><div class='rbox' style='border-radius: 8px 20px 35px;'></div></td>
  <td><div class='rbox' style='border-radius: 5px 15px 30px 45px;'></div></td>
</tr></table>
</body></html>";

            var generator = new PdfGenerator();
            var ex = await Record.ExceptionAsync(() => generator.GeneratePdf(html, PageSize.A4));
            Assert.Null(ex);
        }

        // --- Helpers ---

        private static CssBox? FindFirst(CssBox box, Func<CssBox, bool> predicate)
        {
            if (predicate(box)) return box;
            foreach (var child in box.Boxes)
            {
                var found = FindFirst(child, predicate);
                if (found != null) return found;
            }
            return null;
        }

        private static async Task<CssBox> GetRootBox(string html)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container.Root!;
        }
    }
}
