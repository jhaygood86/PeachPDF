namespace PeachDrawing.PDF.Tests.Operations
{
    /// <summary>Page labels carried through take, merge and append.</summary>
    public class PdfOperationPageLabelTests
    {
        private static byte[] Make(int pages, params PdfPageLabelRange[] labels)
        {
            using var composer = new PdfComposer(new PdfRenderContext(), new PdfComposerOptions { CompressContentStreams = false });
            for (var i = 0; i < pages; i++) composer.AddPage(100, 100);
            if (labels.Length > 0) composer.AddPageLabels(labels);
            using var stream = new MemoryStream();
            composer.Save(stream);
            return stream.ToArray();
        }

        private static IReadOnlyList<string> Labels(byte[] pdf) => PdfPageSource.Open(pdf).GetPageLabels();

        private static PdfPageLabelRange[] FrontMatter() =>
            [new PdfPageLabelRange(0, PdfPageLabelStyle.RomanLower), new PdfPageLabelRange(2, PdfPageLabelStyle.Decimal, "A-")];

        [Fact]
        public void TakingAllThePages_KeepsTheLabels()
        {
            var result = PdfOperation.LoadBytes(Make(5, FrontMatter())).ToArray();

            Assert.Equal(["i", "ii", "A-1", "A-2", "A-3"], Labels(result));
        }

        [Fact]
        public void TakingPagesOutOfOrder_KeepsEachPagesOwnLabel()
        {
            var result = PdfOperation.LoadBytes(Make(5, FrontMatter())).TakePages("4,5,1,2").ToArray();

            Assert.Equal(["A-2", "A-3", "i", "ii"], Labels(result));
        }

        [Fact]
        public void ASelectionThatSkipsPages_StartsANewRangeWhereTheNumbersDoNotFollowOn()
        {
            var result = PdfOperation.LoadBytes(Make(5, FrontMatter())).TakePages("3,5").ToArray();

            Assert.Equal(["A-1", "A-3"], Labels(result));
        }

        [Fact]
        public void AFileWithNoLabelsTakenAlone_WritesNone()
        {
            var result = PdfOperation.LoadBytes(Make(3)).ToArray();

            Assert.DoesNotContain("/PageLabels", System.Text.Encoding.Latin1.GetString(result));
        }

        [Fact]
        public void Merging_CarriesEachFilesLabels_AndTheUnlabelledFilesPagesStayNumbered()
        {
            var result = PdfOperation.LoadBytes(Make(2, new PdfPageLabelRange(0, PdfPageLabelStyle.RomanLower)))
                .MergeBytes(Make(2))
                .MergeBytes(Make(1, new PdfPageLabelRange(0, PdfPageLabelStyle.LettersUpper, "App ")))
                .ToArray();

            Assert.Equal(["i", "ii", "1", "2", "App A"], Labels(result));
        }

        [Fact]
        public void AppendingToAComposer_ContinuesAsRangesOfItsOwn()
        {
            using var composer = new PdfComposer(new PdfRenderContext(), new PdfComposerOptions { CompressContentStreams = false });
            composer.AddPage(100, 100);
            composer.AddPageLabels([new PdfPageLabelRange(0, PdfPageLabelStyle.Decimal, "Cover ")]);

            PdfOperation.LoadBytes(Make(2, new PdfPageLabelRange(0, PdfPageLabelStyle.RomanUpper))).AppendTo(composer, new PdfRenderContext());

            using var stream = new MemoryStream();
            composer.Save(stream);
            Assert.Equal(["Cover 1", "I", "II"], Labels(stream.ToArray()));
        }
    }
}
