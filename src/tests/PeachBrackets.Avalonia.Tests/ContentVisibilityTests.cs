using PeachBrackets.Avalonia.Tests.TestSupport;
using PeachBrackets.Updates;

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

    private static readonly UpdateResult Nothing = new(false, null, []);

    [Fact]
    public async Task AnUpdateThatComesWhileOneRunsIsDoneAgainWhenTheFirstHasFinished()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page, 400, 300);
            var gate = new TaskCompletionSource();
            var calls = 0;
            view.ContentVisibilityUpdater = async (_, _) =>
            {
                if (++calls == 1)
                    await gate.Task;

                return Nothing;
            };

            var first = view.UpdateContentVisibilityAsync(view.Document!);
            await view.UpdateContentVisibilityAsync(view.Document!);
            Assert.Equal(1, calls);

            gate.SetResult();
            await first;
            Assert.Equal(2, calls);
            window.Close();
        });
    }

    [Fact]
    public async Task AnUpdateThatChangedNothingLeavesTheViewWhereItIs()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page, 400, 300);
            view.ScrollToElement("far");
            await HtmlViewInput.SettleAsync(view);
            var scroll = view.ScrollPixels;
            var height = view.ContentHeight;
            view.ContentVisibilityUpdater = (_, _) => ValueTask.FromResult(Nothing);

            await view.UpdateContentVisibilityAsync(view.Document!);
            Assert.Equal(scroll, view.ScrollPixels);
            Assert.Equal(height, view.ContentHeight);
            window.Close();
        });
    }

    [Fact]
    public async Task AnUpdateOfADocumentThatWasReplacedIsNotAppliedAndAFailureOfItIsNotReported()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page, 400, 300);
            var old = view.Document!;
            var failures = 0;
            view.DocumentFailed += (_, _) => failures++;

            var replaced = new TaskCompletionSource();
            view.DocumentLoaded += (_, _) => replaced.TrySetResult();
            view.Html = "<html><body><p id='plain'>Another document.</p></body></html>";
            await replaced.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await HeadlessSession.PumpUntilAsync(() => !view.IsReloading);
            var scroll = view.ScrollPixels;

            // The old document says it changed; it is not the one on show, so the view is not moved to where it looks.
            view.ContentVisibilityUpdater = (_, _) => ValueTask.FromResult(new UpdateResult(true, null, []));
            await view.UpdateContentVisibilityAsync(old);
            Assert.Equal(scroll, view.ScrollPixels);

            view.ContentVisibilityUpdater = (_, _) => throw new ObjectDisposedException("the old document");
            await view.UpdateContentVisibilityAsync(old);
            Assert.Equal(0, failures);
            window.Close();
        });
    }

    [Fact]
    public async Task AnUpdateThatFailsForTheDocumentOnShowIsReported()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page, 400, 300);
            Exception? failure = null;
            view.DocumentFailed += (_, e) => failure = e.Exception;
            view.ContentVisibilityUpdater = (_, _) => throw new InvalidOperationException("the update failed");

            await view.UpdateContentVisibilityAsync(view.Document!);
            Assert.Equal("the update failed", failure?.Message);

            // The failure does not leave the view refusing every later update.
            var calls = 0;
            view.ContentVisibilityUpdater = (_, _) =>
            {
                calls++;
                return ValueTask.FromResult(Nothing);
            };
            await view.UpdateContentVisibilityAsync(view.Document!);
            Assert.Equal(1, calls);
            window.Close();
        });
    }

    [Fact]
    public async Task AViewThatLeftTheTreeHasNothingToUpdateWith()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page, 400, 300);
            var document = view.Document!;
            var calls = 0;
            view.ContentVisibilityUpdater = (_, _) =>
            {
                calls++;
                return ValueTask.FromResult(Nothing);
            };

            window.Content = null;
            await HtmlViewInput.SettleAsync(view);
            await view.UpdateContentVisibilityAsync(document);
            Assert.Equal(0, calls);
            window.Close();
        });
    }
}
