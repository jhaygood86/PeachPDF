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
        private const string PlainPage =
            "<html><body style=\"margin:10px;font:12px sans-serif\"><p>Hinting does not touch vector text. Hxg 0123456789</p><p style=\"font-size:9px\">Small text, too.</p></body></html>";

        private const string PageWithRasterText =
            "<html><body style=\"margin:0;font:11px sans-serif\"><div style=\"filter:grayscale(1);width:200px\">Text drawn into a bitmap: Hxg 0123456789</div></body></html>";

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

            Assert.NotEqual(plain, hinted);
            Assert.Contains("/Subtype /Image", plain.Replace("/Subtype/Image", "/Subtype /Image"));
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
