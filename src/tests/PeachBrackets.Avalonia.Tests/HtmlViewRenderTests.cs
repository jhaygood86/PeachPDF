using PeachBrackets.Avalonia.Tests.TestSupport;

namespace PeachBrackets.Avalonia.Tests;

public class HtmlViewRenderTests
{
    [Fact]
    public async Task ADocumentLoadsAndPaintsItsBackground()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync("<html><body style='margin:0;background:#cc3333'><p style='margin:0;height:600px'>hello</p></body></html>");
            Assert.NotNull(view.Document);
            Assert.True(view.ContentHeight >= 599);
            var frame = HeadlessSession.Capture(window);
            Assert.True(view.FramesPainted > 0);
            var (r, g, b) = HeadlessSession.PixelAt(frame, 350, 250);
            Assert.True(r > 180 && g < 90 && b < 90, $"expected the page background, got {r},{g},{b}");
            window.Close();
        });
    }
}
