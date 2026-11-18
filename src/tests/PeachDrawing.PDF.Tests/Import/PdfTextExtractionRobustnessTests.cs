using PeachDrawing;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>The edges of <see cref="PdfPageSource.GetText"/>: spacing the file asks for, mirrored and rotated text, fonts that cannot be loaded, glyphs with no text, and use from several threads.</summary>
    public class PdfTextExtractionRobustnessTests
    {
        private static readonly string Widths = string.Join(" ", Enumerable.Repeat("500", 224));

        private static string Font(string extra = "") =>
            $"<< /Type /Font /Subtype /TrueType /BaseFont /Arial /Encoding /WinAnsiEncoding /FirstChar 32 /LastChar 255 /Widths [{Widths}] {extra} >>";

        private static PdfPageText Text(string content, string pageExtras = "", params string[] more) =>
            PdfPageSource.Open(TestPdf.Build(content, resources: "/Font << /F1 5 0 R >>", pageExtras: pageExtras, width: 200, height: 100,
                extraObjects: [Font(), .. more])).GetText(0);

        [Fact]
        public void LetterSpacingTheFileAsksFor_IsNotMistakenForGapsBetweenWords()
        {
            // 3 pt of character spacing at 10 pt is 0.3 em between letters, far more than a space is - and it is the file's own spacing.
            Assert.Equal("abc", Text("BT /F1 10 Tf 3 Tc 10 40 Td (abc) Tj ET").Text);
        }

        [Fact]
        public void AWordSpacingGap_StillSeparatesWordsThatHaveNoSpaceGlyph()
        {
            Assert.Equal("ab cd", Text("BT /F1 10 Tf 10 40 Td [(ab) -800 (cd)] TJ ET").Text);
        }

        [Fact]
        public void TextSetWithANegativeHorizontalScale_RunsTheOtherWayButIsOneLine()
        {
            var text = Text("BT /F1 10 Tf -100 Tz 100 40 Td (abc) Tj ET");

            Assert.Equal(1, text.LineCount);
            Assert.DoesNotContain('\n', text.Text);
            Assert.Equal(3, text.Text.Replace(" ", "").Length);
        }

        [Theory]
        [InlineData(90)]
        [InlineData(180)]
        [InlineData(270)]
        public void ARotatedPage_ReadsTheSameLine(int rotation)
        {
            Assert.Equal("Hello World", Text("BT /F1 10 Tf 10 40 Td (Hello World) Tj ET", pageExtras: "/Rotate " + rotation).Text);
        }

        [Fact]
        public void AFontWhoseProgramCannotBeRead_StillGivesItsText()
        {
            var broken = "<< /Type /Font /Subtype /TrueType /BaseFont /Zzz /Encoding /WinAnsiEncoding /FirstChar 32 /LastChar 255 /Widths [" + Widths
                + "] /FontDescriptor 6 0 R >>";
            var descriptor = "<< /Type /FontDescriptor /FontName /Zzz /Flags 32 /FontFile2 7 0 R >>";
            var program = TestPdf.Stream("/Length1 20", "this is not a font program");
            var source = PdfPageSource.Open(TestPdf.Build("BT /F1 10 Tf 10 40 Td (Still here) Tj ET", resources: "/Font << /F1 5 0 R >>", width: 200, height: 100,
                extraObjects: [broken, descriptor, program]));

            Assert.Equal("Still here", source.GetText(0).Text);
        }

        [Fact]
        public void AGlyphWithNoText_IsNotMistakenForAGapInTheWord()
        {
            var cmap = TestPdf.Stream("", "1 begincodespacerange <0000> <FFFF> endcodespacerange 2 beginbfchar <0041> <0041> <0043> <0043> endbfchar");
            var pdf = TestPdf.Build("BT /F1 10 Tf 10 40 Td <004100420043> Tj ET", resources: "/Font << /F1 5 0 R >>", width: 200, height: 100,
                extraObjects:
                [
                    "<< /Type /Font /Subtype /Type0 /BaseFont /Arial /Encoding /Identity-H /DescendantFonts [6 0 R] /ToUnicode 7 0 R >>",
                    "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /Arial /DW 500 /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> >>",
                    cmap,
                ]);

            Assert.Equal("AC", PdfPageSource.Open(pdf).GetText(0).Text);
        }

        [Fact]
        public void AType0FontWithAnEncodingOtherThanIdentityAndNoStandIn_IsLeftOut()
        {
            var pdf = TestPdf.Build("BT /F1 10 Tf 10 40 Td <8140> Tj ET", resources: "/Font << /F1 5 0 R >>", width: 200, height: 100,
                extraObjects:
                [
                    "<< /Type /Font /Subtype /Type0 /BaseFont /MS-Mincho /Encoding /90ms-RKSJ-H /DescendantFonts [6 0 R] >>",
                    "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /MS-Mincho /CIDSystemInfo << /Registry (Adobe) /Ordering (Japan1) /Supplement 2 >> >>",
                ]);

            Assert.Equal("", PdfPageSource.Open(pdf).GetText(0).Text);
        }

        [Fact]
        public void MalformedTextOperators_DoNotStopTheRestOfThePageBeingRead()
        {
            // The double-quote operator with its operands missing, then a good line.
            var text = Text("BT /F1 10 Tf 10 60 Td \" ET BT /F1 10 Tf 10 40 Td (Good) Tj ET");

            Assert.Equal("Good", text.Text);
        }

        [Fact]
        public async Task ReadingTextPaintingAndListingLinks_FromSeveralThreads_AgreeAndDoNotFail()
        {
            var link = "<< /Type /Annot /Subtype /Link /Rect [10 10 50 30] /Border [0 0 0] /A << /S /URI /URI (https://example.com) >> >>";
            var pdf = TestPdf.Build("BT /F1 10 Tf 10 40 Td (Shared page) Tj ET", resources: "/Font << /F1 5 0 R >>", pageExtras: "/Annots [" + link + "]",
                width: 200, height: 100, extraObjects: [Font()]);
            var source = PdfPageSource.Open(pdf);
            var failures = new System.Collections.Concurrent.ConcurrentQueue<string>();

            var workers = Enumerable.Range(0, 8).Select(i => Task.Run(() =>
            {
                for (int round = 0; round < 25; round++)
                {
                    try
                    {
                        switch ((i + round) % 3)
                        {
                            case 0:
                                if (source.GetText(0).Text != "Shared page") failures.Enqueue("text");
                                break;
                            case 1:
                                using (var canvas = new RasterRenderContext().CreateCanvas(200, 100)) source.Paint(0, canvas);
                                break;
                            default:
                                if (source.GetLinks(0).Count != 1) failures.Enqueue("links");
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        failures.Enqueue(ex.GetType().Name + ": " + ex.Message);
                    }
                }
            }, TestContext.Current.CancellationToken)).ToArray();

            await Task.WhenAll(workers);

            Assert.Empty(failures);
        }
    }
}
