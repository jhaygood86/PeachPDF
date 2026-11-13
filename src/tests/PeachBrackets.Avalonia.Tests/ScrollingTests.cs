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
    private static Point? FindBarPart(HtmlView view, ScrollbarPart part)
    {
        var document = view.Document!;
        for (double x = 80; x < 160; x += 1)
        {
            for (double y = 0; y < 76; y += 1)
            {
                if (document.GetScrollbarAt(0, x, y) is { } hit && hit.Part == part && hit.Axis == ScrollbarAxis.Vertical)
                    return new Point(x * DipsPerPoint, y * DipsPerPoint);
            }
        }

        return null;
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
            var (window, view) = await HeadlessSession.ShowAsync(Page);
            Assert.True(view.SetContainerScroll("box", 0, 100000));
            var end = BoxY(view);
            Assert.True(end > 0);
            window.MouseWheel(new Point(50, 40), new Vector(0, -1));
            Assert.Equal(end, BoxY(view));
            Assert.Equal(0, view.ScrollPixels.Y);
            window.Close();
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
}
