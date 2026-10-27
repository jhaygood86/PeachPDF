using PeachPDF;
using PeachBrackets.Html.Fragments;
using PeachBrackets.Engine.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// A row of a table nested in another table's cell that declares <c>break-inside: avoid</c> is moved
    /// whole to the next page rather than split between its lines (css-break-3 §3.2).
    /// </summary>
    public class NestedTableAvoidedRowBreakTests
    {
        private static string Document() =>
            "<!doctype html><html><head><meta charset=\"utf-8\"><style>@page{size:300pt 300pt;margin:20pt}" +
            "body{margin:0;font:10pt Arial}table{width:100%;border-collapse:collapse}td,th{border:1px solid #999;padding:2pt}" +
            "table table tr{break-inside:avoid}</style></head><body><table><thead><tr><th>HDR0</th><th>HDR1</th></tr></thead><tbody>" +
            "<tr><td>R001L01</td><td>R001L02 R001L03</td></tr><tr><td>R002L01</td><td>R002L02 R002L03</td></tr>" +
            "<tr><td>R003L01</td><td>R003L02 R003L03</td></tr><tr><td>R010L01<table>" +
            "<tr><td>R010P01</td><td>R010Q01</td></tr><tr><td>R010P02</td><td>R010Q02</td></tr><tr><td>R010P03</td><td>R010Q03</td></tr>" +
            "<tr><td>R010L04</td><td>" + string.Join("<br>", Enumerable.Range(5, 18).Select(i => $"R010L{i:00}")) + "</td></tr>" +
            "</table></td><td>x</td></tr><tr><td>R021L01</td><td>R021L02 R021L03</td></tr></tbody></table></body></html>";

        [Fact]
        public async Task TheAvoidedNestedRow_IsDrawnWholeOnOnePage()
        {
            var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
                Document(), new PdfGenerateConfig { PageSize = PageSize.Letter });

            var pages = container.FragmentTree!.Fragmentainers;
            var pageOf = new Dictionary<string, List<int>>();

            for (var page = 0; page < pages.Count; page++)
            {
                foreach (var word in Flatten(pages[page].Root).SelectMany(f => f.Words).Select(w => w.Word.Text ?? string.Empty))
                {
                    if (!word.StartsWith("R010L") || word is "R010L01") continue;

                    if (!pageOf.TryGetValue(word, out var list)) pageOf[word] = list = [];
                    list.Add(page);
                }
            }

            Assert.Equal(19, pageOf.Count);
            Assert.All(pageOf, kv => Assert.Single(kv.Value));
            Assert.Single(pageOf.Values.Select(v => v[0]).Distinct());
        }

        private static IEnumerable<BoxFragment> Flatten(BoxFragment fragment)
        {
            yield return fragment;

            foreach (var child in fragment.Children)
            {
                foreach (var descendant in Flatten(child))
                {
                    yield return descendant;
                }
            }
        }
    }
}
