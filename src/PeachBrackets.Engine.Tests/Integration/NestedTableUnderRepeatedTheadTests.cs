using PeachPDF;
using PeachBrackets.Html.Fragments;
using PeachBrackets.Engine.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// A table nested in another table's cell continues below the outer table's repeated
    /// <c>&lt;thead&gt;</c> on every page, not just the second (css-tables-3 §6.2: leave room for the
    /// repeated header rows).
    /// </summary>
    public class NestedTableUnderRepeatedTheadTests
    {
        private static string Document(int rows) =>
            "<!DOCTYPE html><html><head><style>@page{size:300pt 100pt;margin:20pt} " +
            "body{margin:0;font:10pt Arial} table{width:100%;border-collapse:collapse}</style></head><body>" +
            "<table><thead><tr><th>HEADER</th></tr></thead><tbody><tr><td><table>" +
            string.Concat(Enumerable.Range(0, rows).Select(i => $"<tr><td>Row {i}</td></tr>")) +
            "</table></td></tr></tbody></table></body></html>";

        [Theory]
        [InlineData(10)]
        [InlineData(50)]
        public async Task EveryPage_StartsTheInnerTableBelowTheRepeatedHeader(int rows)
        {
            var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
                Document(rows), new PdfGenerateConfig { PageSize = PageSize.Letter });

            var pages = container.FragmentTree!.Fragmentainers;
            Assert.True(pages.Count >= 3, "the document must span at least three pages to be meaningful");

            // The header-to-first-row distance is what the repeated header's room fixes, so it must be the
            // same on every page as on the first (word boxes overlap a little by design, so compare gaps).
            var gaps = new List<double>();

            for (var page = 0; page < pages.Count; page++)
            {
                var words = Flatten(pages[page].Root).SelectMany(f => f.Words)
                    .Where(w => w.Word.Text is "HEADER" or "Row").ToList();
                Assert.True(
                    words.Count(w => w.Word.Text == "HEADER") == 1,
                    $"page {page} of {pages.Count}: expected one repeated HEADER, found {words.Count(w => w.Word.Text == "HEADER")}");

                var header = words.Single(w => w.Word.Text == "HEADER").Rect;
                var firstRow = words.Where(w => w.Word.Text == "Row").OrderBy(w => w.Rect.Top).First().Rect;

                gaps.Add(firstRow.Top - header.Bottom);
            }

            Assert.All(gaps.Select((gap, page) => (gap, page)), x => Assert.True(
                Math.Abs(x.gap - gaps[0]) < 0.1,
                $"page {x.page}: header-to-first-row gap {x.gap:F2} differs from page 0's {gaps[0]:F2}"));
        }

        [Theory]
        [InlineData(10)]
        [InlineData(50)]
        public async Task EveryInnerRow_IsDrawnOnExactlyOnePage(int rows)
        {
            var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
                Document(rows), new PdfGenerateConfig { PageSize = PageSize.Letter });

            // A row the slice cuts in two is drawn on both pages it straddles.
            var numbers = container.FragmentTree!.Fragmentainers
                .SelectMany(page => Flatten(page.Root).SelectMany(f => f.Words))
                .Select(w => w.Word.Text)
                .Where(text => int.TryParse(text, out _))
                .ToList();

            Assert.Equal(Enumerable.Range(0, rows).Select(i => i.ToString()), numbers);
        }

        [Fact]
        public async Task ARowThatBeginsOnALaterPage_ContinuesBelowTheHeaderToo()
        {
            // Enough rows before the nested table that its row starts part-way down page two.
            var html = Document(30).Replace(
                "<tbody><tr><td><table>",
                "<tbody>" + string.Concat(Enumerable.Range(0, 7).Select(i => $"<tr><td>Lead {i}</td></tr>")) + "<tr><td><table>");

            var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
                html, new PdfGenerateConfig { PageSize = PageSize.Letter });

            var pages = container.FragmentTree!.Fragmentainers;
            Assert.True(pages.Count >= 4);

            var gaps = new List<double>();

            foreach (var page in pages)
            {
                var words = Flatten(page.Root).SelectMany(f => f.Words).ToList();
                var header = words.Single(w => w.Word.Text == "HEADER").Rect;
                var firstBody = words.Where(w => w.Word.Text is "Row" or "Lead").Min(w => w.Rect.Top);

                gaps.Add(firstBody - header.Bottom);
            }

            Assert.All(gaps, gap => Assert.True(Math.Abs(gap - gaps[0]) < 0.1, $"gap {gap:F2} differs from {gaps[0]:F2}"));
        }


        [Fact]
        public async Task ARepeatedFooter_StaysBelowEveryInnerRowOnEveryPage()
        {
            var html = Document(40).Replace("</tbody>", "</tbody><tfoot><tr><td>FOOT</td></tr></tfoot>");

            var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
                html, new PdfGenerateConfig { PageSize = PageSize.Letter });

            var pages = container.FragmentTree!.Fragmentainers;
            Assert.True(pages.Count >= 3);

            foreach (var (page, index) in pages.Select((page, index) => (page, index)))
            {
                var words = Flatten(page.Root).SelectMany(f => f.Words).ToList();
                var foot = Assert.Single(words, w => w.Word.Text == "FOOT").Rect;

                Assert.All(
                    words.Where(w => w.Word.Text == "Row"),
                    row => Assert.True(row.Rect.Bottom <= foot.Top + 0.5, $"page {index}: row ending {row.Rect.Bottom:F2} reaches FOOT at {foot.Top:F2}"));
            }
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
