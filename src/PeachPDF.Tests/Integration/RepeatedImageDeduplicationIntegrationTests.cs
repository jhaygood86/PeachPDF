using PeachPDF.Network;
using PeachPDF.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    // Regression coverage for issue #1172: repeated <img>/background-image/<object> references to the
    // same resolved source used to decode and embed a separate PDF Image XObject each time, contradicting
    // docs/architecture.md's own claim that a decoded image is cached for the lifetime of a render.
    public class RepeatedImageDeduplicationIntegrationTests
    {
        // Plain truecolor (no alpha channel, no tRNS), non-interlaced: always embeds as exactly one
        // /Subtype /Image object, regardless of pass-through eligibility - no SMask to complicate the count.
        private static string OpaquePngDataUri(byte r, byte g, byte b) =>
            "data:image/png;base64," + Convert.ToBase64String(RasterPngFixture.MakeInterlacedPngBytes(20, 20, r, g, b, interlace: false));

        private static async Task<string> GetPdfText(string html, bool compress = false)
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = compress };
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0, index = 0;
            while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }
            return count;
        }

        [Fact]
        public async Task TwoImgTags_SameDataUriAtSameSize_EmbedOnlyOneImageXObject()
        {
            var dataUri = OpaquePngDataUri(255, 0, 0);
            var html = $"""
                <!DOCTYPE html><html><body>
                <p><img src="{dataUri}" width="160" height="160"></p>
                <p><img src="{dataUri}" width="160" height="160"></p>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Equal(1, CountOccurrences(pdfText, "/Subtype /Image"));
        }

        [Fact]
        public async Task TwoImgTags_DifferentDataUris_EmbedTwoDistinctImageXObjects()
        {
            var html = $"""
                <!DOCTYPE html><html><body>
                <p><img src="{OpaquePngDataUri(255, 0, 0)}" width="160" height="160"></p>
                <p><img src="{OpaquePngDataUri(0, 0, 255)}" width="160" height="160"></p>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Equal(2, CountOccurrences(pdfText, "/Subtype /Image"));
        }

        [Fact]
        public async Task ImgAndBackgroundImage_SameDataUri_ShareOneImageXObject()
        {
            var dataUri = OpaquePngDataUri(0, 255, 0);
            // background-repeat: no-repeat, so the background draw never forces Interpolate off (see
            // SameXImageAndSize_DrawnWithDifferentInterpolate_EmbedsSeparateCopies for that case) - both
            // draws stay at the default Interpolate, keeping this test focused on RImage/XImage identity.
            var html = $$"""
                <!DOCTYPE html><html><head><style>
                .box { width: 160px; height: 160px; background-image: url({{dataUri}}); background-repeat: no-repeat; }
                </style></head><body>
                <div class="box"></div>
                <img src="{{dataUri}}" width="160" height="160">
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Equal(1, CountOccurrences(pdfText, "/Subtype /Image"));
        }

        // A minimal RNetworkLoader wrapping an in-memory URL -> bytes map, counting each distinct URI's
        // GetResourceStream calls - proves the resolved-source cache skips the fetch itself on a repeat
        // reference, not just that the PDF happens to dedup the resulting XObject.
        private sealed class CountingNetworkLoader(RUri baseUri, string primaryHtml) : RNetworkLoader
        {
            private readonly Dictionary<string, (byte[] Bytes, string ContentType)> _resources = new();

            public readonly Dictionary<string, int> RequestCounts = new();

            public override RUri? BaseUri { get; } = baseUri;

            public void AddResource(string absoluteUrl, byte[] bytes, string contentType) =>
                _resources[absoluteUrl] = (bytes, contentType);

            public override Task<string> GetPrimaryContents() => Task.FromResult(primaryHtml);

            public override Task<RNetworkResponse?> GetResourceStream(RUri uri)
            {
                RequestCounts[uri.AbsoluteUri] = RequestCounts.GetValueOrDefault(uri.AbsoluteUri) + 1;

                if (_resources.TryGetValue(uri.AbsoluteUri, out var resource))
                {
                    var headers = new Dictionary<string, string[]> { ["Content-Type"] = [resource.ContentType] };
                    return Task.FromResult<RNetworkResponse?>(new RNetworkResponse(new MemoryStream(resource.Bytes), headers));
                }

                return Task.FromResult<RNetworkResponse?>(null);
            }
        }

        [Fact]
        public async Task TwoImgTags_SameNetworkUrl_FetchesResourceOnlyOnce()
        {
            var pngBytes = RasterPngFixture.MakeInterlacedPngBytes(20, 20, 128, 64, 200, interlace: false);
            var loader = new CountingNetworkLoader(new RUri("https://example.test/page.html"), """
                <!DOCTYPE html><html><body>
                <p><img src="https://example.test/pic.png" width="160" height="160"></p>
                <p><img src="https://example.test/pic.png" width="160" height="160"></p>
                </body></html>
                """);
            loader.AddResource("https://example.test/pic.png", pngBytes, "image/png");

            var config = new PdfGenerateConfig { NetworkLoader = loader, PageSize = PageSize.A4 };
            var doc = await new PdfGenerator().GeneratePdf(null, config);
            var ms = new MemoryStream();
            doc.Save(ms);

            Assert.Equal(1, loader.RequestCounts["https://example.test/pic.png"]);
        }

        [Fact]
        public async Task ConsolidateImages_AcrossSeparateAddPdfPagesCalls_MergesToOneImageXObject()
        {
            // The render-scoped cache is per AddPdfPages call (a fresh HtmlContainerInt each time), so
            // the same source referenced across two separate calls into the same document is exactly the
            // case ConsolidateImages() exists for - a duplicate the automatic per-render cache can't see.
            var dataUri = OpaquePngDataUri(10, 20, 30);

            var html = $"""
                <!DOCTYPE html><html><body>
                <p><img src="{dataUri}" width="160" height="160"></p>
                </body></html>
                """;

            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4 };
            var doc = await generator.GeneratePdf(html, config);
            // A second, independent AddPdfPages call with the same source - the render-scoped cache is
            // per-call, so this still decodes and embeds separately without ConsolidateImages().
            await generator.AddPdfPages(doc, html, config);

            var beforeMs = new MemoryStream();
            doc.Save(beforeMs);
            var beforeText = Encoding.Latin1.GetString(beforeMs.ToArray());
            Assert.Equal(2, CountOccurrences(beforeText, "/Subtype /Image"));

            doc.ConsolidateImages();

            var afterMs = new MemoryStream();
            doc.Save(afterMs);
            var afterText = Encoding.Latin1.GetString(afterMs.ToArray());
            Assert.Equal(1, CountOccurrences(afterText, "/Subtype /Image"));
        }
    }
}
