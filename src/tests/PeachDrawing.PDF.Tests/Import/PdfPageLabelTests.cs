using PeachDrawing.PDF.Import;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>Page labels: the number tree of ranges (ISO 32000-1 12.4.2) applied to every page.</summary>
    public class PdfPageLabelTests
    {
        private static IReadOnlyList<string> Labels(int pages, string catalogExtras) => PdfPageSource.Open(TestPdf.BuildPages(pages, catalogExtras)).GetPageLabels();

        [Fact]
        public void AFileWithNoLabels_NumbersItsPages()
        {
            Assert.Equal(["1", "2", "3"], Labels(3, ""));
        }

        [Fact]
        public void RangesChangeStyleRestartAndPrefix()
        {
            // i, ii, iii for the front matter, then 1, 2 for the body, then A-1, A-2 for an appendix that starts at 1.
            var labels = Labels(7, "/PageLabels << /Nums [0 << /S /r >> 3 << /S /D >> 5 << /S /D /P (A-) /St 1 >>] >>");

            Assert.Equal(["i", "ii", "iii", "1", "2", "A-1", "A-2"], labels);
        }

        [Fact]
        public void StartValuesLettersAndRomanCapitalsAreApplied()
        {
            var labels = Labels(5, "/PageLabels << /Nums [0 << /S /R /St 4 >> 2 << /S /A >> 4 << /S /a /St 27 >>] >>");

            Assert.Equal(["IV", "V", "A", "B", "aa"], labels);
        }

        [Fact]
        public void APrefixWithNoStyle_IsTheWholeLabel_AndARangeNotStartingAtZeroLeavesEarlierPagesNumbered()
        {
            var labels = Labels(3, "/PageLabels << /Nums [1 << /P (Cover) >>] >>");

            Assert.Equal(["1", "Cover", "Cover"], labels);
        }

        [Fact]
        public void ANumberTreeWithKidsIsWalked_AndJunkIsIgnored()
        {
            var labels = Labels(3, "/PageLabels << /Kids [<< /Nums [0 << /S /r >>] >> << /Nums [2 << /S /D /St -5 >> -1 << /S /A >> 1 /Junk] >>] >>");

            Assert.Equal(["i", "ii", "1"], labels);
        }
    }
}
