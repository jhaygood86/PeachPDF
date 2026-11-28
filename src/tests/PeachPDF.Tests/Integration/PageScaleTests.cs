using PeachPDF.Layout;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <see cref="PdfGenerateConfig.PixelsPerInch"/>, <see cref="PdfGenerateConfig.ShrinkToFit"/>, <see cref="PdfGenerateConfig.ScaleToPageSize"/> and
    /// <see cref="PdfGenerateConfig.MinContentWidth"/> scale the content: the document is laid out on a sheet <c>scale</c> times as wide and as tall and every page
    /// is drawn at <c>1 / scale</c> of its size, so a page that is the configured size holds more. A page's scale is the leading <c>cm</c> of its content, and a
    /// size on the page is the size the content says times that scale.
    /// </summary>
    public class PageScaleTests
    {
        // 16px text, and a table 1200px (900pt) wide: wider than A4's content area at any ordinary margin.
        private const string Html =
            "<!DOCTYPE html><html><head></head><body><p style=\"font-size: 16px;\">SIXTEEN-PX-TEXT</p>" +
            "<table style=\"width: 1200px;\"><tr><td>WIDE-TABLE-TEXT</td></tr></table></body></html>";

        private const string HtmlWithPageRule =
            "<!DOCTYPE html><html><head><style>@page { size: A4; margin: 20mm; }</style></head><body><p style=\"font-size: 16px;\">SIXTEEN-PX-TEXT</p>" +
            "<table style=\"width: 1200px;\"><tr><td>WIDE-TABLE-TEXT</td></tr></table></body></html>";

        // The cell on the right is wholly past the edge of the page at its CSS size, and so is the end of the line that cannot wrap.
        private const string OverflowHtml =
            "<!DOCTYPE html><html><head><style>td { border: 1px solid #999; }</style></head><body>" +
            "<table style=\"width: 1200px; border-collapse: collapse;\"><tr><td style=\"width: 1100px;\">LEFT-CELL</td><td>RIGHT-EDGE-CELL</td></tr></table>" +
            "<p style=\"white-space: nowrap;\">NOWRAP-START aaaa bbbb cccc dddd eeee ffff gggg hhhh iiii jjjj kkkk llll mmmm nnnn oooo pppp qqqq rrrr ssss tttt uuuu vvvv wwww xxxx NOWRAP-END</p>" +
            "</body></html>";

        private const double A4Width = 595.28;

        private static async Task<PeachPdfDocument> RenderAsync(string html, PdfGenerateConfig config)
        {
            config.PageSize = PageSize.A4;
            return await new PdfGenerator().GeneratePdf(html, config);
        }

        /// <summary>The scale the first page is drawn at: its leading <c>cm</c>, or 1 when it has none.</summary>
        private static double ScaleOf(PeachPdfDocument document)
        {
            var file = document.Inspect();
            var content = file.ContentText(file.Pages[0]);
            return Regex.Match(content, @"([\d.]+) 0 0 [\d.]+ 0 0 cm") is { Success: true } cm
                ? double.Parse(cm.Groups[1].Value, CultureInfo.InvariantCulture)
                : 1d;
        }

        /// <summary>The font sizes the first page's text is drawn in, as they land on the page.</summary>
        private static List<double> FontSizesOf(PeachPdfDocument document)
        {
            var file = document.Inspect();
            var scale = ScaleOf(document);
            return [.. Regex.Matches(file.ContentText(file.Pages[0]), @"/\S+ ([\d.]+) Tf")
                .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * scale)];
        }

        [Theory]
        [InlineData(96d, 12d)]
        [InlineData(72d, 16d)]
        [InlineData(144d, 8d)]
        public async Task PixelsPerInch_SetsHowLargeACssPixelIs(double pixelsPerInch, double expectedPointSize)
        {
            foreach (var html in new[] { Html, HtmlWithPageRule })
            {
                var document = await RenderAsync(html, new PdfGenerateConfig { PixelsPerInch = pixelsPerInch });

                Assert.Contains(FontSizesOf(document), size => Math.Abs(size - expectedPointSize) < 0.01);
                Assert.Equal(A4Width, PdfFile.Width(document.Inspect().Pages[0]), 1d);
            }
        }

        [Fact]
        public async Task PixelsPerInch_AtTheDefaultDrawsTheContentAtItsCssSize()
        {
            var document = await RenderAsync(Html, new PdfGenerateConfig());

            Assert.Equal(1d, ScaleOf(document));
            Assert.Contains(FontSizesOf(document), size => Math.Abs(size - 12) < 0.01);
        }

        [Theory]
        [InlineData(nameof(PdfGenerateConfig.ShrinkToFit))]
        [InlineData(nameof(PdfGenerateConfig.ScaleToPageSize))]
        [InlineData(nameof(PdfGenerateConfig.MinContentWidth))]
        public async Task TheFitOptions_ShrinkContentThatIsWiderThanThePageUntilItFits(string option)
        {
            foreach (var (html, margin) in new[] { (Html, 10d), (HtmlWithPageRule, 56.69) })
            {
                var config = new PdfGenerateConfig();
                switch (option)
                {
                    case nameof(PdfGenerateConfig.ShrinkToFit): config.ShrinkToFit = true; break;
                    case nameof(PdfGenerateConfig.ScaleToPageSize): config.ScaleToPageSize = true; break;
                    default: config.MinContentWidth = 1300; break;
                }

                var document = await RenderAsync(html, config);
                var scale = ScaleOf(document);

                // The table is 900pt wide as laid out; drawn at `scale` it has to fit between the margins, and it was not shrunk for nothing.
                Assert.True(scale < 1, $"{option} must shrink content wider than the page, scale was {scale}");
                Assert.True(900 * scale <= A4Width - (2 * margin) + 0.5, $"the table is {900 * scale:F1}pt wide at a scale of {scale}");
                Assert.True(FontSizesOf(document).Max() < 12 + 0.01, "text is smaller than its CSS size once shrunk");
                Assert.Equal(A4Width, PdfFile.Width(document.Inspect().Pages[0]), 1d);
            }
        }

        [Fact]
        public async Task ShrinkToFit_LeavesContentThatFitsAtItsCssSize()
        {
            var document = await RenderAsync("<!DOCTYPE html><html><body><p>fits</p></body></html>", new PdfGenerateConfig { ShrinkToFit = true });

            Assert.Equal(1d, ScaleOf(document));
        }

        [Fact]
        public async Task ScaleToPageSize_WithAMinContentWidthBelowThePageWidth_EnlargesNarrowContent()
        {
            var document = await RenderAsync("<!DOCTYPE html><html><body><p>fits</p></body></html>",
                new PdfGenerateConfig { ScaleToPageSize = true, MinContentWidth = 300 });

            Assert.True(ScaleOf(document) > 1, "content laid out in 300pt is drawn larger than its CSS size");
        }

        [Fact]
        public async Task TheFitOptions_ReportNothingLost_WhereWithoutThemTheOverflowIsReported()
        {
            var plain = await RenderAsync(OverflowHtml, new PdfGenerateConfig());
            var shrunk = await RenderAsync(OverflowHtml, new PdfGenerateConfig { ShrinkToFit = true });

            Assert.NotEmpty(plain.ClipReport.ClippedWords);
            Assert.Empty(shrunk.ClipReport.ClippedWords);
        }

        [Fact]
        public async Task Links_FollowTheScaleTheirPageIsDrawnAt()
        {
            const string html = "<!DOCTYPE html><html><body style=\"margin:0\"><a href=\"https://example.com/\">A link to follow</a></body></html>";

            var at96 = (await RenderAsync(html, new PdfGenerateConfig())).PageAnnotations().Single().RectOf();
            var at144 = (await RenderAsync(html, new PdfGenerateConfig { PixelsPerInch = 144 })).PageAnnotations().Single().RectOf();

            // A pixel is 2/3 the size at 144 against 96, and the margins (the page's own) do not scale: the link starts where the margin does.
            Assert.Equal(at96.Left, at144.Left, 1);
            Assert.Equal((at96.Right - at96.Left) * 96d / 144d, at144.Right - at144.Left, 0.5);
            Assert.Equal((at96.Top - at96.Bottom) * 96d / 144d, at144.Top - at144.Bottom, 0.5);
        }

        [Fact]
        public async Task RasterizedEffects_KeepTheirResolutionOnPaperWhateverTheScale()
        {
            // A bitmap is rendered at RasterizationDpi pixels per inch of the page: the same box drawn a third smaller has a third fewer pixels, not as many.
            const string html = "<!DOCTYPE html><html><body style=\"margin:0\"><div style=\"width:300px;height:100px;background:red;filter:blur(2px)\"></div></body></html>";

            async Task<int> WidestImageAsync(double pixelsPerInch)
            {
                var document = await RenderAsync(html, new PdfGenerateConfig { PixelsPerInch = pixelsPerInch, CompressContentStreams = false });
                using var stream = new MemoryStream();
                document.Save(stream);
                var pdf = System.Text.Encoding.Latin1.GetString(stream.ToArray());

                var widest = 0;
                foreach (Match obj in Regex.Matches(pdf, @"(?:^|[\r\n])\d+ 0 obj(.*?)endobj", RegexOptions.Singleline))
                {
                    var text = obj.Groups[1].Value;
                    var streamStart = text.IndexOf("stream", StringComparison.Ordinal);
                    var dictionary = streamStart >= 0 ? text[..streamStart] : text;
                    if (Regex.IsMatch(dictionary, @"/Subtype\s*/Image") && Regex.Match(dictionary, @"/Width\s+(\d+)") is { Success: true } width)
                        widest = Math.Max(widest, int.Parse(width.Groups[1].Value, CultureInfo.InvariantCulture));
                }

                return widest;
            }

            var at96 = await WidestImageAsync(96);
            var at144 = await WidestImageAsync(144);

            Assert.True(at96 > 0, "the blurred box is a bitmap");
            Assert.Equal(at96 * 96d / 144d, at144, at96 * 0.06);
        }

        [Theory]
        [InlineData(0d)]
        [InlineData(-96d)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public async Task PixelsPerInch_MustBePositiveAndFinite(double pixelsPerInch)
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => RenderAsync(Html, new PdfGenerateConfig { PixelsPerInch = pixelsPerInch }));
        }

        [Fact]
        public async Task ADeclarativeDocument_IsScaledByPixelsPerInchToo()
        {
            async Task<PeachPdfDocument> Build(double pixelsPerInch) =>
                await new PdfGenerator().CreateDocument(
                    doc => doc.Page(page =>
                    {
                        page.Size(300, 300).Margin(20);
                        page.Content(c => c.Text("scaled"));
                    }),
                    new PdfGenerateConfig { PixelsPerInch = pixelsPerInch });

            Assert.Equal(1d, ScaleOf(await Build(96)));
            Assert.Equal(96d / 144d, ScaleOf(await Build(144)), 3);
        }
    }
}
