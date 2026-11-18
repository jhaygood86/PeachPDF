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

        private static string CjkType0(string encoding, string ordering = "Japan1", string baseFont = "MS-Mincho") =>
            $"<< /Type /Font /Subtype /Type0 /BaseFont /{baseFont} /Encoding {encoding} /DescendantFonts [6 0 R] >>";

        private static string CjkCidFont(string ordering = "Japan1", string baseFont = "MS-Mincho") =>
            $"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /{baseFont} /CIDSystemInfo << /Registry (Adobe) /Ordering ({ordering}) /Supplement 2 >> >>";

        [Fact]
        public void AType0FontWithAPredefinedCMap_IsReadThroughItsCharacterCollection()
        {
            // Shift-JIS: the hiragana A (two bytes), Latin A (one byte), the hiragana I.
            var pdf = TestPdf.Build("BT /F1 10 Tf 10 40 Td <82A04182A2> Tj ET", resources: "/Font << /F1 5 0 R >>", width: 200, height: 100,
                extraObjects: [CjkType0("/90ms-RKSJ-H"), CjkCidFont()]);

            Assert.Equal("あAい", PdfPageSource.Open(pdf).GetText(0).Text);
        }

        [Theory]
        [InlineData("/UniJIS-UCS2-H", "Japan1", "30423044", "あい")]
        [InlineData("/UniGB-UCS2-H", "GB1", "4E2D6587", "中文")]
        [InlineData("/UniCNS-UCS2-H", "CNS1", "4E2D6587", "中文")]
        [InlineData("/UniKS-UCS2-H", "Korea1", "D55CAE00", "한글")]
        public void EachCollectionsUnicodeCMap_GivesItsText(string encoding, string ordering, string hex, string expected)
        {
            var pdf = TestPdf.Build($"BT /F1 10 Tf 10 40 Td <{hex}> Tj ET", resources: "/Font << /F1 5 0 R >>", width: 200, height: 100,
                extraObjects: [CjkType0(encoding), CjkCidFont(ordering)]);

            Assert.Equal(expected, PdfPageSource.Open(pdf).GetText(0).Text);
        }

        [Fact]
        public void AType0FontWithAnEmbeddedCMapStream_IsReadByThatCMap()
        {
            var cmap = TestPdf.Stream("/Type /CMap /CMapName /Custom", "1 begincodespacerange <00> <FF> endcodespacerange 1 begincidrange <01> <10> 843 endcidrange");
            var pdf = TestPdf.Build("BT /F1 10 Tf 10 40 Td <0102> Tj ET", resources: "/Font << /F1 5 0 R >>", width: 200, height: 100,
                extraObjects: [CjkType0("7 0 R"), CjkCidFont(), cmap]);

            Assert.Equal("あぃ", PdfPageSource.Open(pdf).GetText(0).Text);
        }

        [Fact]
        public void AType0FontWithAnUnknownCMap_IsStillLeftOut()
        {
            var pdf = TestPdf.Build("BT /F1 10 Tf 10 40 Td <8140> Tj ET", resources: "/Font << /F1 5 0 R >>", width: 200, height: 100,
                extraObjects: [CjkType0("/NoSuchCMap-H"), CjkCidFont()]);

            Assert.Equal("", PdfPageSource.Open(pdf).GetText(0).Text);
        }

        private static double ColumnGap(string cidFontExtras)
        {
            var cidFont = $"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /MS-Mincho {cidFontExtras} /CIDSystemInfo << /Registry (Adobe) /Ordering (Japan1) /Supplement 2 >> >>";
            var pdf = TestPdf.Build("BT /F1 10 Tf 100 90 Td <30423044> Tj ET", resources: "/Font << /F1 5 0 R >>", width: 200, height: 100,
                extraObjects: [CjkType0("/UniJIS-UCS2-V"), cidFont]);
            var text = PdfPageSource.Open(pdf).GetText(0);
            return text.GetBounds(text.Text.Length - 1, 1).Single().Y - text.GetBounds(0, 1).Single().Y;
        }

        [Fact]
        public void VerticalMetrics_FromDw2AndW2_ChangeTheSpacingDownTheColumn()
        {
            double normal = ColumnGap("");
            double tightDefault = ColumnGap("/DW2 [880 -500]");
            double tightForOneCid = ColumnGap("/W2 [843 [-500 500 880]]");     // CID 843 is the first character

            Assert.Equal(10, normal, 1);
            Assert.Equal(5, tightDefault, 1);
            Assert.Equal(5, tightForOneCid, 1);
            Assert.Equal(5, ColumnGap("/W2 [843 843 -500 500 880]"), 1);
        }

        [Fact]
        public void VerticalText_ReadsDownTheColumnInOrder()
        {
            var pdf = TestPdf.Build("BT /F1 10 Tf 100 90 Td <30423044> Tj ET", resources: "/Font << /F1 5 0 R >>", width: 200, height: 100,
                extraObjects: [CjkType0("/UniJIS-UCS2-V"), CjkCidFont()]);

            var text = PdfPageSource.Open(pdf).GetText(0);

            Assert.Equal("あい", text.Text.Replace("\n", ""));
            var first = text.GetBounds(0, 1).Single();
            var second = text.GetBounds(text.Text.Length - 1, 1).Single();
            Assert.True(second.Y > first.Y + 5, "the second character is below the first");
            Assert.True(Math.Abs(second.X - first.X) < 1, "and in the same column");
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
