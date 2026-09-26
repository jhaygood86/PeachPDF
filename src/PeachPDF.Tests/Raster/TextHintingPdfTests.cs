using PeachPDF.Tests.TestSupport;
using System.Text.RegularExpressions;

namespace PeachPDF.Tests.Raster
{
    /// <summary>
    /// <see cref="PdfGenerateConfig.TextHinting"/> reaches only the raster backend: the text of a PDF is vector text at any size, and stays
    /// exactly what it was.
    /// </summary>
    public class TextHintingPdfTests
    {
        // The text is set in a bundled TrueType font with hinting programs, named in an @font-face rule: a generic family would be
        // whatever font the host resolves it to (a system font of macOS or Linux, which may have no programs to run), and the test would
        // pass or fail with the machine.
        private static string HintedFace => BundledFonts.FontFaceRule(BundledFonts.Ttf, "HintedTestSans", "font/truetype");

        private static string PlainPage =>
            "<html><head><style>" + HintedFace + "</style></head><body style=\"margin:10px;font:12px HintedTestSans\"><p>Hinting does not touch vector text. Hxg 0123456789</p><p style=\"font-size:9px\">Small text, too.</p></body></html>";

        private static string PageWithRasterText =>
            "<html><head><style>" + HintedFace + "</style></head><body style=\"margin:0;font:11px HintedTestSans\"><div style=\"filter:grayscale(1);width:200px\">Text drawn into a bitmap: Hxg 0123456789</div></body></html>";

        private static PdfGenerateConfig Config(TextHinting? hinting) => new PdfGenerateConfig()
        {
            PageSize = PageSize.A4,
            CompressContentStreams = false,
            RasterizationDpi = 72,
            MarginLeft = 0,
            MarginTop = 0,
            MarginRight = 0,
            MarginBottom = 0,
        }.With(hinting);

        /// <summary>The file with the parts that differ between two runs of the same document (dates, the file identifier) blanked.</summary>
        private static async Task<string> Generate(string html, TextHinting? hinting)
        {
            var pdf = await PdfObjectReader.GeneratePdf(html, Config(hinting));
            pdf = Regex.Replace(pdf, @"/(CreationDate|ModDate)\s*\([^)]*\)", "/$1()");
            pdf = Regex.Replace(pdf, @"/ID\s*\[[^\]]*\]", "/ID[]");
            pdf = Regex.Replace(pdf, @"(?m)^%(?! ?PDF|%EOF)[^\r\n]*[\r\n]+", ""); // the creation-time comment lines
            pdf = Regex.Replace(pdf, @"/[A-Z]{6}\+", "/XXXXXX+"); // the random tag of a font subset
            pdf = Regex.Replace(pdf, @"<xmp:(CreateDate|ModifyDate|MetadataDate)>[^<]*</xmp:\1>", "<xmp:$1/>");
            pdf = Regex.Replace(pdf, @"<xmpMM:(DocumentID|InstanceID)>[^<]*</xmpMM:\1>", "<xmpMM:$1/>");
            return pdf;
        }

        [Fact]
        public void HintingIsOffUnlessAskedFor() => Assert.Equal(TextHinting.None, new PdfGenerateConfig().TextHinting);

        [Theory]
        [InlineData(TextHinting.None)]
        [InlineData(TextHinting.Standard)]
        [InlineData(TextHinting.Monochrome)]
        public async Task VectorTextIsTheSameWhateverTheHinting(TextHinting hinting)
        {
            // nothing on this page is drawn by the raster backend, so no mode may change a byte of the file
            var unset = await Generate(PlainPage, null);
            var set = await Generate(PlainPage, hinting);

            Assert.Equal(unset, set);
        }

        [Fact]
        public async Task ExplicitlyTurningHintingOffChangesNothing()
        {
            Assert.Equal(await Generate(PageWithRasterText, null), await Generate(PageWithRasterText, TextHinting.None));
        }

        [Fact]
        public async Task TextThatTheRasterBackendDrawsIsHintedButTheVectorTextAroundItIsNot()
        {
            var plain = await Generate(PageWithRasterText, TextHinting.None);
            var hinted = await Generate(PageWithRasterText, TextHinting.Standard);

            // the bitmap is what hinting changes: both files draw text into an image, and the files differ
            Assert.Contains("/Subtype /Image", plain.Replace("/Subtype/Image", "/Subtype /Image"));
            Assert.Contains("/Subtype /Image", hinted.Replace("/Subtype/Image", "/Subtype /Image"));
            Assert.NotEqual(plain, hinted);
        }

        [Fact]
        public async Task AnUndefinedModeIsRejected()
        {
            var generator = new PdfGenerator();

            var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => generator.GeneratePdf(PlainPage, Config((TextHinting)99)));
            Assert.Equal("config", exception.ParamName);
        }
    }

    internal static class PdfGenerateConfigTestExtensions
    {
        public static PdfGenerateConfig With(this PdfGenerateConfig config, TextHinting? hinting)
        {
            if (hinting is { } value)
                config.TextHinting = value;
            return config;
        }
    }
}
