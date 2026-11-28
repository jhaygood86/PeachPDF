using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A sized gradient layer whose tile is far thinner than a pixel (<c>bottom / 100% 0.25pt no-repeat</c>, the usual
    /// hairline separator) must render, not fail the whole document.
    /// </summary>
    public class GradientHairlineBackgroundTests
    {
        [Theory]
        [InlineData("div")]
        [InlineData("td")]
        public async Task HairlineGradientStripRenders(string tag)
        {
            var body = tag == "td" ? "<table><tr><td>A</td></tr></table>" : "<div>A</div>";
            var html = $"<html><head><style>{tag} {{ background: linear-gradient(#e0e0e0, #e0e0e0) bottom / 100% 0.25pt no-repeat; padding: 4pt; }}</style></head><body>{body}</body></html>";

            var doc = await new PdfGenerator().GeneratePdf(html, new PdfGenerateConfig { PageSize = PageSize.A4 });
            using var ms = new MemoryStream();
            doc.Save(ms);
            Assert.True(ms.Length > 0);
        }
    }
}
