using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Threading;
using PeachBrackets.Avalonia.Tests.TestSupport;

namespace PeachBrackets.Avalonia.Tests;

public class AutomationProviderTests
{
    private const string Page = """
        <html><body style='margin:0'>
        <form id='f'>
          <label for='t'>Name</label>
          <input id='t' name='n' value='abc' aria-keyshortcuts='Alt+N' accesskey='n' aria-description='Your name'>
          <input id='ro' readonly value='ro'>
          <input id='dis' disabled value='d'>
          <input id='bad' aria-invalid='true' value='x'>
          <input id='r' type='range' min='0' max='10' value='3'>
          <progress id='p' value='3' max='10'></progress>
          <input type='radio' id='rd' name='g'>
          <input type='checkbox' id='c'>
          <button id='b'>Go</button>
        </form>
        <a id='l' href='#x'>link</a>
        <ul id='list' style='overflow:auto;height:50px;width:200px'><li>1</li><li>2</li><li>3</li><li>4</li><li>5</li><li>6</li><li>7</li><li>8</li><li>9</li><li>10</li></ul>
        <div style='height:1500px'></div><h2 id='x'>end</h2>
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

    private static HtmlNodeAutomationPeer PeerOf(AutomationPeer root, string elementId) =>
        Walk(root).OfType<HtmlNodeAutomationPeer>().First(p => p.Node.ElementId == elementId);

    private static async Task SettleAsync(HtmlView view)
    {
        await HtmlViewInput.SettleAsync(view);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public async Task APeerReportsWhatThePageSaysAboutItsPart()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page, 500, 400);
            var root = ControlAutomationPeer.CreatePeerForElement(view);

            var name = PeerOf(root, "t");
            Assert.Equal("Alt+N", name.GetAcceleratorKey());
            Assert.Equal("n", name.GetAccessKey());
            Assert.Equal("Your name", name.GetHelpText());
            Assert.Equal(name.Node.Id, name.GetAutomationId());
            Assert.Equal("HtmlNode", name.GetClassName());
            Assert.Equal("TextBox", name.GetItemType());
            Assert.True(name.IsEnabled());
            Assert.True(name.IsKeyboardFocusable());
            Assert.True(name.IsContentElement());
            Assert.True(name.IsControlElement());
            Assert.False(name.HasKeyboardFocus());
            Assert.NotNull(name.GetParent());
            Assert.False(name.ShowContextMenu());
            Assert.Equal(string.Empty, name.GetItemStatus());

            Assert.False(PeerOf(root, "dis").IsEnabled());
            Assert.Equal("invalid", PeerOf(root, "bad").GetItemStatus());
            window.Close();
        });
    }

    [Fact]
    public async Task AFieldIsLabelledByItsLabelAndFocusingItFocusesThePage()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page, 500, 400);
            var root = ControlAutomationPeer.CreatePeerForElement(view);
            var field = PeerOf(root, "t");

            field.SetFocus();
            await SettleAsync(view);
            Assert.True(field.HasKeyboardFocus());
            Assert.Equal("t", view.Document!.FocusedNode?.ElementId);

            // A part that cannot take the focus is only brought into view.
            var heading = Walk(root).OfType<HtmlNodeAutomationPeer>().First(p => p.Node.ElementId == "x");
            Assert.False(heading.IsKeyboardFocusable());
            heading.SetFocus();
            Assert.True(view.ScrollPixels.Y > 0);

            view.ScrollToElement("t");
            heading.BringIntoView();
            Assert.True(view.ScrollPixels.Y > 100);
            window.Close();
        });
    }

    [Fact]
    public async Task InvokingALinkFollowsItAndInvokingAButtonSubmitsItsForm()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page, 500, 400);
            var root = ControlAutomationPeer.CreatePeerForElement(view);
            string? destination = null;
            view.LinkActivated += (_, e) =>
            {
                destination = e.Destination;
                e.Cancel = true;
            };
            string? submitted = null;
            view.FormSubmitting += (_, e) => submitted = e.Submission.Serialize().ToString();

            PeerOf(root, "l").GetProvider<IInvokeProvider>()!.Invoke();
            await SettleAsync(view);
            Assert.NotNull(destination);

            PeerOf(root, "b").GetProvider<IInvokeProvider>()!.Invoke();
            await SettleAsync(view);
            Assert.NotNull(submitted);
            window.Close();
        });
    }

    [Fact]
    public async Task CheckBoxesAndRadioButtonsToggleThroughTheirProvider()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page, 500, 400);
            var root = ControlAutomationPeer.CreatePeerForElement(view);

            var box = PeerOf(root, "c").GetProvider<IToggleProvider>()!;
            Assert.Equal(ToggleState.Off, box.ToggleState);
            box.Toggle();
            await SettleAsync(view);
            Assert.Equal(ToggleState.On, PeerOf(root, "c").GetProvider<IToggleProvider>()!.ToggleState);
            Assert.True(HtmlViewInput.State(view, "c").Checked);

            PeerOf(root, "rd").GetProvider<IToggleProvider>()!.Toggle();
            await SettleAsync(view);
            Assert.True(HtmlViewInput.State(view, "rd").Checked);

            // Parts that are not toggles offer no such provider.
            Assert.Null(PeerOf(root, "t").GetProvider<IToggleProvider>());
            window.Close();
        });
    }

    [Fact]
    public async Task ATextFieldReadsAndTakesItsValueThroughTheValueProvider()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page, 500, 400);
            var root = ControlAutomationPeer.CreatePeerForElement(view);

            var field = PeerOf(root, "t").GetProvider<IValueProvider>()!;
            Assert.Equal("abc", field.Value);
            Assert.False(field.IsReadOnly);
            field.SetValue("hello");
            await SettleAsync(view);
            Assert.Equal("hello", HtmlViewInput.State(view, "t").Value);
            Assert.Equal("hello", PeerOf(root, "t").GetProvider<IValueProvider>()!.Value);

            Assert.True(PeerOf(root, "ro").GetProvider<IValueProvider>()!.IsReadOnly);
            Assert.True(PeerOf(root, "dis").GetProvider<IValueProvider>()!.IsReadOnly);
            Assert.Null(PeerOf(root, "b").GetProvider<IValueProvider>());
            window.Close();
        });
    }

    [Fact]
    public async Task ASliderIsARangeThatCanBeSetAndAProgressBarOnlyReadsOne()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page, 500, 400);
            var root = ControlAutomationPeer.CreatePeerForElement(view);

            var slider = PeerOf(root, "r").GetProvider<IRangeValueProvider>()!;
            Assert.Equal(3, slider.Value);
            Assert.Equal(0, slider.Minimum);
            Assert.Equal(10, slider.Maximum);
            Assert.Equal(0.1, slider.SmallChange, 3);
            Assert.Equal(1, slider.LargeChange, 3);
            Assert.False(slider.IsReadOnly);
            slider.SetValue(7);
            await SettleAsync(view);
            Assert.Equal("7", HtmlViewInput.State(view, "r").Value);
            Assert.Equal(7, PeerOf(root, "r").GetProvider<IRangeValueProvider>()!.Value);

            var progress = PeerOf(root, "p").GetProvider<IRangeValueProvider>()!;
            Assert.Equal(3, progress.Value);
            Assert.True(progress.IsReadOnly);
            progress.SetValue(9);
            await SettleAsync(view);
            Assert.Equal(3, PeerOf(root, "p").GetProvider<IRangeValueProvider>()!.Value);
            window.Close();
        });
    }

    [Fact]
    public async Task AScrollContainerScrollsThroughItsOwnProvider()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page, 500, 400);
            var root = ControlAutomationPeer.CreatePeerForElement(view);
            var list = PeerOf(root, "list").GetProvider<IScrollProvider>();
            Assert.NotNull(list);
            Assert.True(list.VerticallyScrollable);
            Assert.False(list.HorizontallyScrollable);
            Assert.Equal(0, list.VerticalScrollPercent, 1);
            Assert.True(list.VerticalViewSize < 100);
            Assert.Equal(-1, list.HorizontalScrollPercent);
            Assert.Equal(100, list.HorizontalViewSize);

            list.Scroll(ScrollAmount.NoAmount, ScrollAmount.SmallIncrement);
            await SettleAsync(view);
            Assert.True(view.GetContainerScroll("list")!.Value.Y > 0);

            list.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeIncrement);
            await SettleAsync(view);
            list.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeDecrement);
            list.Scroll(ScrollAmount.NoAmount, ScrollAmount.SmallDecrement);
            await SettleAsync(view);

            list.SetScrollPercent(-1, 100);
            await SettleAsync(view);
            Assert.Equal(100, PeerOf(root, "list").GetProvider<IScrollProvider>()!.VerticalScrollPercent, 1);

            // A part that does not scroll offers no provider.
            Assert.Null(PeerOf(root, "b").GetProvider<IScrollProvider>());
            window.Close();
        });
    }

    [Fact]
    public async Task TheWholeDocumentScrollsThroughTheViewsProvider()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page, 500, 400);
            var root = ControlAutomationPeer.CreatePeerForElement(view);
            var scroll = root.GetProvider<IScrollProvider>()!;
            Assert.True(scroll.VerticallyScrollable);
            Assert.False(scroll.HorizontallyScrollable);
            Assert.Equal(0, scroll.VerticalScrollPercent, 1);
            Assert.Equal(-1, scroll.HorizontalScrollPercent);
            Assert.InRange(scroll.VerticalViewSize, 1, 99);
            Assert.Equal(100, scroll.HorizontalViewSize);

            scroll.Scroll(ScrollAmount.NoAmount, ScrollAmount.SmallIncrement);
            Assert.Equal(40, view.ScrollPixels.Y, 0.5);
            scroll.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeIncrement);
            var afterPage = view.ScrollPixels.Y;
            Assert.True(afterPage > 300);
            scroll.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeDecrement);
            scroll.Scroll(ScrollAmount.NoAmount, ScrollAmount.SmallDecrement);
            Assert.Equal(0, view.ScrollPixels.Y, 0.5);

            scroll.SetScrollPercent(-1, 50);
            Assert.True(view.ScrollPixels.Y > 100);
            Assert.Equal(50, scroll.VerticalScrollPercent, 1);
            scroll.SetScrollPercent(0, -1);
            window.Close();
        });
    }

    [Fact]
    public async Task ScrollingTheViewTellsEveryPartItMoved()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page, 500, 400);
            var root = ControlAutomationPeer.CreatePeerForElement(view);
            var link = PeerOf(root, "l");
            var moved = 0;
            link.PropertyChanged += (_, e) =>
            {
                if (e.Property == AutomationElementIdentifiers.BoundingRectangleProperty)
                    moved++;
            };

            view.ScrollToElement("x");
            Assert.True(moved > 0, "a part is told it is somewhere else when the view scrolls");
            Assert.True(link.IsOffscreen());
            window.Close();
        });
    }
}
