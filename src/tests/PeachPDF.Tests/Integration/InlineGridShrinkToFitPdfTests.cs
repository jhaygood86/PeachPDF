using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// An auto-width inline-grid is fit-content wide (CSS Grid 1 section 3), so a run of badges shares lines
    /// instead of each one filling its own.
    /// </summary>
    public class InlineGridShrinkToFitPdfTests
    {
        private static async Task<int> PageCountAsync(string display)
        {
            var badges = string.Concat(System.Linq.Enumerable.Repeat(
                $"<span style='display:{display};background:#ccc'>Badge</span> ", 160));
            var result = await new PdfGenerator().GeneratePdf($"<html><body style='font-size:14pt'>{badges}</body></html>", PageSize.A4);
            return result.Inspect().PageCount;
        }

        [Fact]
        public async Task AutoWidthInlineGrid_SharesLinesLikeAnInlineBlock()
        {
            var grid = await PageCountAsync("inline-grid");
            var block = await PageCountAsync("inline-block");

            Assert.Equal(block, grid);
        }
    }
}
