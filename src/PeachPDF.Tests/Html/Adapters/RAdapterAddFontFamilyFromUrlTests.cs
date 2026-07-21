using PeachPDF.Adapters;
using System;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Html.Adapters
{
    /// <summary>
    /// Direct unit tests for <see cref="PeachPDF.Html.Adapters.RAdapter.AddFontFamilyFromUrl"/>'s
    /// error-handling paths - a malformed URL, and a relative URL with no base to resolve against.
    /// Both must fail gracefully (font simply doesn't load) rather than throwing and crashing the whole
    /// render, per the type's own documented contract.
    /// </summary>
    public class RAdapterAddFontFamilyFromUrlTests
    {
        [Fact]
        public async Task MalformedUrl_DoesNotThrow()
        {
            var adapter = new PdfSharpAdapter();

            // A control character makes this an invalid URI even under UriKind.RelativeOrAbsolute.
            await adapter.AddFontFamilyFromUrl("TestFont", "http://\x01invalid", null);
        }

        [Fact]
        public async Task RelativeUrl_WithNoBaseUri_DoesNotThrow()
        {
            var adapter = new PdfSharpAdapter();

            await adapter.AddFontFamilyFromUrl("TestFont", "fonts/relative.ttf", null, baseUri: null);
        }

        [Fact]
        public async Task AbsoluteUrl_MissingResource_ReturnsFalse()
        {
            var adapter = new PdfSharpAdapter();

            // An absolute file: URL that resolves but has nothing behind it: GetResourceStream returns
            // null, so the font simply doesn't load (no throw, returns false).
            var missing = new Uri(System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "peachpdf-no-such-font-" + Guid.NewGuid().ToString("N") + ".ttf"));

            var result = await adapter.AddFontFamilyFromUrl("TestFont", missing.AbsoluteUri, null);

            Assert.False(result);
        }
    }
}
