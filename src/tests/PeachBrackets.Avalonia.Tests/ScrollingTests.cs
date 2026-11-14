using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using PeachBrackets.Avalonia.Tests.TestSupport;

namespace PeachBrackets.Avalonia.Tests;

public class ScrollingTests
{
    private const double DipsPerPoint = 96.0 / 72;

    private const string Page =
        "<html><body style='margin:0'>" +
        "<div id='box' style='width:200px;height:100px;overflow:auto'><div style='width:150px;height:1000px;background:#9cf'>inner</div></div>" +
        "<div style='height:2000px'>tall</div></body></html>";

    private static double BoxY(HtmlView view) => (view.GetContainerScroll("box") ?? throw new InvalidOperationException("no box")).Y;

    // The first point (in control DIPs) over the box's scroll bar that is the given part.
    private static Point? FindBarPart(HtmlView view, ScrollbarPart part, bool last = false)
    {
        Point? found = null;
        var document = view.Document!;
        for (double x = 80; x < 160; x += 1)
        {
            for (double y = 0; y < 76; y += 1)
            {
                if (document.GetScrollbarAt(0, x, y) is { } hit && hit.Part == part && hit.Axis == ScrollbarAxis.Vertical)
                {
                    found = new Point(x * DipsPerPoint, y * DipsPerPoint);
                    if (!last)
                        return found;
                }
            }
        }

        return found;
    }

    [Fact]
    public async Task TheWheelOverAnOverflowBoxScrollsTheBoxNotThePage()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page);
            window.MouseWheel(new Point(50, 40), new Vector(0, -1));
            Assert.True(BoxY(view) > 0, "the box should have scrolled");
            Assert.Equal(0, view.ScrollPixels.Y);

            window.MouseWheel(new Point(50, 200), new Vector(0, -1));
            Assert.True(view.ScrollPixels.Y > 0, "over the page the page scrolls");
            window.Close();
        });
    }

    [Fact]
    public async Task TheWheelAtTheEndOfABoxStillBelongsToTheBox()
    {
        await HeadlessSession.Run(async () =>
        {
            // Without overscroll-behavior a box at its end chains the turn to the page; with `contain` the box keeps it.
            var (window, view) = await HeadlessSession.ShowAsync(Page.Replace("overflow:auto", "overflow:auto;overscroll-behavior:contain"));
            Assert.True(view.SetContainerScroll("box", 0, 100000));
            var end = BoxY(view);
            Assert.True(end > 0);
            window.MouseWheel(new Point(50, 40), new Vector(0, -1));
            Assert.Equal(end, BoxY(view));
            Assert.Equal(0, view.ScrollPixels.Y);
            window.Close();

            var (chained, chainedView) = await HeadlessSession.ShowAsync(Page);
            Assert.True(chainedView.SetContainerScroll("box", 0, 100000));
            chained.MouseWheel(new Point(50, 40), new Vector(0, -1));
            Assert.True(chainedView.ScrollPixels.Y > 0, "at its end a plain box passes the turn on to the page");
            chained.Close();
        });
    }

    [Fact]
    public async Task APressOnTheThumbDragsTheBox()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page);
            var from = FindBarPart(view, ScrollbarPart.Thumb) ?? throw new InvalidOperationException("no thumb");
            window.MouseDown(from, MouseButton.Left);
            Assert.True(view.IsDraggingScrollbar);
            window.MouseMove(new Point(from.X, from.Y + 20));
            var dragged = BoxY(view);
            Assert.True(dragged > 0, "dragging the thumb scrolls the box");
            window.MouseMove(new Point(from.X, from.Y + 40));
            Assert.True(BoxY(view) > dragged);
            window.MouseUp(new Point(from.X, from.Y + 40), MouseButton.Left);
            Assert.False(view.IsDraggingScrollbar);
            Assert.Equal(0, view.ScrollPixels.Y);
            window.Close();
        });
    }

    [Fact]
    public async Task APressOnTheTrackPagesTheBox()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page);
            var press = FindBarPart(view, ScrollbarPart.TrackAfter) ?? throw new InvalidOperationException("no track");
            window.MouseDown(press, MouseButton.Left);
            Assert.True(BoxY(view) > 0);
            window.MouseUp(press, MouseButton.Left);
            window.Close();
        });
    }

    [Fact]
    public async Task KeysAreThePagesWhenNothingInAContainerHasTheFocus()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page);
            Assert.False(view.ScrollFocusedContainer(0, 40));
            Assert.Equal(0, BoxY(view));
            window.Close();
        });
    }

    [Fact]
    public async Task ScrollingAContainerToAPositionReportsIt()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page);
            Assert.True(view.SetContainerScroll("box", 0, 120));
            Assert.Equal(120, BoxY(view), 0.5);
            Assert.False(view.SetContainerScroll("missing", 0, 1));
            window.Close();
        });
    }

    [Fact]
    public async Task APressHeldOnTheTrackPagesAgainUntilTheThumbArrives()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page);
            var press = FindBarPart(view, ScrollbarPart.TrackAfter, last: true) ?? throw new InvalidOperationException("no track");
            window.MouseDown(press, MouseButton.Left);
            var first = BoxY(view);
            Assert.True(first > 0);

            // Held past the delay it pages again at a steady rate, and moving the pointer on the track keeps the repeat going.
            Assert.True(await HeadlessSession.PumpUntilAsync(() => BoxY(view) > first + 1, 5000), "the held track should page again");
            window.MouseMove(new Point(press.X, press.Y + 2));
            Assert.True(await HeadlessSession.PumpUntilAsync(() => BoxY(view) >= first * 2, 5000));
            window.MouseUp(new Point(press.X, press.Y + 2), MouseButton.Left);
            var stopped = BoxY(view);
            await HeadlessSession.PumpUntilAsync(() => false, 300);
            Assert.Equal(stopped, BoxY(view));
            window.Close();
        });
    }

    [Fact]
    public async Task APressHeldOnTheTrackStopsOnceTheThumbIsUnderThePointer()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page);
            var press = FindBarPart(view, ScrollbarPart.TrackAfter) ?? throw new InvalidOperationException("no track");
            // The track just after the thumb: the first page brings the thumb past the pointer, so the repeat ends by itself.
            window.MouseDown(press, MouseButton.Left);
            await HeadlessSession.PumpUntilAsync(() => false, 900);
            var settled = BoxY(view);
            await HeadlessSession.PumpUntilAsync(() => false, 300);
            Assert.Equal(settled, BoxY(view));
            window.MouseUp(press, MouseButton.Left);
            window.Close();
        });
    }

    [Fact]
    public async Task TheWheelScrollsATextAreaThatHoldsMoreThanItShows()
    {
        await HeadlessSession.Run(async () =>
        {
            var lines = string.Join(" ", Enumerable.Range(1, 60).Select(i => "line " + i));
            var html = $"<html><body style='margin:0'><textarea id='ta' rows='4' cols='30'>{lines}</textarea><div style='height:1500px'></div></body></html>";
            var (window, view) = await HeadlessSession.ShowAsync(html);
            var inside = HtmlViewInput.CenterOf(view, "ta");
            var before = view.GetContainerScroll("ta");
            window.MouseWheel(inside, new Vector(0, -2));
            await HtmlViewInput.SettleAsync(view);

            // The text area took the wheel, so the page did not move.
            Assert.Equal(0, view.ScrollPixels.Y);
            window.MouseWheel(new Point(100, 250), new Vector(0, -2));
            Assert.True(view.ScrollPixels.Y > 0, "over the page the page scrolls");
            _ = before;
            window.Close();
        });
    }

    [Fact]
    public async Task AFocusedScrollContainerTakesTheArrowKeysBeforeThePage()
    {
        await HeadlessSession.Run(async () =>
        {
            var html = "<html><body style='margin:0'><div id='box' style='width:200px;height:100px;overflow:auto'><button id='b'>inside</button><div style='height:1000px'>inner</div></div><div style='height:2000px'></div></body></html>";
            var (window, view) = await HeadlessSession.ShowAsync(html);
            view.Focus();
            Assert.True(view.StepFocus(true));
            await HtmlViewInput.SettleAsync(view);
            Assert.NotNull(view.Document!.FocusedNode);

            await HtmlViewInput.KeyAsync(window, view, Key.Down);
            Assert.True(BoxY(view) > 0, "the arrow key scrolls the box");
            Assert.Equal(0, view.ScrollPixels.Y);

            var arrow = BoxY(view);
            await HtmlViewInput.KeyAsync(window, view, Key.PageDown);
            Assert.True(BoxY(view) > arrow + 20, "a page key scrolls it by most of its own height");
            window.Close();
        });
    }
}
