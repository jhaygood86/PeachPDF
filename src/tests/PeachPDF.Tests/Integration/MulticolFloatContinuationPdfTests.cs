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
    }
}
