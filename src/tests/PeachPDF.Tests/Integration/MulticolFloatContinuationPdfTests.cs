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

        // #2320: a float inside a grid inside a table cell, the reduced document of the issue. The paragraph after the float used to be painted twice (its
        // words on two pages), and its background on two more pages as a rectangle a thousand points tall, because the grid item's content that the next page
        // continues was still held by the page before it. It is drawn once, on the page the column continues on, and its background is the size of its text.
        [Fact]
        public async Task TheParagraphAfterAFloatInAGridInATableCell_IsDrawnOnceWhereTheColumnContinues()
        {
            var pages = await PagesWithMarkerAsync(
                "@page { size: 300pt 250pt; margin: 20pt } body { margin: 0; font: 10pt 'FAMILY' }",
                "<div style='columns:3'><table style=''><tbody><tr><td style=''><div style='display:grid'><div style='border:1pt dotted #888'><div style='float:right'><p style=''> w6045_757 w6045_758 w6045_759 w6045_760 w6045_761 w6045_762 w6045_763  w6045_764 w6045_765 w6045_766 w6045_767 w6045_768 </p><p style=''> w6045_769 w6045_770 w6045_771 w6045_772 w6045_773 w6045_774 w6045_775 w6045_776 w6045_777 w6045_778 w6045_779 w6045_780  w6045_781  w6045_782 w6045_783 w6045_784 w6045_785 w6045_786 w6045_787 w6045_788 w6045_789  w6045_790 w6045_791 w6045_792 w6045_793 w6045_794 w6045_795 w6045_796 w6045_797 w6045_798 w6045_799 w6045_800 w6045_801 w6045_802 w6045_803 </p><p style='margin:0 0 3pt'> w6045_804 w6045_805 w6045_806  w6045_807 w6045_808 w6045_809 w6045_810  w6045_811 w6045_812  w6045_815 w6045_816 w6045_817 w6045_818 w6045_819 w6045_820 w6045_821 w6045_822 w6045_823 w6045_824 w6045_825 w6045_826 w6045_827 w6045_828  w6045_829  w6045_830 w6045_831 w6045_832 w6045_833 w6045_834 w6045_835 w6045_836 w6045_837  w6045_838 w6045_839 w6045_840  w6045_841 w6045_842 w6045_843  w6045_844 </p></div><p style='background:#f00'> w6045_848  w6045_872 </p></div></div></td></tr></tbody></table></div>",
                7);

            Assert.Equal([2], pages);
        }
    }
}
