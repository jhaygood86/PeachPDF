using PeachBrackets;
using PeachDrawing.PDF;

namespace PeachPDF.Tests.Network
{
    /// <summary>A render context that can prefetch is told which images, style sheets and fonts a document is about to load.</summary>
    public class ResourcePrefetchTests
    {
        private sealed class PrefetchingContext : PdfRenderContext, IResourcePrefetcher
        {
            public override Uri? BaseUri => new("https://example.test/dir/");

            public List<string> Addresses { get; } = [];

            public ValueTask PrefetchAsync(IReadOnlyList<Uri> addresses)
            {
                Addresses.AddRange(addresses.Select(a => a.AbsoluteUri));
                return default;
            }
        }

        private static async Task<PrefetchingContext> LoadAsync(string body, string head = "")
        {
            var context = new PrefetchingContext();
            var options = new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 300, ViewportHeightPoints = 100 };
            using var document = await HtmlDocument.LoadAsync($"<!DOCTYPE html><html><head>{head}</head><body>{body}</body></html>", context, options);
            using var measuring = PdfCanvas.CreateMeasuring(context, new PeachDrawing.Core.Size(300, 400));
            await document.LayoutAsync(measuring);
            return context;
        }

        [Fact]
        public async Task Images_AreHandedOver_ResolvedAgainstTheBase()
        {
            var context = await LoadAsync("<img src='a.png'><img src='/b.png'>");

            Assert.Contains("https://example.test/dir/a.png", context.Addresses);
            Assert.Contains("https://example.test/b.png", context.Addresses);
        }

        [Fact]
        public async Task StyleSheetsAndWebFonts_AreHandedOver()
        {
            var context = await LoadAsync("<p>text</p>",
                "<link rel='stylesheet' href='a.css'><link rel='stylesheet' href='b.css'>" +
                "<style>@font-face{font-family:A;src:url(https://fonts.test/a.woff2)} @font-face{font-family:B;src:url(https://fonts.test/b.woff2)}</style>");

            Assert.Contains("https://example.test/dir/a.css", context.Addresses);
            Assert.Contains("https://example.test/dir/b.css", context.Addresses);
            Assert.Contains("https://fonts.test/a.woff2", context.Addresses);
        }
    }
}
