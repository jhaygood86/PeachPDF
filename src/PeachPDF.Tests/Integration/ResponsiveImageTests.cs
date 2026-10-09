using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Tests.TestSupport;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>srcset/sizes and &lt;picture&gt;/&lt;source&gt; selection: which image an &lt;img&gt; ends up loading and at what size.</summary>
    public class ResponsiveImageTests
    {
        // 100x50 and 400x200 solid PNGs; the box's laid-out width tells which one loaded.
        private static readonly string Small = Uri(100, 50, 255, 0, 0);
        private static readonly string Large = Uri(400, 200, 0, 0, 255);

        private static string Uri(int w, int h, byte r, byte g, byte b) =>
            "data:image/png;base64," + Convert.ToBase64String(RasterPngFixture.MakeSolidRgbaPngBytes(w, h, r, g, b));

        private static async Task<double> WidthOfAsync(string body)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(body));
            return ((PeachPDF.Html.Core.Dom.CssBoxImage)LayoutHarness.FindById(root, "i")!).ReplacedWord.Width;
        }

        [Fact]
        public async Task Srcset_DensityDescriptors_PicksHighestDensityAndCorrectsNaturalSize()
        {
            // 400px natural at 2x => 200 CSS px => 150pt.
            Assert.Equal(150, await WidthOfAsync($"<img id='i' srcset='{Small} 1x, {Large} 2x'>"), 3);
        }

        [Fact]
        public async Task Srcset_WidthDescriptorsWithSizes_PicksHighestDensityForTheSlot()
        {
            // Slot 200px: small = 0.5x, large = 2x => large, drawn at 400/2 = 200px = 150pt.
            Assert.Equal(150, await WidthOfAsync($"<img id='i' sizes='200px' srcset='{Small} 100w, {Large} 400w'>"), 3);
        }

        [Fact]
        public async Task Srcset_SizesMediaCondition_UsesFirstMatchingEntry()
        {
            // First entry never matches; second (400px) does, so large is exactly 1x => 300pt.
            var html = $"<img id='i' sizes='(min-width: 99999px) 10px, 400px' srcset='{Small} 100w, {Large} 400w'>";
            Assert.Equal(300, await WidthOfAsync(html), 3);
        }

        [Fact]
        public async Task Srcset_WithoutSizes_DefaultsToTheFullPageWidth()
        {
            // 100vw is the 555pt content-box viewport (740px): large is the higher density and lays out at 100vw.
            Assert.Equal(555, await WidthOfAsync($"<img id='i' srcset='{Small} 100w, {Large} 400w'>"), 1);
        }

        [Fact]
        public async Task Srcset_AlongsideSrc_SrcCountsAsA1xCandidate()
        {
            Assert.Equal(75, await WidthOfAsync($"<img id='i' src='{Small}' srcset='{Large} 0.5x'>"), 3);
        }

        [Fact]
        public async Task Srcset_AllCandidatesInvalid_FallsBackToSrc()
        {
            Assert.Equal(75, await WidthOfAsync($"<img id='i' src='{Small}' srcset='{Large} bogus'>"), 3);
        }

        [Fact]
        public async Task Img_WithoutSrcset_IsUnchanged()
        {
            Assert.Equal(300, await WidthOfAsync($"<img id='i' src='{Large}'>"), 3);
        }

        [Fact]
        public async Task Picture_MatchingSource_WinsOverImgSrc()
        {
            var html = $"<picture><source media='(min-width: 100px)' srcset='{Large}'><img id='i' src='{Small}'></picture>";
            Assert.Equal(300, await WidthOfAsync(html), 3);
        }

        [Fact]
        public async Task Picture_NonMatchingMedia_IsSkipped()
        {
            var html = $"<picture><source media='(min-width: 99999px)' srcset='{Large}'><img id='i' src='{Small}'></picture>";
            Assert.Equal(75, await WidthOfAsync(html), 3);
        }

        [Fact]
        public async Task Picture_JpegXlSource_IsChosenOverFallback()
        {
            // rgb_lossy.jxl is 64x48 px => 48pt wide; the <img> fallback would be 400px => 300pt.
            var html = $"<picture><source type='image/jxl' srcset='{JxlFixtures.DataUri("rgb_lossy")}'><img id='i' src='{Large}'></picture>";
            Assert.Equal(48, await WidthOfAsync(html), 3);
        }

        [Fact]
        public async Task Picture_UnsupportedType_IsSkipped()
        {
            var html = $"<picture><source type='image/jxl-unknown' srcset='{Large}'><source type='image/png' srcset='{Small} 1x'><img id='i' src='{Large}'></picture>";
            Assert.Equal(75, await WidthOfAsync(html), 3);
        }

        [Fact]
        public async Task Picture_SourceIsVoid_ImgIsItsSiblingNotChild()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<picture><source srcset='{Large}'><img id='i' src='{Small}'></picture>"));

            var img = LayoutHarness.FindById(root, "i")!;
            Assert.Equal("picture", img.ParentBox!.HtmlTag!.Name);
        }

        [Fact]
        public async Task Picture_RendersTheSelectedImageIntoThePdf()
        {
            var html = LayoutHarness.Wrap($"<picture><source srcset='{Large} 2x'><img src='{Small}'></picture>");
            var doc = await new PdfGenerator().GeneratePdf(html, new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false });
            var ms = new MemoryStream();
            doc.Save(ms);
            var text = Encoding.Latin1.GetString(ms.ToArray());

            Assert.Contains("/Width 400", text);
            Assert.DoesNotContain("/Width 100\n", text.Replace("\r", ""));
        }

        [Theory]
        [InlineData("a.png, b.png 2x", new[] { "a.png|1", "b.png|2" })]
        [InlineData("a.png 1.5x,b.png 100w", new[] { "a.png|1.5", "b.png|w100" })]
        [InlineData("a.png,, b.png,", new[] { "a.png|1", "b.png|1" })]
        [InlineData("a.png 1x 2x, b.png 0x, c.png -1x, d.png 0w", new string[0])]
        [InlineData("data:image/png;base64,AAAA 2x", new[] { "data:image/png;base64,AAAA|2" })]
        public void ParseSrcset_FollowsTheHtmlAlgorithm(string input, string[] expected)
        {
            var actual = ResponsiveImageSelector.ParseSrcset(input)
                .ConvertAll(c => c.Width is { } w ? $"{c.Url}|w{w}" : $"{c.Url}|{c.Density.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData("400px", "", "400px")]
        [InlineData("(max-width: 600px) 480px", "(max-width: 600px)", "480px")]
        [InlineData("(max-width: 600px) calc(100vw - 20px)", "(max-width: 600px)", "calc(100vw - 20px)")]
        [InlineData("(min-width: 600px)", "(min-width: 600px)", "")]
        public void SplitSizeEntry_SeparatesConditionFromLength(string entry, string condition, string length)
        {
            Assert.Equal((condition, length), ResponsiveImageSelector.SplitSizeEntry(entry));
        }
    }
}
