using PeachPDF.Rendering;
using PeachDrawing.PDF;
using System;
using System.Threading.Tasks;

namespace PeachPDF.Tests.PdfBackendTests
{
    /// <summary>
    /// Direct unit tests for <see cref="PeachDrawing.Core.RenderContext.AddFontFamilyFromUrl"/>'s
    /// error-handling paths - a malformed URL, and a relative URL with no base to resolve against.
    /// Both must fail gracefully (font simply doesn't load) rather than throwing and crashing the whole
    /// render, per the type's own documented contract.
    /// </summary>
    public class RenderContextAddFontFamilyFromUrlTests
    {
        [Fact]
        public async Task MalformedUrl_DoesNotThrow()
        {
            var renderContext = new HtmlRenderContext();

            // A control character makes this an invalid URI even under UriKind.RelativeOrAbsolute.
            await renderContext.AddFontFamilyFromUrl("TestFont", "http://\x01invalid", null);
        }

        [Fact]
        public async Task RelativeUrl_WithNoBaseUri_DoesNotThrow()
        {
            var renderContext = new HtmlRenderContext();

            await renderContext.AddFontFamilyFromUrl("TestFont", "fonts/relative.ttf", null, baseUri: null);
        }

        [Fact]
        public async Task AbsoluteUrl_MissingResource_ReturnsFalse()
        {
            var renderContext = new HtmlRenderContext();

            // An absolute file: URL that resolves but has nothing behind it: GetResourceStream returns
            // null, so the font simply doesn't load (no throw, returns false).
            var missing = new Uri(System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "peachpdf-no-such-font-" + Guid.NewGuid().ToString("N") + ".ttf"));

            var result = await renderContext.AddFontFamilyFromUrl("TestFont", missing.AbsoluteUri, null);

            Assert.False(result);
        }
    }
}
