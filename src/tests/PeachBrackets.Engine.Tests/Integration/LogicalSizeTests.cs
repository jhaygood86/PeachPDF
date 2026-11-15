using PeachPDF.Rendering;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using System.Threading.Tasks;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>The logical size properties (css-logical-1 section 4) resolve to the physical width/height of the box's own writing mode.</summary>
    public class LogicalSizeTests
    {
        private static async Task<CssBox> LayOut(string style, string writingMode = "horizontal-tb")
        {
            var html = "<!DOCTYPE html><html><head><style>body{margin:0}#a{display:block;" + style + "}</style></head><body>" +
                       "<div style='writing-mode:" + writingMode + ";width:300pt;height:300pt'><div id='a'></div></div></body></html>";
            var rc = new HtmlRenderContext { PixelsPerPoint = 1.0 };
            var c = new HtmlContainerInt(rc);
            await c.SetHtml(html, null);
            var size = new Size(595, 842);
            c.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            c.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            using var g = PdfCanvas.CreateMeasuring(rc, size);
            await c.PerformLayout(g);

            static CssBox? Find(CssBox b)
            {
                if (b.HtmlTag?.TryGetAttribute("id", "") == "a") return b;
                foreach (var x in b.Boxes) if (Find(x) is { } f) return f;
                return null;
            }

            return Find(c.Root!)!;
        }

        [Fact]
        public async Task InlineAndBlockSize_AreWidthAndHeight_InAHorizontalWritingMode()
        {
            var box = await LayOut("inline-size:50pt;block-size:30pt");

            Assert.Equal(50, box.ActualRight - box.Location.X, 0.5);
            Assert.Equal(30, box.ActualBottom - box.Location.Y, 0.5);
        }

        [Fact]
        public async Task InlineAndBlockSize_SwapAxes_InAVerticalWritingMode()
        {
            var box = await LayOut("inline-size:50pt;block-size:30pt", "vertical-rl");

            Assert.Equal(30, box.ActualRight - box.Location.X, 0.5);
            Assert.Equal(50, box.ActualBottom - box.Location.Y, 0.5);
        }

        [Fact]
        public async Task MaxInlineSize_CapsTheWidth()
        {
            var box = await LayOut("width:100pt;height:10pt;max-inline-size:40pt;min-block-size:25pt");

            Assert.Equal(40, box.ActualRight - box.Location.X, 0.5);
            Assert.Equal(25, box.ActualBottom - box.Location.Y, 0.5);
        }

        [Fact]
        public async Task ThePhysicalDeclarationWrittenLater_Wins()
        {
            var box = await LayOut("inline-size:50pt;width:70pt");

            Assert.Equal(70, box.ActualRight - box.Location.X, 0.5);
        }

        [Fact]
        public async Task TheLogicalDeclarationWrittenLater_Wins()
        {
            var box = await LayOut("width:70pt;inline-size:50pt");

            Assert.Equal(50, box.ActualRight - box.Location.X, 0.5);
        }
    }
}
