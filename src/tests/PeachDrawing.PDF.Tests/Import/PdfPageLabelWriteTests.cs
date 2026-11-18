using PeachDrawing.PDF;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>Writing page labels with the composer, read back through <see cref="PdfPageSource.GetPageLabels"/>.</summary>
    public class PdfPageLabelWriteTests
    {
        private static byte[] Build(int pages, Action<PdfComposer>? compose, Action<PdfComposerOptions>? configure = null)
        {
            var options = new PdfComposerOptions { CompressContentStreams = false };
            configure?.Invoke(options);
            using var composer = new PdfComposer(new PdfRenderContext(), options);
            for (int i = 0; i < pages; i++) composer.AddPage(200, 300);
            compose?.Invoke(composer);
            using var ms = new MemoryStream();
            composer.Save(ms);
            return ms.ToArray();
        }

        private static IReadOnlyList<string> Labels(byte[] pdf, string? password = null) => PdfPageSource.Open(pdf, password).GetPageLabels();

        [Fact]
        public void AFileWithNoLabels_WritesNoLabelEntry()
        {
            var pdf = Build(2, null);

            Assert.DoesNotContain("/PageLabels", System.Text.Encoding.Latin1.GetString(pdf));
            Assert.Equal(["1", "2"], Labels(pdf));
        }

        [Fact]
        public void EveryStyleIsWrittenAndReadBack()
        {
            var pdf = Build(12, c => c.AddPageLabels(
            [
                new PdfPageLabelRange(0, PdfPageLabelStyle.RomanLower),
                new PdfPageLabelRange(2, PdfPageLabelStyle.Decimal),
                new PdfPageLabelRange(4, PdfPageLabelStyle.RomanUpper, startNumber: 4),
                new PdfPageLabelRange(6, PdfPageLabelStyle.LettersUpper),
                new PdfPageLabelRange(8, PdfPageLabelStyle.LettersLower, startNumber: 27),
                new PdfPageLabelRange(10, PdfPageLabelStyle.None, "Cover "),
            ]));

            Assert.Equal(["i", "ii", "1", "2", "IV", "V", "A", "B", "aa", "bb", "Cover ", "Cover "], Labels(pdf));
        }

        [Fact]
        public void APrefixIsPutBeforeTheNumber_AndNonAsciiPrefixesSurvive()
        {
            var pdf = Build(4, c => c.AddPageLabels(
            [
                new PdfPageLabelRange(0, PdfPageLabelStyle.Decimal, "A-"),
                new PdfPageLabelRange(2, PdfPageLabelStyle.Decimal, "付録 é "),
            ]));

            Assert.Equal(["A-1", "A-2", "付録 é 1", "付録 é 2"], Labels(pdf));
        }

        [Fact]
        public void ADocumentWithoutARangeAtPageZero_GetsADecimalOneThere()
        {
            var pdf = Build(4, c => c.AddPageLabels([new PdfPageLabelRange(2, PdfPageLabelStyle.RomanLower)]));

            Assert.Equal(["1", "2", "i", "ii"], Labels(pdf));
            Assert.Contains("/Nums", System.Text.Encoding.Latin1.GetString(pdf));
        }

        [Fact]
        public void ARangeWithTheSameStartReplacesTheEarlierOne_AcrossBatches()
        {
            var pdf = Build(3, c =>
            {
                c.AddPageLabels([new PdfPageLabelRange(0, PdfPageLabelStyle.RomanLower), new PdfPageLabelRange(1, PdfPageLabelStyle.Decimal)]);
                c.AddPageLabels([new PdfPageLabelRange(0, PdfPageLabelStyle.LettersUpper)]);
            });

            Assert.Equal(["A", "1", "2"], Labels(pdf));
        }

        [Fact]
        public void SavingTwice_RewritesTheLabelsAndPagesAddedLaterAreCovered()
        {
            using var composer = new PdfComposer(new PdfRenderContext(), new PdfComposerOptions { CompressContentStreams = false });
            composer.AddPage(100, 100);
            composer.AddPageLabels([new PdfPageLabelRange(0, PdfPageLabelStyle.RomanLower)]);
            using var first = new MemoryStream();
            composer.Save(first);
            composer.AddPage(100, 100);
            composer.AddPageLabels([new PdfPageLabelRange(1, PdfPageLabelStyle.Decimal)]);
            using var second = new MemoryStream();
            composer.Save(second);

            Assert.Equal(["i"], Labels(first.ToArray()));
            Assert.Equal(["i", "1"], Labels(second.ToArray()));
        }

        [Fact]
        public void ARangeBeyondTheLastPage_IsRejected()
        {
            using var composer = new PdfComposer(new PdfRenderContext());
            composer.AddPage(100, 100);

            Assert.Throws<ArgumentOutOfRangeException>(() => composer.AddPageLabels([new PdfPageLabelRange(1, PdfPageLabelStyle.Decimal)]));
            Assert.Throws<ArgumentNullException>(() => composer.AddPageLabels(null!));
            Assert.Throws<ArgumentNullException>(() => composer.AddPageLabels([null!]));
        }

        [Fact]
        public void ARangeValidatesItsArguments()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PdfPageLabelRange(-1, PdfPageLabelStyle.Decimal));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PdfPageLabelRange(0, PdfPageLabelStyle.Decimal, null, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PdfPageLabelRange(0, (PdfPageLabelStyle)99));
        }

        [Theory]
        [InlineData("linearized")]
        [InlineData("encrypted")]
        [InlineData("pdfa")]
        public void LabelsSurviveLinearizingEncryptingAndPdfA(string mode)
        {
            const string password = "secret";
            var pdf = Build(3, c => c.AddPageLabels([new PdfPageLabelRange(0, PdfPageLabelStyle.RomanLower), new PdfPageLabelRange(1, PdfPageLabelStyle.Decimal, "P")]), o =>
            {
                if (mode == "linearized") o.Linearize = true;
                if (mode == "encrypted") o.Encryption = new PdfEncryptionOptions { UserPassword = password, OwnerPassword = "owner" };
                if (mode == "pdfa") { o.Conformance = PdfAConformance.PdfA2B; o.CreationDate = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero); }
            });

            Assert.Equal(["i", "P1", "P2"], Labels(pdf, mode == "encrypted" ? password : null));
        }
    }
}
