using PeachDrawing.PDF;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <c>-peachpdf-pdf-page-label</c> (and Prince's <c>-prince-pdf-page-label</c> spelling of it) on <c>@page</c>: the PDF's page labels, read back
    /// with <see cref="PdfPageSource.GetPageLabels"/>.
    /// </summary>
    public class PdfPageLabelPropertyTests
    {
        /// <summary>A page of 200pt by 200pt per block, with the given rules on top.</summary>
        private static string Pages(string rules, int pages = 4)
        {
            var blocks = string.Concat(Enumerable.Range(1, pages).Select(i => $"<div style='height:100pt;{(i > 1 ? "break-before:page;" : "")}'>p{i}</div>"));
            return $"<!DOCTYPE html><html><head><style>@page {{ size: 200pt 200pt; margin: 40pt 10pt; }} {rules} body {{ margin: 0; }}</style></head><body>{blocks}</body></html>";
        }

        private static async Task<(IReadOnlyList<string> Labels, byte[] Bytes)> LabelsAsync(string html)
        {
            var document = await new PdfGenerator().GeneratePdf(html, new PdfGenerateConfig { PageSize = PageSize.A4 });
            using var stream = new MemoryStream();
            document.Save(stream);
            var bytes = stream.ToArray();
            return (PdfPageSource.Open(bytes).GetPageLabels(), bytes);
        }

        [Fact]
        public async Task ADocumentWithNoLabelProperty_WritesNoLabelEntry()
        {
            var (labels, bytes) = await LabelsAsync(Pages(""));

            Assert.Equal(["1", "2", "3", "4"], labels);
            Assert.DoesNotContain("/PageLabels", System.Text.Encoding.Latin1.GetString(bytes));
        }

        [Fact]
        public async Task AutoEverywhere_WritesNoLabelEntry()
        {
            var (_, bytes) = await LabelsAsync(Pages("@page { -peachpdf-pdf-page-label: auto; }"));

            Assert.DoesNotContain("/PageLabels", System.Text.Encoding.Latin1.GetString(bytes));
        }

        [Fact]
        public async Task ACounterWithAStyle_IsTheLabelsNumberingStyle()
        {
            var (labels, _) = await LabelsAsync(Pages("@page { -peachpdf-pdf-page-label: counter(page, lower-roman); }"));

            Assert.Equal(["i", "ii", "iii", "iv"], labels);
        }

        [Fact]
        public async Task ABareCounterStyle_IsShorthandForTheCounterOfThatStyle()
        {
            var (labels, _) = await LabelsAsync(Pages("@page { -peachpdf-pdf-page-label: upper-roman; }", 3));

            Assert.Equal(["I", "II", "III"], labels);
        }

        [Fact]
        public async Task APrefixBeforeTheCounter_IsThePrefixOfTheRange()
        {
            var (labels, _) = await LabelsAsync(Pages("@page { -peachpdf-pdf-page-label: \"A-\" counter(page); }", 3));

            Assert.Equal(["A-1", "A-2", "A-3"], labels);
        }

        [Fact]
        public async Task FrontMatterBodyAndAppendix_AreRangesAndTheRestartFollowsTheCounter()
        {
            // Pages 1-2 roman; page 3 restarts the counter at 1 for the body; page 4 is the appendix with a prefix and its own restart.
            var rules = """
                @page { -peachpdf-pdf-page-label: counter(page, lower-roman); }
                @page body { -peachpdf-pdf-page-label: counter(page); counter-reset: page 1; }
                @page appendix { -peachpdf-pdf-page-label: "App-" counter(page, upper-alpha); counter-reset: page 1; }
                .body { page: body; } .appendix { page: appendix; }
                """;
            var html = "<!DOCTYPE html><html><head><style>@page { size: 200pt 200pt; margin: 40pt 10pt; } " + rules + " div { height: 100pt; } body { margin: 0; }</style></head><body>" +
                       "<div>p1</div><div style='break-before:page'>p2</div><div class='body' style='break-before:page'>p3</div><div class='appendix' style='break-before:page'>p4</div></body></html>";

            var (labels, _) = await LabelsAsync(html);

            Assert.Equal(["i", "ii", "1", "App-A"], labels);
        }

        [Fact]
        public async Task ARestartedCounter_StartsANewRange()
        {
            var html = Pages("@page { -peachpdf-pdf-page-label: counter(page, decimal); } @page :first { counter-reset: page 7; }", 3);

            var (labels, _) = await LabelsAsync(html);

            Assert.Equal(["7", "8", "9"], labels);
        }

        [Fact]
        public async Task TextAroundTheCounter_IsTheWholeLabelOfEachPage()
        {
            var (labels, _) = await LabelsAsync(Pages("@page { -peachpdf-pdf-page-label: \"Page \" counter(page) \" of \" counter(pages); }", 3));

            Assert.Equal(["Page 1 of 3", "Page 2 of 3", "Page 3 of 3"], labels);
        }

        [Fact]
        public async Task ACounterStylePdfHasNoNameFor_BecomesTheLabelText()
        {
            var (labels, _) = await LabelsAsync(Pages("@page { -peachpdf-pdf-page-label: counter(page, decimal-leading-zero); }", 3));

            Assert.Equal(["01", "02", "03"], labels);
        }

        [Fact]
        public async Task LettersPastZ_AreWrittenAsCssWritesThem()
        {
            // PDF's A..Z, AA, BB differs from CSS's A..Z, AA, AB: the page past 26 is written as its text.
            var html = Pages("@page { -peachpdf-pdf-page-label: counter(page, upper-alpha); } @page :first { counter-reset: page 26; }", 3);

            var (labels, _) = await LabelsAsync(html);

            Assert.Equal(["Z", "AA", "AB"], labels);
        }

        [Fact]
        public async Task ARuleCanLabelOnlySomePages_TheOthersAreDecimalFromTheCounter()
        {
            var html = Pages("@page :first { -peachpdf-pdf-page-label: \"Cover\"; }", 3);

            var (labels, _) = await LabelsAsync(html);

            Assert.Equal(["Cover", "2", "3"], labels);
        }

        [Fact]
        public async Task PrinceSpelling_IsAnAliasWithTheSameGrammar()
        {
            var (labels, _) = await LabelsAsync(Pages("@page { -prince-pdf-page-label: counter(page, lower-roman); }", 3));

            Assert.Equal(["i", "ii", "iii"], labels);
        }

        [Fact]
        public async Task LabelsOfADocumentAddedToAnother_SitAfterThePagesBeforeIt()
        {
            var generator = new PdfGenerator();
            var document = await generator.GeneratePdf(Pages("", 2), new PdfGenerateConfig { PageSize = PageSize.A4 });
            await generator.AddPdfPages(document, Pages("@page { -peachpdf-pdf-page-label: counter(page, lower-roman); }", 2), new PdfGenerateConfig { PageSize = PageSize.A4 });

            using var stream = new MemoryStream();
            document.Save(stream);

            Assert.Equal(["1", "2", "i", "ii"], PdfPageSource.Open(stream.ToArray()).GetPageLabels());
        }
    }
}
