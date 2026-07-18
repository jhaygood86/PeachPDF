using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Coverage for the HTML &lt;object&gt; "replacement algorithm" subset PeachPDF implements
    /// (<see cref="CssBoxObject"/>): a supported image `data` resolves to replaced content (exactly
    /// like &lt;img&gt;) and discards fallback children; anything else falls back to rendering the
    /// element's DOM children normally, recursively. This is the mechanism the real Acid2 test's
    /// nested-object "eyes" depend on - see Acid2RegressionTests for the full fixture.
    /// </summary>
    public class ObjectElementIntegrationTests
    {
        // A real 1x1 yellow-pixel PNG (the same fixture the Acid2 test itself uses).
        private const string ValidPngDataUri =
            "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR42mP4/58BAAT/Af9jgNErAAAAAElFTkSuQmCC";

        [Fact]
        public async Task ValidImageData_BecomesReplacedElement_DiscardsChildren()
        {
            var html = Wrap($"<object id='o' data='{ValidPngDataUri}'>fallback text</object>");
            var (root, _) = await BuildAndLayout(html);

            var obj = FindById(root, "o")!;
            Assert.Empty(obj.Boxes);
            Assert.Contains(obj.Words, w => w.IsImage);
        }

        [Fact]
        public async Task UnsupportedMimeType_FallsBackToChildren_WithoutFetching()
        {
            // "application/x-unknown" is never a recognized image type, and has no ';' segment - a
            // malformed-input case that must not throw or hang.
            var html = Wrap("<object id='o' data='data:application/x-unknown,ERROR'><span id='fallback'>fallback</span></object>");
            var (root, _) = await BuildAndLayout(html);

            var obj = FindById(root, "o")!;
            Assert.NotEmpty(obj.Boxes);
            Assert.NotNull(FindById(root, "fallback"));
        }

        [Fact]
        public async Task NonImageTypeAttribute_FailsWithoutFetch_FallsBackToChildren()
        {
            // A remote, deliberately-unreachable URL with a non-image `type` - must fail
            // deterministically without ever attempting a network fetch.
            var html = Wrap("<object id='o' data='https://example.invalid/404' type='text/html'><span id='fallback'>fallback</span></object>");
            var (root, _) = await BuildAndLayout(html);

            var obj = FindById(root, "o")!;
            Assert.NotEmpty(obj.Boxes);
            Assert.NotNull(FindById(root, "fallback"));
        }

        [Fact]
        public async Task NestedObjectFallbackChain_ResolvesToInnermostValidImage()
        {
            // Mirrors the real Acid2 "eyes" markup: outer object has an unsupported data: MIME,
            // middle object has a non-image `type` (and an unreachable data source), innermost
            // object has a valid image - only the innermost should render as replaced content.
            var html = Wrap($"""
                <object id='outer' data='data:application/x-unknown,ERROR'>
                  <object id='middle' data='https://example.invalid/404' type='text/html'>
                    <object id='inner' data='{ValidPngDataUri}'>ERROR</object>
                  </object>
                </object>
                """);
            var (root, _) = await BuildAndLayout(html);

            var outer = FindById(root, "outer")!;
            var middle = FindById(root, "middle")!;
            var inner = FindById(root, "inner")!;

            Assert.NotEmpty(outer.Boxes);
            Assert.NotEmpty(middle.Boxes);
            Assert.Empty(inner.Boxes);
            Assert.Contains(inner.Words, w => w.IsImage);
        }

        [Fact]
        public async Task NoDataAttribute_FallsBackToChildren()
        {
            var html = Wrap("<object id='o'><span id='fallback'>fallback</span></object>");
            var (root, _) = await BuildAndLayout(html);

            var obj = FindById(root, "o")!;
            Assert.NotEmpty(obj.Boxes);
            Assert.NotNull(FindById(root, "fallback"));
        }

        [Fact]
        public async Task ValidImageData_PaintsAsImageInPdf()
        {
            var html = Wrap($"<object data='{ValidPngDataUri}'>fallback text that must not render</object>");

            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            config.SetMargins(20);
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            var pdfText = Encoding.Latin1.GetString(ms.ToArray());

            Assert.Contains("/Image", pdfText);
            Assert.DoesNotContain("fallback text", pdfText);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body>{body}</body></html>";

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string html)
        {
            var adapter = new PdfSharpAdapter();
            adapter.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            var val = box.HtmlTag?.TryGetAttribute("id", "");
            if (val != null && val.Equals(id, System.StringComparison.OrdinalIgnoreCase))
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found != null) return found;
            }
            return null;
        }
    }
}
