using PeachBrackets.Avalonia.Tests.TestSupport;
using SkiaSharp;

namespace PeachBrackets.Avalonia.Tests;

public class ViewportConsistencyTests
{
    // A fixed bar is drawn where the document's viewport says, and the page where the scroll says: if a frame is painted from a scroll that has since moved on, the two
    // disagree and the bar is drawn that many pixels away from the top, which looks like flicker while scrolling.
    private const string Page =
        "<html><body style='margin:0'>"
        + "<div style='position:fixed;top:0;left:0;right:0;height:40px;background:#ff0000'></div>"
        + "<div style='height:600px;background:#eeeeee'></div><p id='target' style='margin:0;height:600px;background:#dddddd'>far</p>"
        + "<div style='height:1200px;background:#eeeeee'></div></body></html>";

    [Fact]
    public async Task AFrameQueuedBeforeAScrollStillDrawsTheFixedBarAtTheTop()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page, 300, 300);
            view.TilesOnCpuForTests = true;

            // The frame as it was recorded: before the scroll.
            var staleFrame = view.CapturePaintForTests();

            // The page then scrolls on, as it does while a wheel or a touchpad is still turning and the render thread has not caught up.
            Assert.True(view.ScrollToElement("target"));

            using var bitmap = new SKBitmap(300, 300);
            using var canvas = new SKCanvas(bitmap);
            await Task.Run(() => staleFrame(canvas, 300, 300));

            // The bar holds the top of the view in the frame that was queued before the scroll, as in one queued after it.
            foreach (var y in new[] { 2, 20, 37 })
            {
                var pixel = bitmap.GetPixel(150, y);
                Assert.True(pixel.Red > 200 && pixel.Green < 60 && pixel.Blue < 60, $"the fixed bar should be at the top of the view (y={y}), got {pixel}");
            }

            // And below it the page is the part the scroll now shows, not the part it showed when the frame was recorded.
            var below = bitmap.GetPixel(150, 150);
            Assert.True(below.Red is > 200 and < 235, $"the page below the bar should be the target block, got {below}");
            window.Close();
        });
    }
}
