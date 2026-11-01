using PeachDrawing.PDF.Engine;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// An <c>@keyframes</c> rule inside an <c>@media</c> block is registered only when the block matches the document's medium (print).
    /// </summary>
    public class KeyframesMediaIntegrationTests
    {
        private static async Task<string> GetPdfText(string keyframes)
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false, AnimationProgress = 1.0 };
            var doc = await generator.GeneratePdf(
                "<!DOCTYPE html><html><head><style>body { margin: 0; } " + keyframes +
                " #a { width: 50pt; height: 50pt; background: #00f; animation: k 1s linear both; }</style></head><body><div id='a'></div></body></html>", config);
            var ms = new MemoryStream();
            doc.Save(ms);
            var text = Encoding.Latin1.GetString(ms.ToArray());
            text = Regex.Replace(text, @"\(D:[^)]*\)", "");
            return text[..text.LastIndexOf("trailer", System.StringComparison.Ordinal)];
        }

        private const string Frames = "@keyframes k { from { background: #00f } to { background: #f00 } }";

        [Fact]
        public async Task KeyframesInAMatchingMedia_AreUsed()
        {
            var plain = await GetPdfText(Frames);

            Assert.Equal(plain, await GetPdfText("@media print { " + Frames + " }"));
            Assert.Equal(plain, await GetPdfText("@media all { " + Frames + " }"));
        }

        [Theory]
        [InlineData("@media screen")]
        [InlineData("@media not print")]
        [InlineData("@media (min-width: 5000px)")]
        public async Task KeyframesInANonMatchingMedia_AreIgnored(string media)
        {
            var plain = await GetPdfText(Frames);
            var ignored = await GetPdfText(media + " { " + Frames + " }");
            var none = await GetPdfText("");

            Assert.NotEqual(plain, ignored);
            Assert.Equal(none, ignored);
        }

        [Fact]
        public async Task ANonMatchingMediaKeyframes_DoNotReplaceAnEarlierMatchingOne()
        {
            var plain = await GetPdfText(Frames);
            var withScreenOverride = await GetPdfText(Frames + " @media screen { @keyframes k { to { background: #0f0 } } }");

            Assert.Equal(plain, withScreenOverride);
        }
    }
}
