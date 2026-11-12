using PeachBrackets.Html;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;

namespace PeachBrackets.Engine.Tests.Html.Core
{
    /// <summary>
    /// Before the first layout pass a render context that can prefetch (<see cref="IResourcePrefetcher"/>) is told the addresses of the images the document
    /// is about to load, once each, and not those of boxes that are not displayed.
    /// </summary>
    public class ImagePrefetchTests
    {
        private sealed class PrefetchingContext : PdfRenderContext, IResourcePrefetcher
        {
            public override Uri? BaseUri => new("https://example.test/dir/");

            public List<IReadOnlyList<Uri>> Calls { get; } = [];

            public ValueTask PrefetchAsync(IReadOnlyList<Uri> addresses)
            {
                Calls.Add(addresses);
                return default;
            }
        }

        private static async Task<PrefetchingContext> LayOutAsync(string body, string head = "")
        {
            var context = new PrefetchingContext();
            var container = new HtmlContainerInt(context) { MediaType = MediaType.Screen };
            await container.SetHtml($"<!DOCTYPE html><html><head>{head}</head><body>{body}</body></html>", null);

            var size = new Size(595, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            using var graphics = PdfCanvas.CreateMeasuring(context, size);
            await container.PerformLayout(graphics);
            return context;
        }

        [Fact]
        public async Task TheImagesTheDocumentLoads_AreHandedOver_ResolvedAgainstTheBase_AndNotTheHiddenOnes()
        {
            var context = await LayOutAsync("<img src='a.png'><img src='/b.png'><img src='a.png'><div style='display:none'><img src='hidden.png'></div>");

            var call = Assert.Single(context.Calls);
            Assert.Equal(["https://example.test/dir/a.png", "https://example.test/b.png", "https://example.test/dir/a.png"], [.. call.Select(a => a.AbsoluteUri)]);
        }

        [Fact]
        public async Task TheFirstWebFontOfEachFace_IsHandedOverBeforeTheyAreRegistered()
        {
            var context = await LayOutAsync("<p>text</p>",
                "<style>@font-face { font-family: A; src: url(https://fonts.test/a.woff2) } @font-face { font-family: B; src: local(Nope), url(https://fonts.test/b.woff2), url(https://fonts.test/b.woff) } @font-face { font-family: C; src: url(relative.woff2) }</style>");

            var call = Assert.Single(context.Calls);
            Assert.Equal(["https://fonts.test/a.woff2", "https://fonts.test/b.woff2"], [.. call.Select(a => a.AbsoluteUri)]);
        }

        [Fact]
        public async Task AnImageWithNoSource_IsNotHandedOver()
        {
            // Lazy-load markup has no src: resolving an empty one against the base would fetch the page itself.
            var context = await LayOutAsync("<img data-src='x.png'><img src=''><img src='  '><img src='a.png'><img src='b.png'>");

            var call = Assert.Single(context.Calls);
            Assert.Equal(["https://example.test/dir/a.png", "https://example.test/dir/b.png"], [.. call.Select(a => a.AbsoluteUri)]);
        }

        [Fact]
        public async Task TheLinkedStyleSheets_AreHandedOverTogether_InDocumentOrder()
        {
            var context = await LayOutAsync("<p>text</p>",
                "<link rel='stylesheet' href='a.css'><link rel='preload' href='skip.css'><link rel='alternate stylesheet' href='/b.css'><link rel='icon' href='i.png'><link rel='stylesheet' href=''>");

            var call = Assert.Single(context.Calls);
            Assert.Equal(["https://example.test/dir/a.css", "https://example.test/b.css"], [.. call.Select(a => a.AbsoluteUri)]);
        }

        [Fact]
        public async Task ASingleLinkedStyleSheet_IsNotWorthAHandOver()
        {
            var context = await LayOutAsync("<p>text</p>", "<link rel='stylesheet' href='only.css'>");

            Assert.Empty(context.Calls);
        }

        [Fact]
        public async Task ASingleImage_IsNotWorthAHandOver()
        {
            var context = await LayOutAsync("<img src='only.png'>");

            Assert.Empty(context.Calls);
        }
    }
}
