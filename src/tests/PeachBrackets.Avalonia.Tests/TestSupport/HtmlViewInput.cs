using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using PeachBrackets.Updates;

namespace PeachBrackets.Avalonia.Tests.TestSupport;

/// <summary>Drives a view with the mouse and the keyboard the way a person does, then lets the document catch up.</summary>
internal static class HtmlViewInput
{
    public const double PixelsPerPoint = 96.0 / 72.0;

    /// <summary>The middle of an element, in the view's own coordinates.</summary>
    public static Point CenterOf(HtmlView view, string id)
    {
        var bounds = view.Document!.GetElementBounds(id) ?? throw new InvalidOperationException("No element " + id);
        return new Point((bounds.X + (bounds.Width / 2)) * PixelsPerPoint, (bounds.Y + (bounds.Height / 2)) * PixelsPerPoint);
    }

    public static async Task ClickAsync(Window window, HtmlView view, string id) => await ClickAtAsync(window, view, CenterOf(view, id));

    public static async Task ClickAtAsync(Window window, HtmlView view, Point point)
    {
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        await SettleAsync(view);
    }

    public static async Task KeyAsync(Window window, HtmlView view, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, PhysicalKey.None, null);
        await SettleAsync(view);
    }

    /// <summary>
    /// Waits until the view is done with what the input set going: the focus and restyle work it queued, the pointer state being applied, a content-visibility
    /// update, the images chosen again after a zoom, a reload. It has to be seen idle on two looks in a row, since one piece of work can queue the next.
    /// </summary>
    public static async Task SettleAsync(HtmlView view)
    {
        var deadline = Environment.TickCount64 + 30000;
        var idleLooks = 0;
        while (idleLooks < 2)
        {
            Dispatcher.UIThread.RunJobs();
            await view.FocusIdle.WaitAsync(TimeSpan.FromSeconds(30));
            idleLooks = view.IsIdle ? idleLooks + 1 : 0;
            if (Environment.TickCount64 > deadline)
                throw new TimeoutException("The view did not go idle.");

            await Task.Delay(5);
        }

        Dispatcher.UIThread.RunJobs();
    }

    public static FormControlState State(HtmlView view, string id) =>
        view.Document!.GetFormState(view.Document.GetElement(id)!) ?? throw new InvalidOperationException("No form control " + id);
}
