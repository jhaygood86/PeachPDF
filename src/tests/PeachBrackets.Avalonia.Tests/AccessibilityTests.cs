using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using ToggleState = Avalonia.Automation.Provider.ToggleState;
using PeachBrackets.Accessibility;
using PeachBrackets.Avalonia.Logic;
using PeachBrackets.Avalonia.Tests.TestSupport;

namespace PeachBrackets.Avalonia.Tests;

public class AccessibilityMappingTests
{
    [Theory]
    [InlineData(AccessibilityRole.Link, AutomationControlType.Hyperlink)]
    [InlineData(AccessibilityRole.Button, AutomationControlType.Button)]
    [InlineData(AccessibilityRole.CheckBox, AutomationControlType.CheckBox)]
    [InlineData(AccessibilityRole.Radio, AutomationControlType.RadioButton)]
    [InlineData(AccessibilityRole.TextBox, AutomationControlType.Edit)]
    [InlineData(AccessibilityRole.Slider, AutomationControlType.Slider)]
    [InlineData(AccessibilityRole.ProgressBar, AutomationControlType.ProgressBar)]
    [InlineData(AccessibilityRole.Table, AutomationControlType.Table)]
    [InlineData(AccessibilityRole.Heading, AutomationControlType.Text)]
    [InlineData(AccessibilityRole.ListItem, AutomationControlType.ListItem)]
    [InlineData(AccessibilityRole.Main, AutomationControlType.Group)]
    public void ARoleMapsToItsControlType(AccessibilityRole role, AutomationControlType expected) =>
        Assert.Equal(expected, AccessibilityMapping.ToControlType(role));

    [Fact]
    public void OnlyLandmarkRolesHaveALandmarkType()
    {
        Assert.Equal(AutomationLandmarkType.Navigation, AccessibilityMapping.ToLandmarkType(AccessibilityRole.Navigation));
        Assert.Equal(AutomationLandmarkType.Banner, AccessibilityMapping.ToLandmarkType(AccessibilityRole.Banner));
        Assert.Null(AccessibilityMapping.ToLandmarkType(AccessibilityRole.Paragraph));
    }

    [Fact]
    public void LiveSettingsAndToggleStatesMap()
    {
        Assert.Equal(AutomationLiveSetting.Assertive, AccessibilityMapping.ToLiveSetting(LiveSetting.Assertive));
        Assert.Equal(AutomationLiveSetting.Off, AccessibilityMapping.ToLiveSetting(LiveSetting.Off));
        Assert.Equal(ToggleState.On, AccessibilityMapping.ToToggleState(PeachBrackets.Accessibility.ToggleState.True));
        Assert.Equal(ToggleState.Indeterminate, AccessibilityMapping.ToToggleState(PeachBrackets.Accessibility.ToggleState.Mixed));
        Assert.Equal(ToggleState.Off, AccessibilityMapping.ToToggleState(PeachBrackets.Accessibility.ToggleState.None));
    }

    [Fact]
    public void RangeStepsAreAHundredthAndATenthOfTheExtent()
    {
        Assert.Equal((1, 10), AccessibilityMapping.RangeSteps(0, 100));
        Assert.Equal((0, 0), AccessibilityMapping.RangeSteps(5, 5));
    }
}

public class AccessibilityTests
{
    private const string Page = """
        <html><body style='margin:0'>
        <nav aria-label='Site'><a href='#x'>Home</a></nav>
        <main>
          <h2>Title</h2>
          <p>Some text.</p>
          <ul><li>one</li><li>two</li></ul>
          <label><input type='checkbox' id='c'> Agree</label>
          <table><tr><th>A</th><th>B</th></tr><tr><td>1</td><td>2</td></tr></table>
          <div id='live' aria-live='polite'><p>first</p><p id='second' hidden>second</p></div>
        </main>
        </body></html>
        """;

    private static IEnumerable<AutomationPeer> Walk(AutomationPeer peer)
    {
        foreach (var child in peer.GetChildren())
        {
            yield return child;
            foreach (var below in Walk(child))
                yield return below;
        }
    }

    private static AutomationPeer Find(AutomationPeer root, AutomationControlType type, string? name = null) =>
        Walk(root).First(p => p.GetAutomationControlType() == type && (name is null || p.GetName().Contains(name, StringComparison.Ordinal)));

    [Fact]
    public async Task TheViewIsADocumentWithTheBlocksOfThePageBelowIt()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page, 500, 500);
            var peer = ControlAutomationPeer.CreatePeerForElement(view);

            Assert.Equal(AutomationControlType.Document, peer.GetAutomationControlType());
            Assert.Equal("HtmlView", peer.GetClassName());
            Assert.NotEmpty(peer.GetName());

            var heading = Find(peer, AutomationControlType.Text, "Title");
            Assert.Equal(2, heading.GetHeadingLevel());
            Assert.Same(heading.GetParent(), heading.GetParent());

            var link = Find(peer, AutomationControlType.Hyperlink);
            Assert.Equal("Home", link.GetName());
            Assert.NotNull(link.GetProvider<IInvokeProvider>());
            Assert.Null(heading.GetProvider<IInvokeProvider>());

            var nav = Walk(peer).First(p => p.GetLandmarkType() == AutomationLandmarkType.Navigation);
            Assert.Contains("Home", nav.GetChildren().Select(c => c.GetName()).Concat(Walk(nav).Select(c => c.GetName())));
            Assert.Contains(Walk(peer), p => p.GetLandmarkType() == AutomationLandmarkType.Main);
            window.Close();
        });
    }

    [Fact]
    public async Task AListAndATableHaveTheirItemsAsChildren()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page, 500, 500);
            var peer = ControlAutomationPeer.CreatePeerForElement(view);

            var list = Find(peer, AutomationControlType.List);
            var items = Walk(list).Where(p => p.GetAutomationControlType() == AutomationControlType.ListItem).ToList();
            Assert.Equal(2, items.Count);

            var table = Find(peer, AutomationControlType.Table);
            var headers = Walk(table).Where(p => p.GetAutomationControlType() == AutomationControlType.HeaderItem).Select(p => p.GetName()).ToList();
            Assert.Equal(["A", "B"], headers);
            window.Close();
        });
    }

    [Fact]
    public async Task NodesAreWhereTheyArePaintedAndFollowTheScroll()
    {
        await HeadlessSession.Run(async () =>
        {
            var html = "<html><body style='margin:0'><p style='margin:0;height:1000px'>top</p><h1 id='far' style='margin:0'>far</h1></body></html>";
            var (window, view) = await HeadlessSession.ShowAsync(html, 400, 300);
            var peer = ControlAutomationPeer.CreatePeerForElement(view);
            var viewBounds = peer.GetBoundingRectangle();

            var far = Find(peer, AutomationControlType.Text, "far");
            Assert.True(far.IsOffscreen());
            var before = far.GetBoundingRectangle();
            Assert.True(before.Top - viewBounds.Top > 900, $"expected the heading below the first screen, got {before}");

            Assert.True(view.ScrollToElement("far"));
            var after = far.GetBoundingRectangle();
            Assert.True(after.Top < before.Top, $"expected the node to follow the scroll: before {before}, after {after}");
            Assert.False(far.IsOffscreen());
            window.Close();
        });
    }

    [Fact]
    public async Task ACheckBoxTogglesFromAutomationAndTellsItsNewState()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page, 500, 500);
            var peer = ControlAutomationPeer.CreatePeerForElement(view);
            var box = Find(peer, AutomationControlType.CheckBox);
            var toggle = box.GetProvider<IToggleProvider>();
            Assert.NotNull(toggle);
            Assert.Equal(ToggleState.Off, toggle.ToggleState);

            var raised = new List<AutomationPropertyChangedEventArgs>();
            peer.GetChildren();
            box.PropertyChanged += (_, e) => raised.Add(e);
            var tree = view.AccessibilityTree!;
            var node = tree.Root.DescendantsAndSelf().First(n => n.Role == AccessibilityRole.CheckBox);

            Assert.True(view.ToggleFromAutomation(node));

            // The tree was built again, so the peer for the new tree is the one that reports it.
            var fresh = Find(peer, AutomationControlType.CheckBox);
            Assert.Equal(ToggleState.On, fresh.GetProvider<IToggleProvider>()!.ToggleState);
            Assert.NotNull(view.LastAccessibilityDiff);
            Assert.Contains(view.LastAccessibilityDiff!.Changes, c => c.Kind == AccessibilityChangeKind.StateChanged && c.Property == "checked");
            window.Close();
        });
    }

    [Fact]
    public async Task ARestyleRaisesTheChangeAndALiveRegionIsAnnounced()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page, 500, 500);
            var peer = ControlAutomationPeer.CreatePeerForElement(view);
            var children = 0;
            peer.ChildrenChanged += (_, _) => children++;
            var live = Walk(peer).First(p => p.GetLiveSetting() == AutomationLiveSetting.Polite);
            // A region that holds paragraphs is read through them: its own name is only the text it holds directly.
            Assert.Equal(["first"], Walk(live).Where(p => p.GetName().Length > 0).Select(p => p.GetName()));

            // The hidden paragraph is in the document but not in the tree; showing it adds to the live region.
            var document = view.Document!;
            using var measuring = new HtmlView.MeasuringCanvas(view.RenderContext!);
            var result = await document.RemoveAttributeAsync(document.GetElement("second")!, "hidden", measuring.Canvas);
            Assert.True(result.Applied);
            view.ApplyUpdate(result);

            Assert.True(children > 0, "the tree should be told it has other children");
            var diff = view.LastAccessibilityDiff;
            Assert.NotNull(diff);
            Assert.Contains(diff!.Changes, c => c.Kind == AccessibilityChangeKind.Added);
            var announcement = Assert.Single(diff.Announcements);
            Assert.Equal("second", announcement.Text);
            Assert.Contains("second", Walk(Walk(peer).First(p => p.GetLiveSetting() == AutomationLiveSetting.Polite)).Select(p => p.GetName()));
            window.Close();
        });
    }

    [Fact]
    public async Task ARenamedPartRaisesANameChange()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync("<html><body><a id='l' href='#x' aria-label='Old'>link</a></body></html>", 300, 200);
            var peer = ControlAutomationPeer.CreatePeerForElement(view);
            var link = Find(peer, AutomationControlType.Hyperlink);
            Assert.Equal("Old", link.GetName());

            var document = view.Document!;
            using var measuring = new HtmlView.MeasuringCanvas(view.RenderContext!);
            var result = await document.SetAttributeAsync(document.GetElement("l")!, "aria-label", "New", measuring.Canvas);
            view.ApplyUpdate(result);

            Assert.Contains(view.LastAccessibilityDiff!.Changes, c => c.Kind == AccessibilityChangeKind.NameChanged);
            Assert.Equal("New", Find(peer, AutomationControlType.Hyperlink).GetName());
            window.Close();
        });
    }

    [Fact]
    public async Task AChangeThatLeavesTheTreeTheSameRaisesNothing()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page, 500, 500);
            var peer = ControlAutomationPeer.CreatePeerForElement(view);
            var children = 0;
            peer.ChildrenChanged += (_, _) => children++;

            var document = view.Document!;
            using var measuring = new HtmlView.MeasuringCanvas(view.RenderContext!);
            var result = await document.SetAttributeAsync(document.GetElement("c")!, "data-x", "1", measuring.Canvas);
            view.ApplyUpdate(result);

            Assert.True(view.LastAccessibilityDiff is { IsEmpty: true });
            Assert.Equal(0, children);
            window.Close();
        });
    }
}
