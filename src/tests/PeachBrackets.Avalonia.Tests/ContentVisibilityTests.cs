using PeachBrackets.Avalonia.Tests.TestSupport;

namespace PeachBrackets.Avalonia.Tests;

public class ContentVisibilityTests
{
    // The section far down is skipped until the view nears it: skipped it is as tall as its intrinsic size, shown it is as tall as what it holds.
    private const string Page =
        "<html><body style='margin:0'>" +
        "<div style='height:2400px'>spacer</div>" +
        "<section id='far' style='content-visibility:auto;contain-intrinsic-size:auto 100px'><div id='inner' style='height:1200px'>inner</div></section>" +
        "</body></html>";

    [Fact]
    public async Task ASkippedSectionIsLaidOutOnceTheViewScrollsNearIt()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page, 400, 300);
            await HtmlViewInput.SettleAsync(view);
            var skipped = view.ContentHeight;
            Assert.True(skipped < 3000, $"the section should be skipped at first, the page is {skipped} tall");

            view.ScrollToElement("far");
            Assert.True(await HeadlessSession.PumpUntilAsync(() => view.ContentHeight > skipped + 800), $"the section should be shown once it is in view, the page is {view.ContentHeight} tall");
            Assert.True(view.ScrollPixels.Y > 1500, "the view stays where the document says it is looking");
            window.Close();
        });
    }
}
