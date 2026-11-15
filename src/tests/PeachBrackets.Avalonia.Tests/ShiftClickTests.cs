using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using PeachBrackets.Avalonia.Tests.TestSupport;

namespace PeachBrackets.Avalonia.Tests;

public class ShiftClickTests
{
    private const string Page =
        "<html><body style='margin:0;font-size:16px'>" +
        "<p style='margin:10px'><span id='w1'>alpha</span> <span id='w2'>beta</span> <span id='w3'>gamma</span> <span id='w4'>delta</span></p>" +
        "<input id='a' style='position:absolute;left:10px;top:100px;width:200px;height:24px;font-size:14px'>" +
        "</body></html>";

    [Fact]
    public Task ShiftClickMovesTheEndOfThePageSelection() => HeadlessSession.Run(async () =>
    {
        var (window, view) = await HeadlessSession.ShowAsync(Page);
        var from = HtmlViewInput.CenterOf(view, "w1");
        var to = HtmlViewInput.CenterOf(view, "w2");
        // A drag over the first two words selects them.
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to);
        window.MouseUp(to, MouseButton.Left);
        await HtmlViewInput.SettleAsync(view);
        var before = view.SelectedText;
        Assert.DoesNotContain("delta", before);

        var far = HtmlViewInput.CenterOf(view, "w4");
        window.MouseDown(far, MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(far, MouseButton.Left, RawInputModifiers.Shift);
        await HtmlViewInput.SettleAsync(view);

        // The anchor stayed where the drag began and the other end moved to the shift-click; the click did not clear it.
        Assert.True(view.Document!.Selection.HasSelection);
        Assert.Contains("beta gamma", view.SelectedText);
        Assert.Contains("gamma", view.SelectedText);
        Assert.True(view.SelectedText.Length > before.Length);
        window.Close();
    });

    [Fact]
    public Task AnUnshiftedClickStillClearsTheSelection() => HeadlessSession.Run(async () =>
    {
        var (window, view) = await HeadlessSession.ShowAsync(Page);
        var from = HtmlViewInput.CenterOf(view, "w1");
        var to = HtmlViewInput.CenterOf(view, "w2");
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to);
        window.MouseUp(to, MouseButton.Left);
        Assert.NotEqual(string.Empty, view.SelectedText);

        var far = HtmlViewInput.CenterOf(view, "w4");
        window.MouseDown(far, MouseButton.Left);
        window.MouseUp(far, MouseButton.Left);
        await HtmlViewInput.SettleAsync(view);
        Assert.Equal(string.Empty, view.SelectedText);
        window.Close();
    });

    [Fact]
    public Task ShiftClickInATextFieldExtendsTheFieldSelection() => HeadlessSession.Run(async () =>
    {
        var (window, view) = await HeadlessSession.ShowAsync(Page);
        var field = new Point(20, 112);
        window.MouseDown(field, MouseButton.Left);
        window.MouseUp(field, MouseButton.Left);
        await view.PlacementTask;
        window.KeyTextInput("hello world");
        window.MouseDown(new Point(11, 112), MouseButton.Left);
        window.MouseUp(new Point(11, 112), MouseButton.Left);
        await view.PlacementTask;
        var state = view.Document!.GetEditState()!.Value;
        Assert.Equal((0, 0), (state.SelectionStart, state.SelectionEnd));

        window.MouseDown(new Point(205, 112), MouseButton.Left, RawInputModifiers.Shift);
        await view.PlacementTask;
        window.MouseUp(new Point(205, 112), MouseButton.Left, RawInputModifiers.Shift);
        state = view.Document.GetEditState()!.Value;
        Assert.Equal((0, 11), (state.SelectionStart, state.SelectionEnd));
        window.Close();
    });
}
