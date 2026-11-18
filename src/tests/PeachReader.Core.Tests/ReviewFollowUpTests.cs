using PeachDrawing.PDF;

namespace PeachReader.Core.Tests;

/// <summary>What the review of the reader found: addresses from the table of contents get the same allowlist as links, an empty document does not crash a zoom, and selection is safe to read while it changes.</summary>
public class ReviewFollowUpTests
{
    [Theory]
    [InlineData("https://example.com", LinkKind.External)]
    [InlineData("mailto:a@example.com", LinkKind.External)]
    [InlineData("file:///c:/windows/system32/calc.exe", LinkKind.Blocked)]
    [InlineData(@"\\server\share\x.exe", LinkKind.Blocked)]
    [InlineData("ms-settings:privacy", LinkKind.Blocked)]
    [InlineData("javascript:alert(1)", LinkKind.Blocked)]
    [InlineData("", LinkKind.None)]
    [InlineData("   ", LinkKind.None)]
    [InlineData(null, LinkKind.None)]
    public void ForUri_GivesAnAddressFromTheTableOfContentsTheSameAllowlistAsALink(string? uri, LinkKind expected)
    {
        var target = LinkResolver.ForUri(uri);

        Assert.Equal(expected, target.Kind);
        if (expected is LinkKind.External or LinkKind.Blocked) Assert.Equal(uri, target.Uri);
    }

    [Fact]
    public void AnOutlineEntryThatOpensAnUnsafeAddress_IsBlockedWhenItsAddressIsChecked()
    {
        var roots = OutlineTree.Build([new PdfBookmark(1, "Open calculator", false) { Uri = "file:///c:/windows/system32/calc.exe" }]);

        Assert.Equal(LinkKind.Blocked, LinkResolver.ForUri(roots[0].Uri).Kind);
    }

    [Fact]
    public void ZoomingAnEmptyLayout_DoesNotThrow()
    {
        var empty = new PageLayoutModel([], 500, 400);

        Assert.Equal(0, empty.ScrollAfterZoom(0, empty.WithZoom(2), 100));
        Assert.Equal(0, empty.ScrollAfterZoom(300, empty.WithMode(ZoomMode.FitPage), 0));
    }

    [Fact]
    public void ZoomingToALayoutOfAnotherDocument_IsRefusedWithoutThrowing()
    {
        var one = new PageLayoutModel([(100, 100)], 500, 400);
        var two = new PageLayoutModel([(100, 100), (100, 100)], 500, 400);

        Assert.Equal(0, one.ScrollAfterZoom(50, two, 10));
    }

    private static bool Valid(TextPosition start, TextPosition end) =>
        (start, end) == (new TextPosition(0, 2), new TextPosition(0, 2)) || (start, end) == (new TextPosition(0, 2), new TextPosition(0, 5))
        || (start, end) == (new TextPosition(1, 0), new TextPosition(1, 0)) || (start, end) == (new TextPosition(1, 0), new TextPosition(1, 7));

    [Fact]
    public async Task ReadingASelectionWhileAnotherThreadChangesIt_NeverSeesHalfOfOne()
    {
        var bytes = TestDocument.Build([new TestPage(300, 200, TestDocument.Show("A line of text to select", 30)), new TestPage(300, 200, TestDocument.Show("Another page", 30))]);
        var doc = ReaderDocument.Open(bytes);
        var selection = new TextSelectionModel(doc.GetText, doc.PageCount);
        doc.GetText(0);
        doc.GetText(1);
        var torn = 0;
        using var stop = new CancellationTokenSource();

        var reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                // The writer only ever makes these four selections; a torn read would mix an anchor of one with a focus of another.
                if (selection.Range is { } r && !Valid(r.Start, r.End))
                    Interlocked.Increment(ref torn);
            }
        }, TestContext.Current.CancellationToken);

        for (int i = 0; i < 20000; i++)
        {
            selection.Begin(0, 2);
            selection.Extend(0, 5);
            selection.Begin(1, 0);
            selection.Extend(1, 7);
            if (i % 2 == 0) selection.Clear();
        }

        await stop.CancelAsync();
        await reader;

        Assert.Equal(0, torn);
    }
}
