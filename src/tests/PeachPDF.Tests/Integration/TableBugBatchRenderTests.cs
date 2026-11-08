using PeachPDF;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end renders of the shapes behind the table bug batch (DOCTYPE quirks detection, a cell's max-height, a table beside a float):
    /// the layout assertions are in PeachBrackets.Engine.Tests, these prove each document renders through the public API.
    /// </summary>
    public class TableBugBatchRenderTests
    {
        private const string Body = "<p>a<table><tr><td>cell</td></tr></table></p>";

        [Theory]
        [InlineData("<!DOCTYPE html>")]
        [InlineData("<!DOCTYPE HTML PUBLIC \"-//W3C//DTD HTML 4.01//EN\" \"http://www.w3.org/TR/html4/strict.dtd\">")]
        [InlineData("<!DOCTYPE HTML PUBLIC \"-//W3C//DTD HTML 4.01 Transitional//EN\">")]
        [InlineData("<!DOCTYPE HTML PUBLIC \"-//W3C//DTD HTML 4.01 Frameset//EN\">")]
        [InlineData("<!DOCTYPE HTML PUBLIC \"-//W3C//DTD HTML 4.01 Transitional//EN\" \"http://www.w3.org/TR/html4/loose.dtd\">")]
        [InlineData("<!DOCTYPE HTML PUBLIC \"-//IETF//DTD HTML 2.0//EN\">")]
        [InlineData("<!DOCTYPE HTML PUBLIC \"-//W3O//DTD W3 HTML Strict 3.0//EN//\">")]
        [InlineData("<!DOCTYPE HTML PUBLIC \"-/W3C/DTD HTML 4.0 Transitional/EN\">")]
        [InlineData("<!DOCTYPE HTML PUBLIC \"HTML\">")]
        [InlineData("<!DOCTYPE html SYSTEM \"http://www.ibm.com/data/dtd/v11/ibmxhtml1-transitional.dtd\">")]
        [InlineData("<!DOCTYPE foo>")]
        [InlineData("")]
        public async Task DocumentWithAnyDoctype_Renders(string doctype)
        {
            var result = await new PdfGenerator().GeneratePdf(doctype + "<html><body>" + Body + "</body></html>", PageSize.A4);

            Assert.NotEmpty(result.Inspect().ContentText(result.Inspect().Pages[0]));
        }

        [Fact]
        public async Task CellWithMaxHeightAndATableBesideAFloat_Render()
        {
            var html = "<!DOCTYPE html><html><body>" +
                "<table align='left' width='150'><tr><td>float</td></tr></table><table width='200'><tr><td>beside</td></tr></table>" +
                "<table><tr><td style='max-height:5pt;vertical-align:middle'>capped</td></tr></table></body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, PageSize.A4);

            Assert.NotEmpty(result.Inspect().ContentText(result.Inspect().Pages[0]));
        }
    }
}
