namespace PeachBrackets.Tests;

/// <summary><c>HtmlDocument.WarmUpAsync</c> compiles the engine's common paths ahead of the first real document, and never gets in its way.</summary>
public class WarmUpTests
{
    [Fact]
    public async Task WarmingUp_CompletesOnceForTheProcess_AndEveryCallGetsTheSameTask()
    {
        var first = HtmlDocument.WarmUpAsync();
        var second = HtmlDocument.WarmUpAsync();

        Assert.Same(first, second);
        await first.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        Assert.True(first.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ADocumentLoadedWhileTheWarmUpRuns_IsLaidOutTheSameAsOneLoadedAfterIt()
    {
        const string body = "<div id=a style='display: flex; gap: 4pt'><span style='width: 50pt'>one</span><span style='width: 70pt'>two</span></div><p id=p>text that wraps</p>";

        var during = HtmlDocument.WarmUpAsync();
        var (_, before) = await TestDocument.LoadAsync(TestDocument.Wrap(body), new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 300 });
        await during;
        var (_, after) = await TestDocument.LoadAsync(TestDocument.Wrap(body), new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 300 });

        Assert.Equal(before.GetContentHeight(0), after.GetContentHeight(0));
        Assert.Equal(before.GetElementBounds("a"), after.GetElementBounds("a"));
        Assert.Equal(before.GetElementBounds("p"), after.GetElementBounds("p"));
    }
}
