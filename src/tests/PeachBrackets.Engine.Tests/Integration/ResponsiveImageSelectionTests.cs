using PeachBrackets.Engine.Tests.TestSupport;
using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;
using System;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>Device pixel ratio, selection policy, fallback and re-selection of responsive images (srcset/sizes/&lt;picture&gt;).</summary>
    public class ResponsiveImageSelectionTests
    {
        // 100, 200 and 400 px wide solid PNGs.
        private static readonly string Small = Uri(100, 50, 255, 0, 0);
        private static readonly string Mid = Uri(200, 100, 0, 255, 0);
        private static readonly string Large = Uri(400, 200, 0, 0, 255);

        private static string Uri(int w, int h, byte r, byte g, byte b) =>
            "data:image/png;base64," + Convert.ToBase64String(RasterPngFixture.MakeSolidRgbaPngBytes(w, h, r, g, b));

        private static string Name(string uri) => uri == Small ? "small" : uri == Mid ? "mid" : uri == Large ? "large" : "other";

        private static Action<HtmlContainerInt> Screen(double dpr = 1, ImageSelectionOptions? options = null) => c =>
        {
            c.DevicePixelRatio = dpr;
            c.ImageSelection = options ?? ImageSelectionOptions.ScreenDefault;
        };

        private static async Task<(string Chosen, double Width)> ChooseAsync(string body, Action<HtmlContainerInt> configure)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(body), configureContainer: configure);
            var img = (CssBoxImage)LayoutHarness.FindById(root, "i")!;
            return (Name(img.ImageSource), img.ReplacedWord.Width);
        }

        [Theory]
        [InlineData(1, "rgb(255, 0, 0)")]
        [InlineData(1.5, "rgb(255, 0, 0)")]
        [InlineData(2, "rgb(0, 0, 255)")]
        [InlineData(3, "rgb(0, 0, 255)")]
        public async Task ResolutionMediaFeature_ReflectsDevicePixelRatio(double dpr, string expected)
        {
            const string html = "<html><head><style>#el { color: rgb(255, 0, 0); } @media (min-resolution: 2dppx) { #el { color: rgb(0, 0, 255); } }</style></head><body><p id='el'>x</p></body></html>";
            var (root, _) = await LayoutHarness.LayoutAsync(html, configureContainer: c => c.DevicePixelRatio = dpr);

            Assert.Equal(expected, LayoutHarness.FindById(root, "el")!.Color);
        }

        [Theory]
        [InlineData(1, "rgb(255, 0, 0)")]
        [InlineData(2, "rgb(0, 0, 255)")]
        public async Task DevicePixelRatioMediaFeature_ReflectsDevicePixelRatio(double dpr, string expected)
        {
            const string html = "<html><head><style>#el { color: rgb(255, 0, 0); } @media (min-device-pixel-ratio: 2) { #el { color: rgb(0, 0, 255); } }</style></head><body><p id='el'>x</p></body></html>";
            var (root, _) = await LayoutHarness.LayoutAsync(html, configureContainer: c => c.DevicePixelRatio = dpr);

            Assert.Equal(expected, LayoutHarness.FindById(root, "el")!.Color);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-2)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public async Task DevicePixelRatio_InvalidValue_IsTreatedAsOne(double dpr)
        {
            var (_, container) = await LayoutHarness.LayoutAsync("<p>x</p>", configureContainer: c => c.DevicePixelRatio = dpr);

            Assert.Equal(1, container.DevicePixelRatio);
        }

        [Theory]
        [InlineData(1, "small")]
        [InlineData(1.1, "small")]
        [InlineData(1.11, "mid")]
        [InlineData(1.5, "mid")]
        [InlineData(2, "mid")]
        [InlineData(2.3, "large")]
        [InlineData(3, "large")]
        [InlineData(5, "large")]
        public async Task DensityCandidates_ScreenPicksSmallestSufficientForDpr(double dpr, string expected)
        {
            var (chosen, _) = await ChooseAsync($"<img id='i' srcset='{Small} 1x, {Mid} 2x, {Large} 3x'>", Screen(dpr));

            Assert.Equal(expected, chosen);
        }

        [Theory]
        [InlineData(1, "mid")]
        [InlineData(1.5, "large")]
        [InlineData(2, "large")]
        [InlineData(3, "large")]
        public async Task WidthCandidates_ScreenPicksSmallestSufficientForSlotAndDpr(double dpr, string expected)
        {
            // A 200 CSS px slot: 100w/200w/400w are 0.5x/1x/2x.
            var (chosen, _) = await ChooseAsync($"<img id='i' sizes='200px' srcset='{Small} 100w, {Mid} 200w, {Large} 400w'>", Screen(dpr));

            Assert.Equal(expected, chosen);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(1.5)]
        [InlineData(2)]
        [InlineData(3)]
        public async Task SwappingTheCandidate_DoesNotChangeTheBoxSize(double dpr)
        {
            var (_, width) = await ChooseAsync($"<img id='i' sizes='200px' srcset='{Small} 100w, {Mid} 200w, {Large} 400w'>", Screen(dpr));

            Assert.Equal(150, width, 3);
        }

        [Fact]
        public async Task DensityCandidates_NothingSufficient_TakesTheLargest()
        {
            var (chosen, _) = await ChooseAsync($"<img id='i' srcset='{Small} 1x, {Mid} 2x'>", Screen(3));

            Assert.Equal("mid", chosen);
        }

        [Fact]
        public async Task DecodeBudget_ExcludesTheCandidateOverIt()
        {
            // 400 px wide is 160000 pixels for a square image; the budget keeps the 200 px one.
            var options = new ImageSelectionOptions { MaxDecodePixels = 100_000 };
            var (chosen, _) = await ChooseAsync($"<img id='i' sizes='200px' srcset='{Small} 100w, {Mid} 200w, {Large} 400w'>", Screen(2, options));

            Assert.Equal("mid", chosen);
        }

        [Fact]
        public async Task DecodeBudget_UsesTheAspectRatioOfTheWidthAndHeightAttributes()
        {
            // A 2:1 image: 400 px wide is 80000 pixels, inside the budget.
            var options = new ImageSelectionOptions { MaxDecodePixels = 100_000 };
            var (chosen, _) = await ChooseAsync($"<img id='i' width='200' height='100' sizes='200px' srcset='{Small} 100w, {Mid} 200w, {Large} 400w'>", Screen(2, options));

            Assert.Equal("large", chosen);
        }

        [Fact]
        public async Task DecodeBudget_EveryCandidateOverIt_TakesTheSmallest()
        {
            var options = new ImageSelectionOptions { MaxDecodePixels = 10 };
            var (chosen, _) = await ChooseAsync($"<img id='i' sizes='200px' srcset='{Small} 100w, {Mid} 200w, {Large} 400w'>", Screen(2, options));

            Assert.Equal("small", chosen);
        }

        [Fact]
        public async Task HighestDensityPolicy_IgnoresTheDevicePixelRatio()
        {
            var options = new ImageSelectionOptions { Policy = ImageSelectionPolicy.HighestDensity };
            var (chosen, _) = await ChooseAsync($"<img id='i' srcset='{Small} 1x, {Mid} 2x, {Large} 3x'>", Screen(1, options));

            Assert.Equal("large", chosen);
        }

        [Fact]
        public async Task DefaultContainer_IsHighestDensity()
        {
            var (chosen, _) = await ChooseAsync($"<img id='i' srcset='{Small} 1x, {Large} 3x'>", c => c.DevicePixelRatio = 1);

            Assert.Equal("large", chosen);
        }

        [Fact]
        public async Task SrcCountsAsAOneXCandidate_ForTheScreenPolicy()
        {
            var (chosen, _) = await ChooseAsync($"<img id='i' src='{Small}' srcset='{Large} 2x'>", Screen(1));

            Assert.Equal("small", chosen);
        }

        [Fact]
        public async Task Picture_ScreenPolicyAppliesWithinTheMatchingSource()
        {
            var html = $"<picture><source media='(min-resolution: 2dppx)' srcset='{Large}'><source srcset='{Small} 1x, {Mid} 2x'><img id='i' src='{Small}'></picture>";

            Assert.Equal("small", (await ChooseAsync(html, Screen(1))).Chosen);
            Assert.Equal("large", (await ChooseAsync(html, Screen(2))).Chosen);
        }

        [Theory]
        [InlineData(ImageSelectionPolicy.HighestDensity, MediaType.Screen, "large")]
        [InlineData(ImageSelectionPolicy.BestForTarget, MediaType.Print, "small")]
        public async Task ExplicitOption_OverridesTheMediumsDefault(ImageSelectionPolicy policy, MediaType media, string expected)
        {
            var chosen = await ChooseThroughDocumentAsync(
                $"<img id='i' srcset='{Small} 1x, {Large} 3x'>",
                new HtmlDocumentOptions { MediaType = media, ImageSelection = new ImageSelectionOptions { Policy = policy } });

            Assert.Equal(expected, chosen);
        }

        [Theory]
        [InlineData(MediaType.Print, "large")]
        [InlineData(MediaType.Screen, "small")]
        public async Task NoOption_ScreenIsBestForTargetAndPrintIsHighestDensity(MediaType media, string expected)
        {
            var chosen = await ChooseThroughDocumentAsync($"<img id='i' srcset='{Small} 1x, {Large} 3x'>", new HtmlDocumentOptions { MediaType = media });

            Assert.Equal(expected, chosen);
        }

        private sealed class NoAvifContext : PeachPDF.Rendering.HtmlRenderContext
        {
            public override bool SupportsImageType(string mimeType) => !mimeType.Contains("avif", StringComparison.OrdinalIgnoreCase) && base.SupportsImageType(mimeType);
        }

        [Fact]
        public async Task Picture_TypeTheHostCannotDecode_FallsThroughToTheNextSource()
        {
            var html = LayoutHarness.Wrap($"<picture><source type='image/avif' srcset='{Large}'><source type='image/png' srcset='{Mid}'><img id='i' src='{Small}'></picture>");

            var (withAvif, _) = await LayoutHarness.LayoutAsync(html);
            var (withoutAvif, _) = await LayoutHarness.LayoutAsync(html, createRenderContext: () => new NoAvifContext());

            Assert.Equal("large", Name(((CssBoxImage)LayoutHarness.FindById(withAvif, "i")!).ImageSource));
            Assert.Equal("mid", Name(((CssBoxImage)LayoutHarness.FindById(withoutAvif, "i")!).ImageSource));
        }

        private static async Task<string> ChooseThroughDocumentAsync(string body, HtmlDocumentOptions options)
        {
            var renderContext = new PeachPDF.Rendering.HtmlRenderContext();
            using var document = await HtmlDocument.LoadAsync(LayoutHarness.Wrap(body), renderContext, options);
            using var canvas = PeachDrawing.PDF.PdfCanvas.CreateMeasuring(renderContext, new PeachDrawing.Core.Size(595, 842));
            await document.LayoutAsync(canvas);

            return Name(((CssBoxImage)LayoutHarness.FindById(document.Container.Root!, "i")!).ImageSource);
        }
    }
}
