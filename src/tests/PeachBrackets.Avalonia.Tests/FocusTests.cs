using Avalonia.Headless;
using Avalonia.Input;
using PeachBrackets.Accessibility;
using PeachBrackets.Avalonia.Tests.TestSupport;

namespace PeachBrackets.Avalonia.Tests;

public class FocusTests
{
    private const string Page = """
        <html><body style="margin:0">
        <a id="one" href="#end">first</a>
        <button id="two">second</button>
        <input id="three" type="checkbox">
        <p style="height:1500px">filler</p>
        <a id="four" href="https://example.com/">fourth</a>
        <p id="end">end</p>
        </body></html>
        """;

    [Fact]
    public Task TabStepsThroughTheTabStopsInOrder() => HeadlessSession.Run(async () =>
    {
        var (window, view) = await HeadlessSession.ShowAsync(Page, 500, 300);
        view.Focus();
        var seen = new List<string?>();
        for (var i = 0; i < 4; i++)
        {
            await HtmlViewInput.KeyAsync(window, view, Key.Tab);
            seen.Add(view.FocusedNode?.ElementId);
        }

        Assert.Equal(["one", "two", "three", "four"], seen);
        window.Close();
    });

    [Fact]
    public Task ShiftTabStepsBack() => HeadlessSession.Run(async () =>
    {
        var (window, view) = await HeadlessSession.ShowAsync(Page, 500, 300);
        view.Focus();
        await HtmlViewInput.KeyAsync(window, view, Key.Tab);
        await HtmlViewInput.KeyAsync(window, view, Key.Tab);
        Assert.Equal("two", view.FocusedNode?.ElementId);
        await HtmlViewInput.KeyAsync(window, view, Key.Tab, RawInputModifiers.Shift);
        Assert.Equal("one", view.FocusedNode?.ElementId);
        window.Close();
    });

    [Fact]
    public Task TabPastTheLastStopLeavesTheDocumentSoThereIsNoKeyboardTrap() => HeadlessSession.Run(async () =>
    {
        var (window, view) = await HeadlessSession.ShowAsync(Page, 500, 300);
        view.Focus();
        for (var i = 0; i < 4; i++)
            await HtmlViewInput.KeyAsync(window, view, Key.Tab);

        Assert.Equal("four", view.FocusedNode?.ElementId);
        Assert.False(view.StepFocus(forward: true));
        await HtmlViewInput.SettleAsync(view);
        Assert.Null(view.FocusedNode);
        window.Close();
    });

    [Fact]
    public Task TheFocusedElementIsScrolledIntoView() => HeadlessSession.Run(async () =>
    {
        var (window, view) = await HeadlessSession.ShowAsync(Page, 500, 300);
        view.Focus();
        for (var i = 0; i < 4; i++)
            await HtmlViewInput.KeyAsync(window, view, Key.Tab);

        Assert.True(view.ScrollOffsetY > 500, $"the view should have scrolled to the fourth link, offset {view.ScrollOffsetY}");
        window.Close();
    });

    [Fact]
    public Task EnterOnAFocusedLinkFollowsIt() => HeadlessSession.Run(async () =>
    {
        var (window, view) = await HeadlessSession.ShowAsync(Page, 500, 300);
        HtmlLinkActivatedEventArgs? activated = null;
        view.LinkActivated += (_, e) =>
        {
            activated = e;
            e.Cancel = true;
        };
        view.Focus();
        await HtmlViewInput.KeyAsync(window, view, Key.Tab);
        await HtmlViewInput.KeyAsync(window, view, Key.Enter);
        Assert.NotNull(activated);
        Assert.Equal("end", activated.Destination);
        window.Close();
    });

    [Fact]
    public Task SpaceAndEnterActivateAFocusedCheckboxAndButton() => HeadlessSession.Run(async () =>
    {
        var (window, view) = await HeadlessSession.ShowAsync("<form><input id='c' type='checkbox'><button id='b' type='submit'>go</button></form>", 500, 300);
        var submissions = 0;
        view.FormSubmitting += (_, _) => submissions++;
        view.Focus();
        await HtmlViewInput.KeyAsync(window, view, Key.Tab);
        await HtmlViewInput.KeyAsync(window, view, Key.Space);
        Assert.True(HtmlViewInput.State(view, "c").Checked);
        await HtmlViewInput.KeyAsync(window, view, Key.Tab);
        Assert.Equal(AccessibilityRole.Button, view.FocusedNode?.Role);
        await HtmlViewInput.KeyAsync(window, view, Key.Enter);
        Assert.Equal(1, submissions);
        window.Close();
    });

    [Fact]
    public Task FocusChangedIsRaisedAndLosingKeyboardFocusClearsTheDocument() => HeadlessSession.Run(async () =>
    {
        var (window, view) = await HeadlessSession.ShowAsync(Page, 500, 300);
        var raised = 0;
        view.FocusChanged += (_, _) => raised++;
        view.Focus();
        await HtmlViewInput.KeyAsync(window, view, Key.Tab);
        Assert.True(raised > 0);
        Assert.NotNull(view.FocusedNode);

        // Window.Focus() leaves the view's keyboard focus alone (it only focuses the window itself); taking the focus away is what loses it.
        Assert.True(view.IsFocused);
        window.FocusManager!.Focus(null);
        Assert.False(view.IsFocused);
        await HtmlViewInput.SettleAsync(view);
        Assert.Null(view.FocusedNode);
        window.Close();
    });
}
