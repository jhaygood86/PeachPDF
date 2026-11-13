using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A table cell that spans rows past the last one, in a table with a repeating header, holding a float that fits no band: no pass places
    /// it. The driver notices a run that gets nowhere by comparing a pass's outgoing record with the one it was handed, and the table's record
    /// carries the row-span bookkeeping, in which a cell placed again on each pass was listed once more every time: the records were never equal
    /// and the render ran for minutes instead of falling back to laying the rest out in one piece. The same rules are asserted on the pass
    /// counts in <c>PeachBrackets.Engine.Tests</c> (<c>StalledPaginationTests</c>); this runs through <see cref="PdfGenerator"/>, the whole pipeline a
    /// caller uses.
    /// </summary>
    public class RowSpanStallPdfTests
    {
        private const string Document =
            "<!doctype html><html><head><style>@page{size:300pt 160pt;margin:20pt}body{margin:0;font:12pt '{0}'}th{padding:0;line-height:20pt}table{border-spacing:0}</style></head><body>" +
            "{open}<table><thead><tr><th>w1</th></tr></thead><tbody><tr><td rowspan='4'><table><tbody><tr><td rowspan='3'>" +
            "<div style='float:right;width:60%;margin:0 0 6pt 0;height:86pt'></div>w2</td></tr></tbody></table></td></tr></tbody></table>{close}</body></html>";

        // The table alone, and inside the containers whose break tokens the driver compares as well: flex (row and column) and grid.
        [Theory]
        [InlineData("", "")]
        [InlineData("<div style='display:flex'>", "</div>")]
        [InlineData("<div style='display:flex;flex-direction:column'>", "</div>")]
        [InlineData("<div style='display:grid'>", "</div>")]
        public async Task AStalledRowSpanTable_FinishesInsteadOfRunningToThePassCap(string open, string close)
        {
            var generator = new PdfGenerator();
            await using (var stream = File.OpenRead(BundledFonts.LiberationSans))
                await generator.AddFontFromStream(stream);
            var html = Document.Replace("{0}", TypefaceFixtures.FamilyNameOf(BundledFonts.LiberationSans)).Replace("{open}", open).Replace("{close}", close);

            // Run off the test's own thread so that a regression fails here, after a minute, rather than hanging the run.
            var render = Task.Run(() => generator.GeneratePdf(html, PageSize.A4));
            var finished = await Task.WhenAny(render, Task.Delay(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken));

            Assert.True(ReferenceEquals(render, finished), "the render had not finished after 60s: the stalled passes were not recognised");

            var file = (await render).Inspect();

            // The recovery lays the remainder out in one piece after a bounded number of stalled passes, each of which was a page.
            Assert.InRange(file.Pages.Count, 1, 40);
        }
    }
}
