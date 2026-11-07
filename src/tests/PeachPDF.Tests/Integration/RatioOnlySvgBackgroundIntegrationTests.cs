using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// An svg with only a <c>viewBox</c> has an aspect ratio and no size; as a background with <c>background-size: auto</c> it is fitted to its positioning area
    /// (CSS Images 3 section 5.3), as slashdot.org's logo is. The PDF carries it as vector content, so the fill of its rectangle is the evidence that it was painted
    /// (the box-fitting arithmetic itself is asserted in <c>PeachBrackets.Engine.Tests</c>).
    /// </summary>
    public class RatioOnlySvgBackgroundIntegrationTests
    {
        private const string Svg = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 2 1"><rect width="2" height="1" fill="#FF0000"/></svg>""";

        private static async Task<string> GetPdfText(string html)
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            config.SetMargins(0);
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        [Theory]
        [InlineData("background: url('{0}') no-repeat")]
        [InlineData("background: url('{0}') no-repeat; background-size: contain")]
        [InlineData("background: url('{0}') no-repeat; background-size: 50% auto")]
        public async Task ARatioOnlySvgBackground_IsPainted(string declaration)
        {
            var uri = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(Svg));
            var html = $"<!DOCTYPE html><html><body style='margin:0'><div style=\"width:200px;height:100px;{string.Format(declaration, uri)}\"></div></body></html>";

            var pdf = await GetPdfText(html);

            Assert.Contains("1 0 0 rg", pdf);
        }
    }
}
