using PeachPDF.Network;
using PeachPDF.Tests.TestSupport;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression tests for relative <c>url()</c> resolution inside fetched (non-inline) stylesheets —
    /// both a directly linked stylesheet and one reached via <c>@import</c>. Before this fix,
    /// <c>RAdapter.AddFontFamilyFromUrl</c> constructed a bare <c>new RUri(url)</c> from a relative
    /// <c>@font-face src</c> with no base at all, which throws <see cref="UriFormatException"/> and
    /// crashes the whole render — the exact scenario a real page (css4.pub's Icelandic dictionary,
    /// whose custom fonts are declared inside an <c>@import</c>ed stylesheet) hits in practice.
    /// </summary>
    public class StylesheetRelativeUrlResolutionTests
    {
        private static readonly RUri DocumentUri = new("https://example.test/docs/page.html");

        [Fact]
        public async Task FontFaceInLinkedStylesheet_RelativeUrl_ResolvesAgainstStylesheetLocation()
        {
            var loader = new InMemoryNetworkLoader(DocumentUri, primaryHtml: $@"<!DOCTYPE html>
<html><head>
<link rel=""stylesheet"" href=""css/sheet.css"">
</head>
<body>Hello Linked</body>
</html>");

            loader.AddTextResource(
                "https://example.test/docs/css/sheet.css",
                @"@font-face { font-family: 'LinkedFont'; src: url('sub/font.ttf') format('truetype'); }
body { font-family: 'LinkedFont', serif; font-size: 14pt; }",
                "text/css");

            // Registered at css/sub/font.ttf — i.e. resolved against the *stylesheet's* own location
            // (css/sheet.css), not the document's location. This is the assertion that would have
            // thrown UriFormatException before the fix.
            loader.AddResource(
                "https://example.test/docs/css/sub/font.ttf",
                File.ReadAllBytes(BundledFonts.Ttf),
                "font/ttf");

            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { NetworkLoader = loader, PageSize = PageSize.A4 };

            var doc = await generator.GeneratePdf(null, config);

            Assert.NotNull(doc);
            Assert.True(doc.PageCount >= 1);
            Assert.Contains("/FontFile2", GetPdfText(doc));
        }

        [Fact]
        public async Task FontFaceInsideImportedStylesheet_RelativeUrl_ResolvesAgainstImportedStylesheetLocation()
        {
            // Mirrors css4.pub/2015/icelandic/dictionary.html: an inline <style> block @imports an
            // external stylesheet, and that imported stylesheet's own @font-face uses a URL relative
            // to *itself*, not the document.
            var loader = new InMemoryNetworkLoader(DocumentUri, primaryHtml: $@"<!DOCTYPE html>
<html><head><style>
@import url(css/sheet.css);
body {{ font-family: 'ImportedFont', serif; font-size: 14pt; }}
</style></head>
<body>Hello Imported</body>
</html>");

            loader.AddTextResource(
                "https://example.test/docs/css/sheet.css",
                @"@font-face { font-family: 'ImportedFont'; src: url('sub/font.ttf') format('truetype'); }",
                "text/css");

            loader.AddResource(
                "https://example.test/docs/css/sub/font.ttf",
                File.ReadAllBytes(BundledFonts.Ttf),
                "font/ttf");

            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { NetworkLoader = loader, PageSize = PageSize.A4 };

            var doc = await generator.GeneratePdf(null, config);

            Assert.NotNull(doc);
            Assert.True(doc.PageCount >= 1);
            Assert.Contains("/FontFile2", GetPdfText(doc));
        }

        [Fact]
        public async Task CircularImport_DoesNotHang_AndStillRendersRemainingStyles()
        {
            var loader = new InMemoryNetworkLoader(DocumentUri, primaryHtml: $@"<!DOCTYPE html>
<html><head><style>
@import url(a.css);
body {{ font-size: 20pt; }}
</style></head>
<body>Hello Circular</body>
</html>");

            loader.AddTextResource("https://example.test/docs/a.css", "@import url(b.css); .a { color: red; }", "text/css");
            loader.AddTextResource("https://example.test/docs/b.css", "@import url(a.css); .b { color: blue; }", "text/css");

            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { NetworkLoader = loader, PageSize = PageSize.A4 };

            var generateTask = generator.GeneratePdf(null, config);
            var completed = await Task.WhenAny(generateTask, Task.Delay(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken));

            Assert.Same(generateTask, completed);
            var doc = await generateTask;
            Assert.NotNull(doc);
            Assert.True(doc.PageCount >= 1);
        }

        [Fact]
        public async Task LinkedStylesheet_MissingResource_DoesNotThrow_AndStillRenders()
        {
            // StylesheetLoadHandler.LoadStylesheet's networkResponse is null (resource never registered) -
            // isInvalidNetworkResponse stays true and the handler returns an empty stylesheet rather than
            // throwing, so the rest of the document still renders.
            var loader = new InMemoryNetworkLoader(DocumentUri, primaryHtml: $@"<!DOCTYPE html>
<html><head>
<link rel=""stylesheet"" href=""css/missing.css"">
</head>
<body>Hello Missing</body>
</html>");

            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { NetworkLoader = loader, PageSize = PageSize.A4 };

            var doc = await generator.GeneratePdf(null, config);

            Assert.NotNull(doc);
            Assert.True(doc.PageCount >= 1);
        }

        [Fact]
        public async Task LinkedStylesheet_WrongContentType_IsTreatedAsInvalid_AndStillRenders()
        {
            // A resource does exist at the URL, but with a non-CSS content-type - the same
            // isInvalidNetworkResponse path as a missing resource, exercised via the opposite cause.
            var loader = new InMemoryNetworkLoader(DocumentUri, primaryHtml: $@"<!DOCTYPE html>
<html><head>
<link rel=""stylesheet"" href=""css/notcss.css"">
</head>
<body>Hello WrongType</body>
</html>");

            loader.AddTextResource("https://example.test/docs/css/notcss.css", "<html>not css</html>", "text/html");

            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { NetworkLoader = loader, PageSize = PageSize.A4 };

            var doc = await generator.GeneratePdf(null, config);

            Assert.NotNull(doc);
            Assert.True(doc.PageCount >= 1);
        }

        [Fact]
        public async Task LinkedStylesheet_ResolvesAgainstBaseHref_NotDocumentLocation()
        {
            // Regression coverage for the <base href> resolution branch in
            // StylesheetLoadHandler.LoadStylesheet, only reachable for a top-level (non-@import) load.
            var loader = new InMemoryNetworkLoader(DocumentUri, primaryHtml: $@"<!DOCTYPE html>
<html><head>
<base href=""https://example.test/other/"">
<link rel=""stylesheet"" href=""sheet.css"">
</head>
<body>Hello Base</body>
</html>");

            loader.AddTextResource("https://example.test/other/sheet.css", "body { font-size: 14pt; }", "text/css");

            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { NetworkLoader = loader, PageSize = PageSize.A4 };

            var doc = await generator.GeneratePdf(null, config);

            Assert.NotNull(doc);
            Assert.True(doc.PageCount >= 1);
        }

        private static string GetPdfText(PeachPdfDocument doc)
        {
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }
    }
}
