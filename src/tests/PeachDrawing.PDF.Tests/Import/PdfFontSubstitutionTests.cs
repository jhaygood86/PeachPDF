using PeachDrawing.Core;
using PeachDrawing.PDF.Import;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>A font a PDF names but does not embed is drawn with an installed family instead of being skipped.</summary>
    public class PdfFontSubstitutionTests
    {
        private static bool Installed(string family) => family is "Arial" or "Liberation Serif" or "Courier New" or "Trebuchet MS";

        [Theory]
        [InlineData("Helvetica", "Arial", false, false)]
        [InlineData("ABCDEF+Arial-BoldMT", "Arial", true, false)]
        [InlineData("Helvetica-BoldOblique", "Arial", true, true)]
        [InlineData("Times-Roman", "Liberation Serif", false, false)]
        [InlineData("CourierNewPS-ItalicMT", "Courier New", false, true)]
        [InlineData("TrebuchetMS", "Trebuchet MS", false, false)]
        public void Choose_MapsNamesToInstalledFamiliesAndStyles(string baseName, string family, bool bold, bool italic)
        {
            var (chosen, style, weight) = PdfFontSubstitution.Choose(baseName, 0, 0, Installed);

            Assert.Equal(family, chosen);
            Assert.Equal(bold, style.HasFlag(PaintFontStyle.Bold));
            Assert.Equal(italic, style.HasFlag(PaintFontStyle.Italic));
            Assert.Equal(bold ? 700 : 400, weight);
        }

        [Theory]
        [InlineData("SomethingUnknown", 0, "sans-serif")]
        [InlineData("SomethingUnknown", 2, "serif")]
        [InlineData("SomethingUnknown", 1, "monospace")]
        public void Choose_FallsBackToAGenericFamilyFromTheDescriptorFlags(string baseName, int flags, string generic)
        {
            Assert.Equal(generic, PdfFontSubstitution.Choose(baseName, flags, 0, _ => false).Family);
        }

        [Fact]
        public void Choose_ReadsBoldAndItalicFromTheDescriptorWhenTheNameSaysNothing()
        {
            var (_, style, weight) = PdfFontSubstitution.Choose("Mystery", 64 | (1 << 18), 0, _ => false);

            Assert.Equal(PaintFontStyle.Bold | PaintFontStyle.Italic, style);
            Assert.Equal(700, weight);
            Assert.Equal(700, PdfFontSubstitution.Choose("Mystery", 0, 700, _ => false).Weight);
        }

        [Theory]
        [InlineData("TrebuchetMS-Bold", "Trebuchet MS")]
        [InlineData("SegoeUI,Italic", "Segoe UI")]
        [InlineData("Calibri", "Calibri")]
        [InlineData("Open Sans", "Open Sans")]
        public void Family_TurnsPostScriptStyleNamesIntoFamilyNames(string name, string family)
        {
            Assert.Equal(family, PdfFontSubstitution.Family(name));
        }

        [Fact]
        public void StripSubsetTag_RemovesOnlyAGenuineTag()
        {
            Assert.Equal("Arial", PdfFontSubstitution.StripSubsetTag("ABCDEF+Arial"));
            Assert.Equal("abcdef+Arial", PdfFontSubstitution.StripSubsetTag("abcdef+Arial"));
            Assert.Equal("Arial", PdfFontSubstitution.StripSubsetTag("Arial"));
        }

        [Fact]
        public void Paint_DrawsAStandard14FontWithAnInstalledOne_AndSaysSo()
        {
            var pdf = TestPdf.Build("BT /F1 24 Tf 10 40 Td (Hello World) Tj ET",
                resources: "/Font << /F1 5 0 R >>", width: 200, height: 80,
                extraObjects: "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
            var source = PdfPageSource.Open(pdf);
            using var canvas = new RasterRenderContext().CreateCanvas(200, 80);

            var report = source.Paint(0, canvas);

            Assert.Single(report);
            Assert.StartsWith("font substituted: Helvetica -> ", report.Single());
            var pixels = canvas.ToPixelBuffer().PremultipliedRgba.Span;
            int ink = 0;
            for (int i = 0; i < pixels.Length; i += 4) if (pixels[i + 3] > 0) ink++;
            Assert.True(ink > 100, "the text should leave marks on the canvas, found " + ink);
        }

        [Fact]
        public void Paint_UsesTheFilesWidthsForTheAdvances()
        {
            // The same text with the font's own /Widths doubled draws further to the right than with the narrow default widths.
            static int RightmostInk(string widths)
            {
                var pdf = TestPdf.Build("BT /F1 20 Tf 5 30 Td (iii) Tj ET",
                    resources: "/Font << /F1 5 0 R >>", width: 200, height: 60,
                    extraObjects: $"<< /Type /Font /Subtype /TrueType /BaseFont /Arial /Encoding /WinAnsiEncoding /FirstChar 105 /LastChar 105 /Widths [{widths}] >>");
                using var canvas = new RasterRenderContext().CreateCanvas(200, 60);
                PdfPageSource.Open(pdf).Paint(0, canvas);
                var span = canvas.ToPixelBuffer().PremultipliedRgba.Span;
                int right = -1;
                for (int y = 0; y < 60; y++)
                    for (int x = 0; x < 200; x++)
                        if (span[(y * 200 + x) * 4 + 3] > 0) right = Math.Max(right, x);
                return right;
            }

            Assert.True(RightmostInk("2000") > RightmostInk("222") + 20);
        }

        [Fact]
        public void Paint_ReportsAType0FontThatIsNotEmbeddedAndHasNoToUnicodeMap()
        {
            var pdf = TestPdf.Build("BT /F1 12 Tf 10 40 Td <0041> Tj ET",
                resources: "/Font << /F1 5 0 R >>", width: 100, height: 80,
                extraObjects:
                [
                    "<< /Type /Font /Subtype /Type0 /BaseFont /Arial /Encoding /Identity-H /DescendantFonts [6 0 R] >>",
                    "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /Arial /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> >>",
                ]);
            using var canvas = new RasterRenderContext().CreateCanvas(100, 80);

            var report = PdfPageSource.Open(pdf).Paint(0, canvas);

            Assert.Contains(report, r => r.Contains("no ToUnicode map"));
        }

        [Fact]
        public void Paint_DrawsAType0FontThatIsNotEmbeddedWhenItHasAToUnicodeMap()
        {
            var cmap = "/CIDInit /ProcSet findresource begin 12 dict begin begincmap\n1 begincodespacerange <0000> <FFFF> endcodespacerange\n"
                + "1 beginbfchar <0041> <0041> endbfchar\nendcmap end end";
            var pdf = TestPdf.Build("BT /F1 30 Tf 10 30 Td <0041> Tj ET",
                resources: "/Font << /F1 5 0 R >>", width: 100, height: 80,
                extraObjects:
                [
                    "<< /Type /Font /Subtype /Type0 /BaseFont /Arial /Encoding /Identity-H /DescendantFonts [6 0 R] /ToUnicode 7 0 R >>",
                    "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /Arial /DW 700 /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> >>",
                    TestPdf.Stream("", cmap),
                ]);
            using var canvas = new RasterRenderContext().CreateCanvas(100, 80);

            var report = PdfPageSource.Open(pdf).Paint(0, canvas);

            Assert.Single(report);
            Assert.StartsWith("font substituted: Arial -> ", report.Single());
            var pixels = canvas.ToPixelBuffer().PremultipliedRgba.Span;
            Assert.Contains(pixels.ToArray().Where((_, i) => i % 4 == 3), a => a > 0);
        }
    }
}
