using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using PeachBrackets.Avalonia.Tests.TestSupport;

namespace PeachBrackets.Avalonia.Tests;

public class FlyoutFocusTests
{
    private const string Page =
        "<html><body style='margin:0;font-size:14px'>" +
        "<input id='a' style='position:absolute;left:10px;top:10px;width:200px;height:24px'>" +
        "<select id='sel' style='position:absolute;left:10px;top:60px;width:120px;height:24px'><option value='x'>X</option><option value='y' selected>Y</option></select>" +
        "</body></html>";

    [Fact]
    public Task TheFieldMenuTakesTheKeyboardFocusAndTheFieldKeepsEditing() => HeadlessSession.Run(async () =>
    {
        var (window, view) = await HeadlessSession.ShowAsync(Page);
        await HtmlViewInput.ClickAtAsync(window, view, new global::Avalonia.Point(20, 22));
        await view.PlacementTask;
        window.KeyTextInput("abc");
        window.MouseDown(new global::Avalonia.Point(150, 22), MouseButton.Right);
        await view.PlacementTask;
        await HtmlViewInput.SettleAsync(view);

        Assert.True(view.HasOpenFlyout);
        // The menu really has the keyboard (so its arrow keys work), and that did not end the field's editing.
        Assert.False(view.IsFocused);
        Assert.NotNull(view.Document!.GetEditState());
        Assert.Equal("a", view.FocusedNode?.ElementId);

        // Escape closes the menu from the keyboard, and the focus is back on the view with the field still being edited.
        await HtmlViewInput.KeyAsync(window, view, Key.Escape);
        Assert.False(view.HasOpenFlyout);
        Assert.True(view.IsFocused);
        Assert.Equal("abc", view.Document.GetEditState()!.Value.Text);
        window.KeyTextInput("d");
        Assert.Equal("abcd", view.Document.GetEditState()!.Value.Text);
        window.Close();
    });

    [Fact]
    public Task TheArrowKeysAndEnterWorkInTheFieldMenu() => HeadlessSession.Run(async () =>
    {
        var (window, view) = await HeadlessSession.ShowAsync(Page);
        await HtmlViewInput.ClickAtAsync(window, view, new global::Avalonia.Point(20, 22));
        await view.PlacementTask;
        window.KeyTextInput("hello");
        window.MouseDown(new global::Avalonia.Point(150, 22), MouseButton.Right);
        await view.PlacementTask;
        await HtmlViewInput.SettleAsync(view);
        var menu = Assert.IsType<MenuFlyout>(view.OpenFlyout);

        // Down until "Select all" (the last item) has the focus, then Enter chooses it.
        for (var i = 0; i < 6 && !(window.FocusManager?.GetFocusedElement() is MenuItem { Header: "Select all" }); i++)
            await HtmlViewInput.KeyAsync(window, view, Key.Down);

        Assert.IsType<MenuItem>(window.FocusManager!.GetFocusedElement());
        Assert.Equal("Select all", ((MenuItem)window.FocusManager.GetFocusedElement()!).Header);
        await HtmlViewInput.KeyAsync(window, view, Key.Enter);
        await HtmlViewInput.SettleAsync(view);

        Assert.False(view.HasOpenFlyout);
        Assert.NotNull(menu);
        var state = view.Document!.GetEditState()!.Value;
        Assert.Equal((0, 5), (state.SelectionStart, state.SelectionEnd));
        Assert.True(view.IsFocused);
        window.Close();
    });

    [Fact]
    public Task ChoosingFromTheSelectMenuKeepsTheDocumentFocusAndAppliesToTheSelect() => HeadlessSession.Run(async () =>
    {
        var (window, view) = await HeadlessSession.ShowAsync(Page);
        await HtmlViewInput.ClickAtAsync(window, view, new global::Avalonia.Point(20, 22));
        await view.PlacementTask;
        Assert.Equal("a", view.FocusedNode?.ElementId);

        await HtmlViewInput.ClickAtAsync(window, view, HtmlViewInput.CenterOf(view, "sel"));
        await HtmlViewInput.SettleAsync(view);
        Assert.True(view.HasOpenFlyout);
        Assert.NotNull(view.FocusedNode);

        var menu = Assert.IsType<MenuFlyout>(view.OpenFlyout);
        menu.Items.OfType<MenuItem>().First().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        menu.Hide(); // a real click on an item closes the menu
        await HtmlViewInput.SettleAsync(view);

        Assert.Equal("x", HtmlViewInput.State(view, "sel").Value);
        Assert.False(view.HasOpenFlyout);
        Assert.True(view.IsFocused);
        Assert.NotNull(view.FocusedNode);
        window.Close();
    });

    [Fact]
    public Task APickerWritesIntoTheControlThatOpenedItEvenWhenTheFocusWentToItsFlyout() => HeadlessSession.Run(async () =>
    {
        var (window, view) = await HeadlessSession.ShowAsync(
            "<input id='d' type='date' value='2024-02-10' style='position:absolute;left:10px;top:10px;width:160px;height:24px'>", 400, 300);
        await HtmlViewInput.ClickAsync(window, view, "d");
        await HtmlViewInput.SettleAsync(view);
        var flyout = Assert.IsType<Flyout>(view.OpenFlyout);
        Assert.True(view.HasOpenFlyout);
        var calendar = Assert.IsType<Calendar>(flyout.Content);
        calendar.SelectedDate = new DateTime(2024, 3, 5);
        await HtmlViewInput.SettleAsync(view);

        Assert.Equal("2024-03-05", HtmlViewInput.State(view, "d").Value);
        Assert.False(view.HasOpenFlyout);
        window.Close();
    });
}
