using PeachPDF;
using PeachPDF.Tests.TestSupport;
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

        [Theory]
        [InlineData("<div dir='rtl'><table width='300'><tr><td>t1</td><td>t2</td></tr></table></div>")]
        [InlineData("<div dir='rtl'><table width='300' style='border-collapse:collapse' border='1'><thead><tr><th colspan='2'>H</th></tr></thead><tr><td rowspan='2'>1</td><td>2</td></tr><tr><td>3</td></tr></table></div>")]
        [InlineData("<table style='min-width:300pt;margin:0 auto'><tr><td>narrow</td></tr></table><table style='min-width:300pt;table-layout:fixed'><tr><td>fixed</td></tr></table>")]
        [InlineData("<center><table><tr><td>a</td></tr></table></center><table width='500'><tr><td align='center'><table><tr><td>b</td></tr></table></td></tr></table><div style='text-align:center'><table><tr><td>c</td></tr></table></div>")]
        [InlineData("<div style='float:left;width:300pt;height:50pt'></div><table width='500'><tr><td>below</td></tr></table><div style='float:right;width:300pt;height:50pt'></div><table width='500'><tr><td>below</td></tr></table>")]
        public async Task TableShapesOfTheSecondBatch_Render(string body)
        {
            var result = await new PdfGenerator().GeneratePdf("<!DOCTYPE html><html><body>" + body + "</body></html>", PageSize.A4);

            Assert.NotEmpty(result.Inspect().ContentText(result.Inspect().Pages[0]));
        }

        // A table with a repeating header and a float that fits no band, in containers whose break tokens the driver compares: each finishes
        // in a handful of pages instead of running to the pass cap.
        [Theory]
        [InlineData("<div style='display:flex'>")]
        [InlineData("<div style='display:flex;flex-direction:column'>")]
        [InlineData("<div style='display:grid'>")]
        [InlineData("<div style='position:absolute;left:0;top:0;width:250pt'>")]
        [InlineData("<div>")]
        public async Task AStalledTableInAnyContainer_FinishesInAFewPages(string open)
        {
            var generator = new PdfGenerator();
            await using (var stream = File.OpenRead(BundledFonts.LiberationSans))
                await generator.AddFontFromStream(stream);
            var family = TypefaceFixtures.FamilyNameOf(BundledFonts.LiberationSans);
            var html = "<!doctype html><html><head><style>@page{size:300pt 160pt;margin:20pt}body{margin:0;font:12pt '" + family + "'}th{padding:0;line-height:20pt}table{border-spacing:0}</style></head><body>" +
                       open + "<table><thead><tr><th>w1</th></tr></thead><tbody><tr><td><div style='float:right;width:60%;margin:0 0 6pt 0;height:86pt'></div>w2</td></tr></tbody></table></div></body></html>";

            var render = Task.Run(() => generator.GeneratePdf(html, PageSize.A4));
            var finished = await Task.WhenAny(render, Task.Delay(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken));

            Assert.True(ReferenceEquals(render, finished), "the render had not finished after 60s");
            Assert.InRange((await render).Inspect().Pages.Count, 1, 5);
        }
    }
}
