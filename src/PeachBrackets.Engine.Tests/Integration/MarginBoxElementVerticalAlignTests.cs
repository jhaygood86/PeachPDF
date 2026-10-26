using PeachPDF;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// css-page-3 §6.2: <c>vertical-align</c> on a margin box places its content as in a table cell, and
    /// the UA default is <c>middle</c>. A <c>position: running()</c> element placed with
    /// <c>content: element()</c> must follow it just as a plain string does.
    /// </summary>
    public class MarginBoxElementVerticalAlignTests
    {
        private static async Task<(double Top, double Bottom)> RunningTopAndBottomAsync(string marginBoxDeclarations)
        {
            var html = $$"""
                <!DOCTYPE html><html><head><style>
                @page { size: a6; margin: 30mm 12mm; }
                @page { @top-center { content: element(hdr); {{marginBoxDeclarations}} } }
                .hdr { position: running(hdr); margin: 0; font-size: 9pt; }
                </style></head><body><div class="hdr">RUNNING-HEADER</div><p>body</p></body></html>
                """;

            var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
                html, new PdfGenerateConfig { PageSize = PageSize.A6 });

            var marginBox = Assert.Single(container.FragmentTree!.Fragmentainers[0].MarginBoxes, m => m.BoxName == "top-center");
            var rect = marginBox.Content.WholeBoxRect;
            return (rect.Top, rect.Bottom);
        }

        [Fact]
        public async Task RunningElement_DefaultsToMiddle_AndTopAndBottomMoveIt()
        {
            var middle = await RunningTopAndBottomAsync("");
            var top = await RunningTopAndBottomAsync("vertical-align: top;");
            var bottom = await RunningTopAndBottomAsync("vertical-align: bottom;");

            var height = top.Bottom - top.Top;
            Assert.True(height > 0);

            // Top is flush with the margin box; middle sits half the free space lower; bottom, all of it.
            Assert.True(middle.Top > top.Top + 1, $"middle ({middle.Top}) should sit below top ({top.Top})");
            Assert.True(bottom.Top > middle.Top + 1, $"bottom ({bottom.Top}) should sit below middle ({middle.Top})");
            Assert.Equal(top.Bottom - top.Top, middle.Bottom - middle.Top, 3);
            Assert.Equal(bottom.Top - middle.Top, middle.Top - top.Top, 1);
        }
    }
}
