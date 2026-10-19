using System.IO;
using System.Threading.Tasks;

namespace PeachPDF.Tests.PdfEngineTests
{
    /// <summary>The generator's output for the simplest document: it has a page and it saves.</summary>
    public class PdfGeneratorSimplePageTests
    {
        [Fact]
        public async Task GeneratePdf_SimpleHtml_ProducesAtLeastOnePage()
        {
            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf("<p>Hello</p>", PageSize.A4);
            Assert.NotNull(doc);
            Assert.True(doc.PageCount >= 1);
        }

        [Fact]
        public async Task GeneratePdf_SimpleHtml_CanBeSaved()
        {
            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf("<p>Hello</p>", PageSize.A4);
            var ms = new MemoryStream();
            var ex = Record.Exception(() => doc.Save(ms));
            Assert.Null(ex);
            Assert.True(ms.Length > 0);
        }
    }
}
