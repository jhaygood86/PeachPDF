using PeachPDF.Adapters;
using PeachDrawing.Core;
using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end coverage for CSS <c>image-rendering</c> (CSS Images 3 §5.3): the keyword reaches each image painter as the
    /// sampling it asks the canvas for. A recording graphics captures the <see cref="ImageSampling"/> of every image draw, so
    /// the assertions are on what the painters actually requested, not merely that painting completed.
    /// </summary>
    public class ImageRenderingIntegrationTests
    {
        private const string Png =
            "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAQAAAAECAIAAAAmkwkpAAAAE0lEQVR4nGM8YWTEAANMcBZeDgA8MgE0GRiVCQAAAABJRU5ErkJggg==";

        private sealed class SamplingRecorder : TestRecordingGraphics
        {
            public List<ImageSampling> Samplings { get; } = [];

            public override void DrawImage(Image image, Rect destRect, ImageSampling sampling)
            {
                Samplings.Add(sampling);
                base.DrawImage(image, destRect, sampling);
            }

            public override void DrawImage(Image image, Rect destRect, Rect srcRect, ImageSampling sampling)
            {
                Samplings.Add(sampling);
                base.DrawImage(image, destRect, srcRect, sampling);
            }
        }

        private static async Task<SamplingRecorder> PaintAsync(string body)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml($"<!DOCTYPE html><html><head></head><body style='margin:0'>{body}</body></html>", null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            var recorder = new SamplingRecorder();
            FragmentPaintHarness.PaintBox(container, container.Root!, recorder);
            return recorder;
        }

        private static string Img(string style) => $"<img style='width:40pt;height:40pt;{style}' src='{Png}'>";

        private static string Bg(string style) =>
            $"<div style='width:40pt;height:40pt;background-image:url({Png});{style}'></div>";

        [Theory]
        [InlineData("", ImageSampling.Automatic)]
        [InlineData("image-rendering:auto", ImageSampling.Automatic)]
        [InlineData("image-rendering:smooth", ImageSampling.Bilinear)]
        [InlineData("image-rendering:high-quality", ImageSampling.Bicubic)]
        [InlineData("image-rendering:crisp-edges", ImageSampling.Nearest)]
        [InlineData("image-rendering:pixelated", ImageSampling.Pixelated)]
        [InlineData("image-rendering:bogus", ImageSampling.Automatic)]
        public async Task Img_RequestsTheSamplingItsImageRenderingNames(string style, ImageSampling expected)
        {
            var g = await PaintAsync(Img(style));

            Assert.Equal(expected, Assert.Single(g.Samplings));
        }

        [Fact]
        public async Task ImageRendering_IsInherited()
        {
            var g = await PaintAsync($"<div style='image-rendering:pixelated'>{Img("")}</div>");

            Assert.Equal(ImageSampling.Pixelated, Assert.Single(g.Samplings));
        }

        [Fact]
        public async Task ImageRendering_AChildCanOverrideItsParent()
        {
            var g = await PaintAsync($"<div style='image-rendering:pixelated'>{Img("image-rendering:smooth")}</div>");

            Assert.Equal(ImageSampling.Bilinear, Assert.Single(g.Samplings));
        }

        [Fact]
        public async Task Background_NoRepeat_IsAutomaticUnlessAsked()
        {
            var plain = await PaintAsync(Bg("background-repeat:no-repeat"));
            var pixelated = await PaintAsync(Bg("background-repeat:no-repeat;image-rendering:pixelated"));

            Assert.Equal(ImageSampling.Automatic, Assert.Single(plain.Samplings));
            Assert.Equal(ImageSampling.Pixelated, Assert.Single(pixelated.Samplings));
        }

        [Fact]
        public async Task Background_Repeating_IsCrispByDefaultSoTilesMeetWithoutASeam()
        {
            var g = await PaintAsync(Bg("background-repeat:repeat;background-size:20pt 20pt"));

            // Crisp when enlarged, but still smooth when a tile is shrunk (as it was before the sampling was a parameter).
            Assert.NotEmpty(g.Samplings);
            Assert.All(g.Samplings, s => Assert.Equal(ImageSampling.Pixelated, s));
        }

        [Fact]
        public async Task Background_Repeating_AnExplicitImageRenderingWins()
        {
            var g = await PaintAsync(Bg("background-repeat:repeat;background-size:20pt 20pt;image-rendering:high-quality"));

            Assert.All(g.Samplings, s => Assert.Equal(ImageSampling.Bicubic, s));
        }

        [Fact]
        public async Task BorderImage_IsCrispByDefaultAndHonoursImageRendering()
        {
            var style = $"width:40pt;height:40pt;border:8pt solid black;border-image-source:url({Png});border-image-slice:1;border-image-width:8pt";
            var plain = await PaintAsync($"<div style='{style}'></div>");
            var smooth = await PaintAsync($"<div style='{style};image-rendering:smooth'></div>");

            Assert.NotEmpty(plain.Samplings);
            Assert.All(plain.Samplings, s => Assert.Equal(ImageSampling.Pixelated, s));
            Assert.All(smooth.Samplings, s => Assert.Equal(ImageSampling.Bilinear, s));
        }

        [Fact]
        public void Resolver_MapsEveryKeyword()
        {
            Assert.Equal(ImageSampling.Automatic, ImageRenderingResolver.Resolve(ImageRenderingMode.Auto));
            Assert.Equal(ImageSampling.Nearest, ImageRenderingResolver.Resolve(ImageRenderingMode.Auto, ImageSampling.Nearest));
            Assert.Equal(ImageSampling.Bilinear, ImageRenderingResolver.Resolve(ImageRenderingMode.Smooth, ImageSampling.Nearest));
            Assert.Equal(ImageSampling.Bicubic, ImageRenderingResolver.Resolve(ImageRenderingMode.HighQuality));
            Assert.Equal(ImageSampling.Nearest, ImageRenderingResolver.Resolve(ImageRenderingMode.CrispEdges));
            Assert.Equal(ImageSampling.Pixelated, ImageRenderingResolver.Resolve(ImageRenderingMode.Pixelated));
        }
    }
}
