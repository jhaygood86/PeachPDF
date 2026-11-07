using PeachPDF.Rendering;
using PeachDrawing.PDF;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// The WebKit-era properties (<c>-webkit-box-reflect</c>, <c>-webkit-text-fill-color</c>, <c>-webkit-text-security</c>, <c>initial-letter</c>)
    /// carried through a real PDF render. The layout and paint calls are asserted in PeachBrackets.Engine.Tests; this checks that the whole
    /// pipeline writes a document in which each one changed what is drawn.
    /// </summary>
    public class WebkitLegacyPropertiesPdfTests
    {
        private static async Task<byte[]> RenderAsync(string body)
        {
            var html = "<!DOCTYPE html><html><head><style>body{margin:20pt;font:12pt/18pt Arial}</style></head><body>" + body + "</body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, PageSize.A4);
            Assert.Equal(1, result.Inspect().PageCount);
            using var stream = new MemoryStream();
            result.Save(stream);
            return stream.ToArray();
        }

        [Fact]
        public async Task BoxReflect_AddsContent_WithAndWithoutAMask()
        {
            const string box = "width:100pt;height:40pt;background:#3498db;";
            var plain = await RenderAsync($"<div style='{box}'></div>");
            var reflected = await RenderAsync($"<div style='{box}-webkit-box-reflect:below 2pt'></div>");
            var masked = await RenderAsync($"<div style='{box}-webkit-box-reflect:above linear-gradient(transparent, white)'></div>");
            var leftRight = await RenderAsync($"<div style='{box}-webkit-box-reflect:right 3pt'></div><div style='{box}-webkit-box-reflect:left'></div>");

            Assert.True(reflected.Length > plain.Length);
            Assert.True(masked.Length > plain.Length);
            Assert.True(leftRight.Length > plain.Length);
        }

        [Fact]
        public async Task TextFillColor_ChangesTheFillOperator()
        {
            var plain = await RenderAsync("<p style='color:#0000ff'>Hi</p>");
            var filled = await RenderAsync("<p style='color:#0000ff;-webkit-text-fill-color:#ff0000'>Hi</p>");

            Assert.NotEqual(plain, filled);
        }

        [Fact]
        public async Task TextSecurity_ChangesTheDrawnText()
        {
            var plain = await RenderAsync("<p>secret</p>");
            var masked = await RenderAsync("<p style='-webkit-text-security:square'>secret</p>");

            Assert.NotEqual(plain, masked);
        }

        [Fact]
        public async Task InitialLetter_ChangesTheLayout_AndRaiseAndDropBothRender()
        {
            const string text = "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.";
            var plain = await RenderAsync($"<p style='width:300pt'>{text}</p>");
            var dropped = await RenderAsync($"<style>p::first-letter{{initial-letter:3}}</style><p style='width:300pt'>{text}</p>");
            var raised = await RenderAsync($"<style>p::first-letter{{initial-letter:3 1}}</style><p style='width:300pt'>{text}</p>");

            Assert.NotEqual(plain, dropped);
            Assert.NotEqual(dropped, raised);
        }
    }
}
