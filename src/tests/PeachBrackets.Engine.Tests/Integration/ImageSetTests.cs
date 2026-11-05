using PeachBrackets.Css;
using PeachBrackets.Engine.Tests.TestSupport;
using PeachBrackets.Html;
using PeachBrackets.Html.Entities;
using PeachBrackets.Html.Parse;
using PeachPDF;
using PeachPDF.Rendering;
using PeachPDF.Tests.TestSupport;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary><c>image-set()</c> (CSS Images 4 §2.4): the option it resolves to, by resolution and by the document's selection policy.</summary>
    public class ImageSetTests
    {
        private static async Task<HtmlContainerInt> ContainerAsync(double dpr, ImageSelectionOptions options)
        {
            var (_, container) = await LayoutHarness.LayoutAsync("<p>x</p>", configureContainer: c =>
            {
                c.DevicePixelRatio = dpr;
                c.ImageSelection = options;
            });
            return container;
        }

        private static string? Href(CssImage? image) => (image as CssImage.Url)?.Href;

        [Theory]
        [InlineData("image-set(url(a.png) 1x, url(b.png) 2x)", "b.png")]
        [InlineData("image-set(url(b.png) 2x, url(a.png) 1x)", "b.png")]
        [InlineData("image-set('a.png' 1x, \"b.png\" 3x)", "b.png")]
        [InlineData("image-set(url(a.png), url(b.png) 1.5x)", "b.png")]
        [InlineData("image-set(url(a.png) 96dpi, url(b.png) 192dpi)", "b.png")]
        [InlineData("image-set(url(a.png) 1dppx, url(b.png) 2dppx)", "b.png")]
        [InlineData("image-set(url(a.png) 2x, url(b.png) 2x)", "a.png")]
        [InlineData("image-set(url(a.png))", "a.png")]
        [InlineData("image-set('data:image/png;base64,AA+/==' 1x, 'data:image/png;base64,BB+/==' 192dpi)", "data:image/png;base64,BB+/==")]
        public async Task Paged_PicksTheHighestResolution(string value, string expected)
        {
            var container = await ContainerAsync(1, ImageSelectionOptions.HighestDensityPolicy);
            Assert.Equal(expected, Href(new CssValueParser(container.RenderContext!).ParseImage(value, container)));
        }

        [Fact]
        public async Task WithoutAContainer_PicksTheHighestResolution()
        {
            var container = await ContainerAsync(1, ImageSelectionOptions.HighestDensityPolicy);
            Assert.Equal("b.png", Href(new CssValueParser(container.RenderContext!).ParseImage("image-set(url(a.png) 1x, url(b.png) 2x)")));
        }

        [Theory]
        [InlineData(1, "a.png")]
        [InlineData(1.5, "b.png")]
        [InlineData(2, "b.png")]
        [InlineData(3, "c.png")]
        public async Task Screen_PicksTheSmallestSharpEnoughOption(double dpr, string expected)
        {
            var container = await ContainerAsync(dpr, ImageSelectionOptions.ScreenDefault);
            var image = new CssValueParser(container.RenderContext!).ParseImage("image-set(url(c.png) 3x, url(a.png) 1x, url(b.png) 2x)", container);
            Assert.Equal(expected, Href(image));
        }

        [Fact]
        public async Task Screen_WhenNoneIsSharpEnough_PicksTheLargest()
        {
            var container = await ContainerAsync(4, ImageSelectionOptions.ScreenDefault);
            Assert.Equal("b.png", Href(new CssValueParser(container.RenderContext!).ParseImage("image-set(url(a.png) 1x, url(b.png) 2x)", container)));
        }

        [Fact]
        public async Task Screen_ZoomCountsWhenTheOptionAllowsIt()
        {
            var container = await ContainerAsync(1, ImageSelectionOptions.ScreenDefault);
            container.RenderScale = 2;
            var parser = new CssValueParser(container.RenderContext!);
            Assert.Equal("b.png", Href(parser.ParseImage("image-set(url(a.png) 1x, url(b.png) 2x)", container)));

            container.ImageSelection = new ImageSelectionOptions { UpgradeOnZoom = false };
            Assert.Equal("a.png", Href(parser.ParseImage("image-set(url(a.png) 1x, url(b.png) 2x)", container)));
        }

        [Fact]
        public async Task TypeOption_DropsAnUndecodableFormat()
        {
            var container = await ContainerAsync(1, ImageSelectionOptions.HighestDensityPolicy);
            var parser = new CssValueParser(container.RenderContext!);
            Assert.Equal("a.png", Href(parser.ParseImage("image-set(url(a.png) 1x type(\"image/png\"), url(b.xyz) 2x type(\"image/x-nonexistent\"))", container)));
            Assert.Null(parser.ParseImage("image-set(url(b.xyz) type(\"image/x-nonexistent\"))", container));
        }

        [Theory]
        [InlineData("image-set()")]
        [InlineData("image-set(url(a.png) bogus)")]
        [InlineData("image-set(url(a.png) -1x)")]
        [InlineData("image-set(url(a.png) 0x)")]
        public async Task InvalidSets_PaintNothing(string value)
        {
            var container = await ContainerAsync(1, ImageSelectionOptions.HighestDensityPolicy);
            Assert.Null(new CssValueParser(container.RenderContext!).ParseImage(value, container));
        }

        [Fact]
        public async Task GradientSource_IsChosenLikeAUrl()
        {
            var container = await ContainerAsync(1, ImageSelectionOptions.HighestDensityPolicy);
            var image = new CssValueParser(container.RenderContext!).ParseImage("image-set(url(a.png) 1x, linear-gradient(red, blue) 2x)", container);
            Assert.IsType<CssImage.LinearGradient>(image);
        }

        [Fact]
        public async Task BackgroundImage_UsesTheChosenOption()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<div id='d' style=\"height:20px; background-image: image-set(url(a.png) 1x, url(b.png) 2x)\"></div>"),
                configureContainer: c => { c.DevicePixelRatio = 2; c.ImageSelection = ImageSelectionOptions.ScreenDefault; });
            var box = LayoutHarness.FindById(root, "d")!;
            Assert.Equal("b.png", Href(box.BackgroundImages!.Single()));
        }

        [Fact]
        public async Task ListStyleImage_UsesTheChosenOption()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<ul><li id='d' style=\"list-style-image: image-set(url(a.png) 1x, url(b.png) 2x)\">x</li></ul>"));
            Assert.Equal("b.png", Href(LayoutHarness.FindById(root, "d")!.ListStyleImage));
        }

        // 100x50 and 200x100 solid PNGs: the same picture at 1x and 2x.
        private static readonly string OneX = "data:image/png;base64," + Convert.ToBase64String(RasterPngFixture.MakeSolidRgbaPngBytes(100, 50, 255, 0, 0));
        private static readonly string TwoX = "data:image/png;base64," + Convert.ToBase64String(RasterPngFixture.MakeSolidRgbaPngBytes(200, 100, 0, 0, 255));

        private static string SetBackground() =>
            LayoutHarness.Wrap($"<div id='d' style=\"width:300pt; height:100pt; background: image-set(url({OneX}) 1x, url({TwoX}) 2x) no-repeat\"></div>");

        private static async Task<(double Width, double Height)> PaintedSizeAsync(HtmlContainerInt container)
        {
            using var graphics = new RecordingGraphics(new HtmlRenderContext());
            FragmentPaintHarness.PaintPage(container, graphics, 0);
            var rect = Assert.Single(graphics.DrawnImageRects);
            await Task.CompletedTask;
            return (rect.Width, rect.Height);
        }

        [Fact]
        public async Task BackgroundNaturalSize_FollowsTheDeclaredResolution()
        {
            // 200px wide at 2x is 100 CSS px = 75pt, not 150pt.
            var (_, container) = await LayoutHarness.LayoutAsync(SetBackground(), configureContainer: c => c.ImageSelection = ImageSelectionOptions.HighestDensityPolicy);
            var (w, h) = await PaintedSizeAsync(container);
            Assert.Equal(75, w, 2);
            Assert.Equal(37.5, h, 2);
        }

        [Fact]
        public async Task ZoomingIn_SwapsInTheDenserOptionAndKeepsTheSize()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(SetBackground(), configureContainer: c =>
            {
                c.DevicePixelRatio = 1;
                c.ImageSelection = ImageSelectionOptions.ScreenDefault;
            });
            var box = LayoutHarness.FindById(root, "d")!;
            Assert.Equal(OneX, Href(box.BackgroundImages!.Single()));
            Assert.Empty(await container.ReselectImagesAsync());

            container.RenderScale = 2;
            var changed = await container.ReselectImagesAsync();

            Assert.Same(box, Assert.Single(changed));
            Assert.Equal(TwoX, Href(box.BackgroundImages!.Single()));
            var (w, h) = await PaintedSizeAsync(container);
            Assert.Equal(75, w, 2);
            Assert.Equal(37.5, h, 2);

            // Zooming back out keeps the sharper image.
            container.RenderScale = 1;
            Assert.Empty(await container.ReselectImagesAsync());
            Assert.Equal(TwoX, Href(box.BackgroundImages!.Single()));
        }
    }
}
