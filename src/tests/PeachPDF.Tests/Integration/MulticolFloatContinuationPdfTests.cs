using System.Text.RegularExpressions;

using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A float that runs into the next page's column takes the in-flow content after it with it. That content was placed from the bottom of the
    /// paragraph before the float, in the column the fill had left, so it was laid out a page back and painted nowhere. The same document is checked
    /// word by word in <c>PeachBrackets.Engine.Tests</c> (<c>MulticolContentLossTests</c>); this one goes through <see cref="PdfGenerator"/>, which is
    /// what the coverage gate measures the engine through, and marks the paragraph after the float with a background so that a page that holds it
    /// shows as a filled rectangle in the content stream.
    /// </summary>
    public class MulticolFloatContinuationPdfTests
    {
        private static string Words(int n, int count) => string.Join(' ', Enumerable.Range(n, count).Select(i => $"w9000_{i}"));

        [Fact]
        public async Task TheParagraphAfterAFloatThatContinuesInTheNextColumn_IsOnThePageWhereTheColumnContinues()
        {
            var generator = new PdfGenerator();
            await using (var stream = File.OpenRead(BundledFonts.LiberationSans))
                await generator.AddFontFromStream(stream);
            var family = TypefaceFixtures.FamilyNameOf(BundledFonts.LiberationSans);

            var html = $"<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page {{ size: 300pt 160pt; margin: 20pt }} " +
                       $"body {{ margin: 0; font: 12pt '{family}' }}</style></head><body><div style='height:60pt'></div><div style='columns:2'>" +
                       $"<p style='margin:0'>{Words(1, 10)}</p><div style='float:left'>{Words(11, 10)}</div>" +
                       "<p style='margin:0;background:#f00'>w9000_21</p></div></body></html>";

            var result = await generator.GeneratePdf(html, PageSize.A4);
            var file = result.Inspect();

            // The paragraph is the only box with a background, so it is painted on exactly one page and that page is not the first.
            var fill = new Regex(@"(?<![\d.])1 0 0 rg\s+(?:/\S+ gs\s+)?\S+ \S+ \S+ \S+ re\s+f", RegexOptions.CultureInvariant);
            var pagesWithIt = Enumerable.Range(0, file.Pages.Count).Where(i => fill.IsMatch(file.ContentText(file.Pages[i]))).ToList();

            Assert.Equal(2, file.Pages.Count);
            Assert.Equal([1], pagesWithIt);
        }

        private static async Task<List<int>> PagesWithMarkerAsync(string css, string body, int expectedPages)
        {
            var generator = new PdfGenerator();
            await using (var stream = File.OpenRead(BundledFonts.LiberationSans))
                await generator.AddFontFromStream(stream);
            var family = TypefaceFixtures.FamilyNameOf(BundledFonts.LiberationSans);

            var html = $"<!DOCTYPE html><html><head><meta charset='utf-8'><style>{css.Replace("FAMILY", family)}</style></head><body>{body}</body></html>";
            var result = await generator.GeneratePdf(html, PageSize.A4);
            var file = result.Inspect();

            var fill = new Regex(@"(?<![\d.])1 0 0 rg\s+(?:/\S+ gs\s+)?\S+ \S+ \S+ \S+ re\s+f", RegexOptions.CultureInvariant);
            Assert.Equal(expectedPages, file.Pages.Count);
            return Enumerable.Range(0, file.Pages.Count).Where(i => fill.IsMatch(file.ContentText(file.Pages[i]))).ToList();
        }

        // #2312: bare text right after the float shares the container with it; the paragraph behind both is drawn on the page the column continues on.
        [Fact]
        public async Task TextAndAParagraphAfterAFloatThatContinues_AreOnThePageWhereTheColumnContinues()
        {
            var pages = await PagesWithMarkerAsync(
                "@page { size: 300pt 160pt; margin: 20pt } body { margin: 0; font: 12pt 'FAMILY' }",
                $"<div style='height:60pt'></div><div style='columns:2'><p style='margin:0'>{Words(1, 10)}</p><div style='float:left'>{Words(11, 10)}</div>w9000_21 w9000_22 <p style='margin:0;background:#f00'>w9000_23 w9000_24</p></div>",
                2);

            Assert.Equal([1], pages);
        }

        // #2319: a second float after the one that continues is placed from the top of the column too, not from the paragraph before the first.
        [Fact]
        public async Task TheParagraphAfterTwoFloatsInAColumn_IsNotLeftOnThePageOfTheParagraphBeforeThem()
        {
            var pages = await PagesWithMarkerAsync(
                "@page { size: 200pt 120pt; margin: 20pt } body { margin: 0; font: 10pt 'FAMILY' }",
                $"<div style='columns:2'><p>{Words(139, 6)}</p><div style='float:left'>{Words(198, 19)}</div><div style='float:left'><p>w9000_316</p></div><p style='background:#f00'>w9000_318</p></div>",
                3);

            Assert.DoesNotContain(0, pages);
            Assert.Single(pages);
        }

        // #2320: a float inside a grid inside a table cell. The column top is not where the grid item's content starts, so the paragraph after
        // the float is left where the grid placed it; it was moved to the column top, away from its box, and its words drawn on no page.
        [Fact]
        public async Task TheParagraphAfterAFloatInAGridInATableCell_IsDrawn()
        {
            var pages = await PagesWithMarkerAsync(
                "@page { size: 300pt 250pt; margin: 20pt } body { margin: 0; font: 10pt 'FAMILY' }",
                $"<div style='columns:3'><table><tbody><tr><td><div style='display:grid'><div><div style='float:right'><p>{Words(757, 12)}</p><p>{Words(769, 35)}</p><p>{Words(804, 40)}</p></div><p style='background:#f00'>w9000_848</p></div></div></td></tr></tbody></table></div>",
                2);

            Assert.Single(pages);
        }
    }
}
