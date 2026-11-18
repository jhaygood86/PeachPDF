using PeachDrawing.Core;
using PeachDrawing.PDF;

namespace PeachReader.Core.Tests;

public class OutlineAndLinkTests
{
    private static PdfBookmark Entry(int level, string title, int? page, double top = 0, string? uri = null, bool open = false) =>
        new(level, title, open) { PageIndex = page, TopPoints = top, Uri = uri };

    [Fact]
    public void Build_NestsEntriesUnderTheNearestEarlierEntryOfALowerLevel()
    {
        var roots = OutlineTree.Build([
            Entry(1, "A", 0), Entry(2, "A.1", 1), Entry(3, "A.1.a", 1), Entry(2, "A.2", 2), Entry(1, "B", 3),
        ]);

        Assert.Equal(["A", "B"], roots.Select(r => r.Title));
        Assert.Equal(["A.1", "A.2"], roots[0].Children.Select(c => c.Title));
        Assert.Equal(["A.1.a"], roots[0].Children[0].Children.Select(c => c.Title));
        Assert.Same(roots[0].Children[0], roots[0].Children[0].Children[0].Parent);
        Assert.Null(roots[1].Parent);
    }

    [Fact]
    public void Build_CopesWithAFileThatSkipsLevels()
    {
        var roots = OutlineTree.Build([Entry(1, "Top", 0), Entry(3, "Deep", 1), Entry(2, "Middle", 2)]);

        var top = Assert.Single(roots);
        Assert.Equal(["Deep", "Middle"], top.Children.Select(c => c.Title));
    }

    [Fact]
    public void Build_KeepsWhatEachEntryLeadsTo()
    {
        var roots = OutlineTree.Build([Entry(1, "Page", 4, 120.5, open: true), Entry(1, "Web", null, uri: "https://example.com"), Entry(1, "Nowhere", null)]);

        Assert.Equal(new ReaderLocation(4, 120.5), roots[0].Location);
        Assert.True(roots[0].IsOpen);
        Assert.Null(roots[1].Location);
        Assert.Equal("https://example.com", roots[1].Uri);
        Assert.Null(roots[2].PageIndex);
        Assert.Empty(OutlineTree.Build([]));
        Assert.Throws<ArgumentNullException>(() => OutlineTree.Build(null!));
    }

    [Fact]
    public void Flatten_ListsEachEntryBeforeItsChildren()
    {
        var roots = OutlineTree.Build([Entry(1, "A", 0), Entry(2, "A.1", 0), Entry(1, "B", 1)]);

        Assert.Equal(["A", "A.1", "B"], OutlineTree.Flatten(roots).Select(n => n.Title));
    }

    [Theory]
    [InlineData(0, "Intro")]
    [InlineData(1, "Intro")]
    [InlineData(2, "Part")]
    [InlineData(3, "Part")]
    [InlineData(5, "Last")]
    public void CurrentFor_IsTheSectionThePageBelongsTo(int page, string expected)
    {
        var roots = OutlineTree.Build([Entry(1, "Intro", 0), Entry(2, "Part", 2), Entry(1, "Web", null, uri: "https://x.test"), Entry(1, "Last", 5)]);

        Assert.Equal(expected, OutlineTree.CurrentFor(roots, page)?.Title);
    }

    [Fact]
    public void CurrentFor_IsNothingBeforeTheFirstEntry()
    {
        Assert.Null(OutlineTree.CurrentFor(OutlineTree.Build([Entry(1, "Later", 3)]), 1));
        Assert.Null(OutlineTree.CurrentFor([], 0));
    }

    [Theory]
    [InlineData("https://example.com/a", true)]
    [InlineData("http://example.com", true)]
    [InlineData("mailto:someone@example.com", true)]
    [InlineData("tel:+15555550100", true)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("ms-settings:privacy", false)]
    [InlineData("relative/path.pdf", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsLaunchable_AllowsOnlySafeSchemes(string? uri, bool expected)
    {
        Assert.Equal(expected, LinkResolver.IsLaunchable(uri));
    }

    [Fact]
    public void Hit_PicksTheLastLinkOfOverlappingOnes_AndBlocksUnsafeAddresses()
    {
        var below = new PdfSourceLink(new Rect(0, 0, 100, 100), "https://below.test", null);
        var above = new PdfSourceLink(new Rect(40, 40, 20, 20), null, new PdfSourceDestination(2, 75));
        var unsafeLink = new PdfSourceLink(new Rect(200, 0, 50, 50), "file:///c:/secret", null);
        IReadOnlyList<PdfSourceLink> links = [below, above, unsafeLink];

        Assert.Equal(new LinkTarget(LinkKind.Internal, new ReaderLocation(2, 75)), LinkResolver.Hit(links, new PaintPoint(50, 50)));
        Assert.Equal(new LinkTarget(LinkKind.External, Uri: "https://below.test"), LinkResolver.Hit(links, new PaintPoint(10, 10)));
        Assert.Equal(new LinkTarget(LinkKind.Blocked, Uri: "file:///c:/secret"), LinkResolver.Hit(links, new PaintPoint(210, 10)));
        Assert.Equal(LinkTarget.None, LinkResolver.Hit(links, new PaintPoint(150, 150)));
        Assert.Throws<ArgumentNullException>(() => LinkResolver.Hit(null!, default));
    }

    [Fact]
    public void History_GoesBackAndForwardAndForgetsForwardAfterANewJump()
    {
        var history = new NavigationHistory();
        Assert.False(history.CanGoBack);
        Assert.Null(history.GoBack(new ReaderLocation(0)));
        Assert.Null(history.GoForward(new ReaderLocation(0)));

        history.Navigated(new ReaderLocation(0, 10));
        history.Navigated(new ReaderLocation(3));
        Assert.True(history.CanGoBack);

        Assert.Equal(new ReaderLocation(3), history.GoBack(new ReaderLocation(7)));
        Assert.Equal(new ReaderLocation(0, 10), history.GoBack(new ReaderLocation(3)));
        Assert.False(history.CanGoBack);
        Assert.True(history.CanGoForward);
        Assert.Equal(new ReaderLocation(3), history.GoForward(new ReaderLocation(0, 10)));

        history.Navigated(new ReaderLocation(3));        // a new jump while there was somewhere to go forward
        Assert.False(history.CanGoForward);

        history.Clear();
        Assert.False(history.CanGoBack);
    }

    [Fact]
    public void History_IgnoresARepeatedPlaceAndKeepsAtMostAHundred()
    {
        var history = new NavigationHistory();
        history.Navigated(new ReaderLocation(1));
        history.Navigated(new ReaderLocation(1));
        Assert.NotNull(history.GoBack(new ReaderLocation(2)));
        Assert.False(history.CanGoBack);

        for (int i = 0; i < 150; i++) history.Navigated(new ReaderLocation(i));
        int steps = 0;
        while (history.GoBack(new ReaderLocation(999)) is not null) steps++;
        Assert.Equal(100, steps);
    }
}
