using System.Linq;
using System.Threading.Tasks;
using PeachPDF.Tests.TestSupport;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// A flex or grid item paints as an atomic unit in the container's item order (CSS Flexbox 1 section 4.3), so the text of a PDF reads in that
    /// order whatever the items hold: a block of only inline content and a flex container beside it used to paint in different passes and swap.
    /// </summary>
    public class FlexGridItemPaintOrderTests
    {
        private const string Svg = "<svg width='16' height='16'><rect width='16' height='16'/></svg>";

        private static async Task<string> PaintedText(string html)
        {
            var (_, container) = await LayoutAsync(Wrap(html));
            var recording = new RecordingGraphics(new PeachPDF.Rendering.HtmlRenderContext());
            FragmentPaintHarness.PaintPage(container, recording);

            return string.Join(" ", recording.Log.Where(op => op.Kind == PaintOpKind.DrawString).Select(op => op.Text));
        }

        [Theory]
        [InlineData("display:inline-block", "display:inline-flex")]
        [InlineData("display:inline-flex", "display:inline-block")]
        [InlineData("display:inline-block", "display:inline-block")]
        [InlineData("display:inline-flex", "display:inline-flex")]
        [InlineData("display:block", "display:flex")]
        public async Task FlexItemsPaintInSourceOrder_WhateverTheyHold(string first, string second)
        {
            var text = await PaintedText("<div style='display:flex;gap:10px'>"
                + $"<span style='{first};padding:4px'>{Svg}Shop</span><span style='{second};padding:4px'>{Svg}Signup</span></div>");

            Assert.Equal("Shop Signup", text);
        }

        [Fact]
        public async Task GridItemsPaintInSourceOrder_WhateverTheyHold()
        {
            var text = await PaintedText("<div style='display:grid;grid-template-columns:1fr 1fr'>"
                + $"<span style='display:inline-block'>{Svg}Shop</span><span style='display:inline-flex'>{Svg}Signup</span></div>");

            Assert.Equal("Shop Signup", text);
        }
    }
}
