using Avalonia;
using PeachBrackets.Avalonia.Logic;
using PeachBrackets.Avalonia.Tests.TestSupport;

namespace PeachBrackets.Avalonia.Tests;

public class TouchMathTests
{
    [Fact]
    public void AFlingSlowsToAThirdAfterOneTimeConstant()
    {
        Assert.Equal(Math.Exp(-1), TouchMath.FlingDecay(0.35), 6);
        Assert.Equal(1, TouchMath.FlingDecay(0), 6);
    }

    [Fact]
    public void VelocityBlendsTheNewSampleWithTheOldOne()
    {
        Assert.Equal((0.6 * 100) + (0.4 * 50), TouchMath.SmoothVelocity(50, 10, 0.1), 6);
        Assert.Equal(50, TouchMath.SmoothVelocity(50, 10, 0));
    }

    [Fact]
    public void TwoTapsCloseInTimeAndPlaceAreADoubleTap()
    {
        Assert.True(TouchMath.IsDoubleTap(10, 10, 1000, 20, 15, 1200));
        Assert.False(TouchMath.IsDoubleTap(10, 10, 1000, 20, 15, 1500));
        Assert.False(TouchMath.IsDoubleTap(10, 10, 1000, 200, 15, 1200));
        Assert.False(TouchMath.IsDoubleTap(10, 10, 0, 10, 10, 100));
    }

    [Fact]
    public void ADoubleTapZoomsInAndThenBackOut()
    {
        Assert.Equal(2, TouchMath.DoubleTapZoom(1));
        Assert.Equal(1, TouchMath.DoubleTapZoom(2));
    }

    [Fact]
    public void APinchScalesTheZoomItBeganWith() => Assert.Equal(3, TouchMath.PinchZoom(1.5, 2));

    [Fact]
    public void CursorImagesFitTheirSizeAndHotspot()
    {
        Assert.Equal((64, 32), CursorImages.Fit(64, 32, 128));
        Assert.Equal((128, 64), CursorImages.Fit(256, 128, 128));
        Assert.Equal((64, 32), CursorImages.Hotspot(null, null, 128, 64, 128, 64));
        Assert.Equal((0, 0), CursorImages.Hotspot(0, 0, 128, 64, 64, 32));
        Assert.True(CursorImages.IsFetchable("data:image/png;base64,AAAA"));
        Assert.False(CursorImages.IsFetchable("ms-appx:///x.png"));
        Assert.True(CursorImages.TryDecodeDataUri("data:text/plain;base64,aGk=", out var bytes));
        Assert.Equal("hi"u8.ToArray(), bytes);
    }
}

public class TouchTests
{
    private const string Tall = "<html><body style='margin:0'><div style='height:3000px;background:#eee'>tall</div></body></html>";

    private const string Boxed =
        "<html><body style='margin:0'><div id='box' style='width:200px;height:100px;overflow:auto'><div style='height:1000px'>x</div></div><div style='height:3000px'>t</div></body></html>";

    [Fact]
    public async Task AFingerDraggedOverThePageScrollsIt()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Tall);
            Assert.True(view.TouchDragBy(0, 60));
            Assert.Equal(60, view.ScrollPixels.Y, 0.5);
            window.Close();
        });
    }

    [Fact]
    public async Task TouchActionLimitsThePanTheFingerMayDo()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Tall);
            view.TouchAllowed = PeachBrackets.TouchActions.PanDown;
            Assert.False(view.TouchDragBy(0, 60));
            Assert.Equal(0, view.ScrollPixels.Y);
            window.Close();
        });
    }

    [Fact]
    public async Task AFingerOnAnOverflowBoxDragsTheBoxAndNotThePage()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Boxed);
            view.BeginTouchPress(0, 40, 40);
            Assert.True(view.TouchDragBy(0, 50));
            Assert.True(view.IsTouchScrollingContainer);
            Assert.Equal(0, view.ScrollPixels.Y);
            Assert.True(view.GetContainerScroll("box")!.Value.Y > 0);
            window.Close();
        });
    }

    [Fact]
    public async Task AFlingCarriesThePageOnAndDecaysToAStop()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Tall);
            view.BeginFling(null, 0, 2000);
            Assert.True(view.IsFlinging);
            var last = 0.0;
            var steps = 0;
            while (view.IsFlinging && steps++ < 1000)
            {
                view.StepFling(0.016);
                Assert.True(view.ScrollPixels.Y >= last);
                last = view.ScrollPixels.Y;
            }

            Assert.False(view.IsFlinging);
            // The distance of an exponential decay is speed times its time constant, less what is left under the stop speed.
            Assert.InRange(last, 2000 * 0.35 * 0.8, 2000 * 0.35 * 1.05);
            window.Close();
        });
    }

    [Fact]
    public async Task AFlingStopsAtTheEndOfTheContent()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync("<html><body style='margin:0'><div style='height:100px'>short</div></body></html>");
            view.BeginFling(null, 0, 5000);
            view.StepFling(0.016);
            Assert.False(view.IsFlinging);
            window.Close();
        });
    }

    [Fact]
    public async Task APinchZoomsAboutItsFocusAndEndsOnRelease()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Tall);
            Assert.True(view.TouchDragBy(0, 600));
            var focus = new Point(100, 100);
            var contentBefore = (view.ScrollPixels.Y + focus.Y) / view.Zoom;
            view.ApplyPinch(1.5, focus);
            Assert.Equal(1.5, view.Zoom, 3);
            view.ApplyPinch(2, focus);
            Assert.Equal(2, view.Zoom, 3);
            Assert.Equal(contentBefore, (view.ScrollPixels.Y + focus.Y) / view.Zoom, 1);
            view.EndPinch();
            view.ApplyPinch(0.5, focus);
            Assert.Equal(1, view.Zoom, 3);
            window.Close();
        });
    }

    [Fact]
    public async Task ADoubleTapZoomsInUnlessTheDocumentForbidsIt()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Tall);
            view.HandleTouchTap(new Point(50, 50));
            Assert.Equal(1, view.Zoom);
            view.HandleTouchTap(new Point(52, 51));
            Assert.Equal(2, view.Zoom, 3);

            view.TouchAllowed = PeachBrackets.TouchActions.PanUp;
            view.HandleTouchTap(new Point(50, 50));
            view.HandleTouchTap(new Point(50, 50));
            Assert.Equal(2, view.Zoom, 3);
            window.Close();
        });
    }
}
