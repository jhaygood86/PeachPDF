using PeachBrackets.Html.Fragments;
using PeachBrackets.Html.Paint;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// A stacking-context descendant (transform, opacity below 1, a z-indexed positioned box) of a
    /// <c>float: footnote</c> body used to be skipped and claimed by no ordering scope: the body is painted on its own, so
    /// nothing above it could paint the box. The detached root is now an ordering scope itself.
    /// </summary>
    public class FootnoteStackingContextPaintTests
    {
        private static async Task<List<string>> PaintFootnotesAsync(string noteHtml)
        {
            var (_, container) = await LayoutAsync(
                "<style>body{margin:0}div{font:10pt/12pt sans-serif}</style>"
                + $"<div>Text<span style='float:footnote'>{noteHtml}</span> after</div>",
                pageWidth: 400, pageHeight: 400, margin: 20);

            var g = new TestRecordingGraphics();
            var painter = new FragmentPainter(container);
            var bodies = 0;

            foreach (var area in container.FragmentTree!.Fragmentainers.SelectMany(f => f.FootnoteAreas ?? []))
            {
                foreach (var body in area.Bodies)
                {
                    painter.PaintDetached(g, body);
                    bodies++;
                }
            }

            Assert.True(bodies > 0);
            return [.. g.Log.OfType<TestRecordingGraphics.DrawStringCall>().Select(d => d.Text)];
        }

        private const string Box = "display:inline-block;width:40pt;height:12pt;";

        [Theory]
        [InlineData("transform:rotate(30deg)")]
        [InlineData("opacity:.5")]
        [InlineData("opacity:.99")]
        [InlineData("position:relative;z-index:2")]
        [InlineData("")]
        public async Task AnInlineBlockInAFootnoteBody_IsPaintedExactlyOnce(string effect)
        {
            var drawn = await PaintFootnotesAsync($"note A <span style='{Box}{effect}'>inner</span> end");

            Assert.Single(drawn, t => t == "inner");
            Assert.Contains("note", drawn);
            Assert.Contains("end", drawn);
        }

        [Fact]
        public async Task ABlockWithATransformedDescendantInAFootnoteBody_IsPaintedOnce()
        {
            var drawn = await PaintFootnotesAsync(
                $"<span>one <span style='{Box}transform:scale(.9)'>deep</span></span><span style='{Box}opacity:.5'>two</span>");

            Assert.Single(drawn, t => t == "deep");
            Assert.Single(drawn, t => t == "two");
        }
    }
}
