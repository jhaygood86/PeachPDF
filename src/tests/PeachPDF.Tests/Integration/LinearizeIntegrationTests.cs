using System.Text;
using PeachDrawing.PDF;

namespace PeachPDF.Tests.Integration
{
    /// <summary><see cref="PdfGenerateConfig.Linearize"/>: HTML and declarative documents written in the linearized layout.</summary>
    public class LinearizeIntegrationTests
    {
        private const string Html =
            "<html><body><h1>Title</h1>" +
            "<p><a href='https://example.com'>a link</a> and some text</p>" +
            "<div style='page-break-before:always'><h2>Second</h2><form><input type='text' name='n' value='v'></form></div>" +
            "<div style='page-break-before:always'><h2>Third</h2><p style='font-family:serif'>more</p></div></body></html>";

        private static async Task<byte[]> RenderAsync(bool linearize, bool tagged = false, bool forms = false)
        {
            var generator = new PdfGenerator();
            var document = await generator.GeneratePdf(Html, new PdfGenerateConfig
            {
                PageSize = PageSize.A5,
                Linearize = linearize,
                EnableTaggedPdf = tagged,
                EnableInteractivePdfForms = forms,
            });
            using var stream = new MemoryStream();
            document.Save(stream);
            return stream.ToArray();
        }

        [Fact]
        public async Task Linearize_IsOffByDefault_AndWritesTheLayoutWhenAsked()
        {
            Assert.DoesNotContain("/Linearized", Encoding.Latin1.GetString(await RenderAsync(linearize: false)));

            var linear = await RenderAsync(linearize: true);
            Assert.Contains("/Linearized 1", Encoding.Latin1.GetString(linear));
            Assert.Equal(3, PdfPageSource.Open(linear).PageCount);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task LinearizedFile_KeepsLinksOutlineAndPages(bool tagged, bool forms)
        {
            var plain = PdfPageSource.Open(await RenderAsync(linearize: false, tagged, forms));
            var linear = PdfPageSource.Open(await RenderAsync(linearize: true, tagged, forms));

            Assert.Equal(plain.PageCount, linear.PageCount);
            Assert.Equal(plain.GetLinks(0).Select(l => l.Uri), linear.GetLinks(0).Select(l => l.Uri));
            Assert.Equal(plain.GetOutline().Select(o => (o.Title, o.PageIndex)), linear.GetOutline().Select(o => (o.Title, o.PageIndex)));
            Assert.Equal(plain.GetFormFields(1).Count, linear.GetFormFields(1).Count);
        }

        [Fact]
        public async Task Linearize_AppliesToADeclarativeDocument()
        {
            var generator = new PdfGenerator();
            var document = await generator.CreateDocument(
                doc =>
                {
                    doc.Page(page => page.Content(c => c.Text("one")));
                    doc.Page(page => page.Content(c => c.Text("two")));
                },
                new PdfGenerateConfig { PageSize = PageSize.A5, Linearize = true });
            using var stream = new MemoryStream();
            document.Save(stream);

            Assert.Contains("/Linearized 1", Encoding.Latin1.GetString(stream.ToArray()));
            Assert.Equal(2, PdfPageSource.Open(stream.ToArray()).PageCount);
        }
    }
}
