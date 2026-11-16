using PeachPDF.Network;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <see cref="PdfGenerateConfig.DocumentSyntax"/>: a document the loader says is <c>application/xhtml+xml</c> is parsed with an XML parser, one that
    /// is not well-formed XML is an error, and a string handed over directly is HTML unless the config says otherwise.
    /// </summary>
    public class DocumentSyntaxTests
    {
        private const string Ns = "http://www.w3.org/1999/xhtml";

        // Well-formed XHTML with an entity only XHTML's DTD knows.
        private static string Xhtml(string body = "<p id=\"t\">a&nbsp;b</p>") =>
            $"<?xml version=\"1.0\"?><!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Strict//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-strict.dtd\">"
            + $"<html xmlns=\"{Ns}\"><head><title>x</title></head><body>{body}</body></html>";

        // The same page as tag soup: fine for the HTML parser, an error for an XML one.
        private const string Soup = "<html><body><p>one<p>two<br>three</body></html>";

        private static string TempFile(string extension, string content)
        {
            var path = Path.Combine(Path.GetTempPath(), $"peach-syntax-{Guid.NewGuid():N}{extension}");
            File.WriteAllText(path, content);
            return path;
        }

        private static async Task<int> PagesAsync(string path, PdfDocumentSyntax syntax = PdfDocumentSyntax.Auto)
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, NetworkLoader = new FileUriNetworkLoader(path), DocumentSyntax = syntax };
            var result = await new PdfGenerator().GeneratePdf(null, config);
            return result.Inspect().PageCount;
        }

        [Theory]
        [InlineData(".xhtml")]
        [InlineData(".XHT")]
        [InlineData(".xht")]
        public void TheFileLoaderReportsXhtmlForAnXhtmlFile(string extension)
        {
            var path = TempFile(extension, Xhtml());
            try
            {
                Assert.Equal("application/xhtml+xml", new FileUriNetworkLoader(path).PrimaryContentType);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void TheFileLoaderReportsHtmlForAnHtmlFileAndNothingForNoFile()
        {
            Assert.Equal("text/html", new FileUriNetworkLoader("x.html").PrimaryContentType);
            Assert.Null(new FileUriNetworkLoader().PrimaryContentType);
        }

        [Theory]
        [InlineData("application/xhtml+xml", true)]
        [InlineData("Application/XHTML+XML; charset=utf-8", true)]
        [InlineData(" application/xhtml+xml ;q=1", true)]
        [InlineData("text/html", false)]
        [InlineData("application/xml", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void OnlyApplicationXhtmlXmlIsXhtml(string? contentType, bool expected) =>
            Assert.Equal(expected, PdfGenerator.IsXhtmlContentType(contentType));

        [Fact]
        public async Task AnXhtmlFileIsParsedAsXml()
        {
            var path = TempFile(".xhtml", Xhtml());
            try
            {
                Assert.Equal(1, await PagesAsync(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task AnXhtmlFileThatIsNotWellFormedIsAnError()
        {
            var path = TempFile(".xhtml", Soup);
            try
            {
                var error = await Assert.ThrowsAsync<HtmlRenderException>(() => PagesAsync(path));

                Assert.Equal(HtmlRenderErrorType.HtmlParsing, error.RenderErrorType);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task TheSameTextInAnHtmlFileIsParsedAsHtml()
        {
            var path = TempFile(".html", Soup);
            try
            {
                Assert.Equal(1, await PagesAsync(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task ExplicitHtmlParsesAnXhtmlFileAsHtml()
        {
            var path = TempFile(".xhtml", Soup);
            try
            {
                Assert.Equal(1, await PagesAsync(path, PdfDocumentSyntax.Html));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task ExplicitXmlParsesAnHtmlFileAsXml()
        {
            var path = TempFile(".html", Soup);
            try
            {
                await Assert.ThrowsAsync<HtmlRenderException>(() => PagesAsync(path, PdfDocumentSyntax.Xml));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task AStringIsHtmlUnlessTheConfigSaysXml()
        {
            var generator = new PdfGenerator();

            // As HTML, the string is fine; as XML it is not well-formed.
            await generator.GeneratePdf(Soup, PageSize.A4);
            await Assert.ThrowsAsync<HtmlRenderException>(() =>
                generator.GeneratePdf(Soup, new PdfGenerateConfig { PageSize = PageSize.A4, DocumentSyntax = PdfDocumentSyntax.Xml }));

            var result = await generator.GeneratePdf(Xhtml(), new PdfGenerateConfig { PageSize = PageSize.A4, DocumentSyntax = PdfDocumentSyntax.Xml });
            Assert.Equal(1, result.Inspect().PageCount);
        }

        [Fact]
        public async Task AnXhtmlStringWithAnXmlDeclarationIsStillHtmlWithoutTheOption()
        {
            // The syntax is never guessed from the text: this one is well-formed, and HTML takes it too (the declaration is a bogus comment).
            var result = await new PdfGenerator().GeneratePdf(Xhtml(), PageSize.A4);

            Assert.Equal(1, result.Inspect().PageCount);
        }

        [Fact]
        public void TheMimeTypeMapKnowsXhtml()
        {
            Assert.Equal("application/xhtml+xml", MimeTypeResolver.TryGetFromStaticMap("xhtml"));
            Assert.Equal("application/xhtml+xml", MimeTypeResolver.TryGetFromStaticMap("xht"));
        }
    }
}
