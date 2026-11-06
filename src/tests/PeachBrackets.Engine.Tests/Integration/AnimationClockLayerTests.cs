using PeachBrackets;
using PeachDrawing;
using PeachDrawing.Core;
using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>What the live animation clock does to painting: an animated box is recorded into a layer once and drawn again each frame, not repainted.</summary>
    public class AnimationClockLayerTests
    {
        private const string Html = "<!DOCTYPE html><html><head><style>html,body{margin:0}"
            + "@keyframes spin{from{transform:rotate(0deg)}to{transform:rotate(90deg)}}"
            + "#a{width:40pt;height:40pt;background:rgb(255,0,0);animation:spin 2s linear infinite}</style></head><body><div id=a></div></body></html>";

        private static async Task<(RasterRenderContext Context, HtmlDocument Document)> LoadAsync()
        {
            var context = new RasterRenderContext();
            await using var font = File.OpenRead(BundledFonts.Ttf);
            await context.AddFont(font, "Test Sans");
            var document = await HtmlDocument.LoadAsync(Html, context, new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 200, AnimationsEnabled = true });
            using var measuring = context.CreateCanvas(1, 1);
            await document.LayoutAsync(measuring);
            return (context, document);
        }

        private static async Task PaintAsync(RasterRenderContext context, HtmlDocument document)
        {
            var page = document.Pages[0];
            using var canvas = context.CreateCanvas((int)Math.Ceiling(page.WidthPoints), (int)Math.Ceiling(page.HeightPoints));
            await document.PaintPageAsync(0, canvas);
        }

        [Fact]
        public async Task AnAnimatedBox_IsRecordedOnceAndDrawnAgainEveryFrame()
        {
            var (context, document) = await LoadAsync();

            for (var i = 0; i < 5; i++)
            {
                document.AdvanceAnimations(TimeSpan.FromMilliseconds(100 * i));
                await PaintAsync(context, document);
            }

            Assert.Equal(1, document.RetainedLayersRecorded);
        }

        [Fact]
        public async Task ARestyleOfThePage_RecordsTheLayerAgain()
        {
            var (context, document) = await LoadAsync();
            document.AdvanceAnimations(TimeSpan.Zero);
            await PaintAsync(context, document);

            using var measuring = context.CreateCanvas(1, 1);
            await document.SetPointerStateAsync(0, 5, 5, false, measuring);
            await document.ClearPointerStateAsync(measuring);
            document.AdvanceAnimations(TimeSpan.FromMilliseconds(50));
            await PaintAsync(context, document);

            Assert.True(document.RetainedLayersRecorded >= 1);
        }

        [Fact]
        public async Task ADocumentThatIsPainted_WithoutTheClock_RecordsNoLayers()
        {
            var context = new RasterRenderContext();
            await using var font = File.OpenRead(BundledFonts.Ttf);
            await context.AddFont(font, "Test Sans");
            var document = await HtmlDocument.LoadAsync(Html, context, new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 200 });
            using var measuring = context.CreateCanvas(1, 1);
            await document.LayoutAsync(measuring);

            await PaintAsync(context, document);

            Assert.Equal(0, document.RetainedLayersRecorded);
        }
    }
}
