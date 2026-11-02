using PeachBrackets.Accessibility;

namespace PeachBrackets.Tests;

public class AccessibilityTreeDiffTests
{
    private static async Task<AccessibilityTree> TreeAsync(string body)
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body, "<style>@page { margin: 0 }</style>"), new HtmlDocumentOptions { Media = "screen" });
        return document.GetAccessibilityTree();
    }

    [Fact]
    public async Task TwoTreesOfTheSameDocumentDifferInNothing()
    {
        var tree = await TreeAsync("<h1>Title</h1><p>text</p>");

        var diff = AccessibilityTreeDiff.Compare(tree, tree);
        Assert.True(diff.IsEmpty);
        Assert.Empty(diff.Announcements);
    }

    [Fact]
    public async Task AChangedStateNamesTheStateThatChanged()
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<input id=a type=checkbox aria-label=agree>", "<style>@page { margin: 0 }</style>"), new HtmlDocumentOptions { Media = "screen" });
        var before = document.GetAccessibilityTree();
        document.SetChecked(document.GetElement("a")!, true);

        var diff = AccessibilityTreeDiff.Compare(before, document.GetAccessibilityTree());

        var change = Assert.Single(diff.Changes);
        Assert.Equal((AccessibilityChangeKind.StateChanged, "checked", AccessibilityRole.CheckBox), (change.Kind, change.Property, change.Node.Role));
    }

    [Fact]
    public async Task APartThatCameAndAPartThatWentAreReported()
    {
        var before = await TreeAsync("<p id=a>one</p><p id=b>two</p>");
        var after = await TreeAsync("<p id=a>one</p><p id=b>two</p><button>go</button>");

        var diff = AccessibilityTreeDiff.Compare(before, after);
        Assert.Contains(diff.Changes, c => c.Kind == AccessibilityChangeKind.Added && c.Node.Role == AccessibilityRole.Button);

        var back = AccessibilityTreeDiff.Compare(after, before);
        Assert.Contains(back.Changes, c => c.Kind == AccessibilityChangeKind.Removed && c.Node.Role == AccessibilityRole.Button);
    }

    [Fact]
    public async Task ARenamedPartAndAMovedValueAreReported()
    {
        var before = await TreeAsync("<button aria-label=Save>x</button><progress value=1 max=4></progress>");
        var after = await TreeAsync("<button aria-label=Saved>x</button><progress value=2 max=4></progress>");

        var kinds = AccessibilityTreeDiff.Compare(before, after).Changes.Select(c => c.Kind).ToList();

        Assert.Contains(AccessibilityChangeKind.NameChanged, kinds);
        Assert.Contains(AccessibilityChangeKind.ValueChanged, kinds);
    }

    [Fact]
    public async Task ALiveRegionSaysWhatChangedInIt()
    {
        var before = await TreeAsync("<div id=r aria-live=polite><span>Loading</span></div>");
        var after = await TreeAsync("<div id=r aria-live=polite><span>Done</span></div>");

        var announcement = Assert.Single(AccessibilityTreeDiff.Compare(before, after).Announcements);

        Assert.Equal(("Done", LiveSetting.Polite), (announcement.Text, announcement.Politeness));
    }

    [Fact]
    public async Task AnAtomicRegionSaysAllOfItAndAnAssertiveOneInterrupts()
    {
        var before = await TreeAsync("<div aria-live=assertive aria-atomic=true>Score: <b>1</b></div>");
        var after = await TreeAsync("<div aria-live=assertive aria-atomic=true>Score: <b>2</b></div>");

        var announcement = Assert.Single(AccessibilityTreeDiff.Compare(before, after).Announcements);

        Assert.Equal(LiveSetting.Assertive, announcement.Politeness);
        Assert.Contains("Score", announcement.Text);
        Assert.Contains("2", announcement.Text);
    }

    [Fact]
    public async Task NewContentInALiveRegionIsAnnounced()
    {
        var before = await TreeAsync("<div aria-live=polite><p>first</p></div>");
        var after = await TreeAsync("<div aria-live=polite><p>first</p><p>second</p></div>");

        var announcement = Assert.Single(AccessibilityTreeDiff.Compare(before, after).Announcements);

        Assert.Equal("second", announcement.Text);
    }

    [Fact]
    public async Task ABusyRegionAndANewRegionAndAnUnchangedOneSayNothing()
    {
        var busyBefore = await TreeAsync("<div aria-live=polite aria-busy=true>a</div>");
        var busyAfter = await TreeAsync("<div aria-live=polite aria-busy=true>b</div>");
        Assert.Empty(AccessibilityTreeDiff.Compare(busyBefore, busyAfter).Announcements);

        var none = await TreeAsync("<p>x</p>");
        var created = await TreeAsync("<p>x</p><div aria-live=polite>new</div>");
        Assert.Empty(AccessibilityTreeDiff.Compare(none, created).Announcements);

        var same = await TreeAsync("<div aria-live=polite>same</div>");
        Assert.Empty(AccessibilityTreeDiff.Compare(same, await TreeAsync("<div aria-live=polite>same</div>")).Announcements);
    }

    [Fact]
    public async Task ARegionOutsideAnyLiveRegionSaysNothingWhenItsTextChanges()
    {
        var before = await TreeAsync("<p>one</p>");
        var after = await TreeAsync("<p>two</p>");

        Assert.Empty(AccessibilityTreeDiff.Compare(before, after).Announcements);
    }

    [Fact]
    public async Task ARegionInsideAnotherIsSpokenWithIt()
    {
        var before = await TreeAsync("<div aria-live=polite><div aria-live=polite>a</div></div>");
        var after = await TreeAsync("<div aria-live=polite><div aria-live=polite>b</div></div>");

        Assert.Single(AccessibilityTreeDiff.Compare(before, after).Announcements);
    }

    [Fact]
    public void ComparingNothingThrows()
    {
        Assert.Throws<ArgumentNullException>(() => AccessibilityTreeDiff.Compare(null!, null!));
    }
}
