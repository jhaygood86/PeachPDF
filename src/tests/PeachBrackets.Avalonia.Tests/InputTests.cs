using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using PeachBrackets.Avalonia.Tests.TestSupport;

namespace PeachBrackets.Avalonia.Tests;

public class LinkAndClickTests
{
    private const string Page =
        "<html><body style='margin:0'>" +
        "<p><a id='web' href='https://example.test/page'>web</a> <a id='jump' href='#far'>jump</a> <a id='mail' href='mailto:someone@example.test'>mail</a></p>" +
        "<div id='plain' style='height:80px;background:#eee'>plain</div>" +
        "<div style='height:1800px'></div><h2 id='far'>far</h2><div style='height:1800px'></div></body></html>";

    [Fact]
    public async Task AClickOnALinkRaisesLinkActivatedWithItsAddress()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page, 500, 400);
            // The host would take the link; cancelling keeps the test from launching anything.
            var seen = new List<(string? Uri, string? Destination)>();
            view.LinkActivated += (_, e) =>
            {
                seen.Add((e.Uri, e.Destination));
                e.Cancel = true;
            };

            await HtmlViewInput.ClickAsync(window, view, "web");
            await HtmlViewInput.ClickAsync(window, view, "mail");
            Assert.Equal([("https://example.test/page", null), ("mailto:someone@example.test", null)], seen);
            Assert.Equal(0, view.ScrollPixels.Y);
            window.Close();
        });
    }

    [Fact]
    public async Task AnUncancelledLinkIsFollowedThroughTheLauncher()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page, 500, 400);
            var raised = 0;
            view.LinkActivated += (_, _) => raised++;

            // The headless platform's launcher does nothing, so following the link starts nothing; the event still tells the host first.
            Assert.NotNull(TopLevel.GetTopLevel(view)!.Launcher);
            await HtmlViewInput.ClickAsync(window, view, "web");
            Assert.Equal(1, raised);
            window.Close();
        });
    }

    [Fact]
    public async Task ALinkToAPlaceInTheDocumentScrollsThere()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page, 500, 400);
            string? destination = "unset";
            view.LinkActivated += (_, e) => destination = e.Destination;

            await HtmlViewInput.ClickAsync(window, view, "jump");
            Assert.NotNull(destination);
            Assert.True(view.ScrollPixels.Y > 1500, $"the page should have scrolled to the heading, at {view.ScrollPixels.Y}");
            window.Close();
        });
    }

    [Fact]
    public async Task AClickOnThePageThatIsNotALinkRaisesPageClickedAndClearsTheSelection()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page, 500, 400);
            HtmlPageClickedEventArgs? clicked = null;
            view.PageClicked += (_, e) => clicked = e;

            view.SelectAll();
            Assert.NotEqual(string.Empty, view.SelectedText);
            await HtmlViewInput.ClickAsync(window, view, "plain");
            Assert.NotNull(clicked);
            Assert.Equal(0, clicked!.PageIndex);
            Assert.True(clicked.Y > 0 && clicked.X > 0);
            Assert.Equal(string.Empty, view.SelectedText);
            window.Close();
        });
    }

    [Fact]
    public async Task APressThatIsDraggedFarIsNotAClick()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page, 500, 400);
            var clicks = 0;
            view.PageClicked += (_, _) => clicks++;
            var from = HtmlViewInput.CenterOf(view, "plain");
            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(from + new Point(30, 0));
            window.MouseUp(from + new Point(30, 0), MouseButton.Left);
            await HtmlViewInput.SettleAsync(view);
            Assert.Equal(0, clicks);
            window.Close();
        });
    }

    [Fact]
    public async Task ASecondaryButtonDoesNotClick()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page, 500, 400);
            var clicks = 0;
            view.PageClicked += (_, _) => clicks++;
            var at = HtmlViewInput.CenterOf(view, "plain");
            window.MouseDown(at, MouseButton.Right);
            window.MouseUp(at, MouseButton.Right);
            await HtmlViewInput.SettleAsync(view);
            Assert.Equal(0, clicks);
            window.Close();
        });
    }
}

public class WheelAndKeyboardTests
{
    private const string Tall = "<html><body style='margin:0'><div style='height:4000px;width:1200px;background:#ddd'>tall</div></body></html>";

    [Fact]
    public async Task TheWheelWithControlZoomsAroundThePointer()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Tall);
            var zooms = 0;
            view.ZoomChanged += (_, _) => zooms++;
            window.MouseWheel(new Point(100, 100), new Vector(0, 3), RawInputModifiers.Control);
            Assert.True(view.Zoom > 1.2, $"zoom was {view.Zoom}");
            Assert.True(zooms > 0);

            view.IsZoomEnabled = false;
            var held = view.Zoom;
            window.MouseWheel(new Point(100, 100), new Vector(0, 3), RawInputModifiers.Control);
            Assert.Equal(held, view.Zoom);
            window.Close();
        });
    }

    [Fact]
    public async Task ThePlainWheelScrollsByNotchesInBothDirections()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Tall);
            window.MouseWheel(new Point(100, 100), new Vector(0, -1));
            Assert.Equal(48, view.ScrollPixels.Y, 0.5);
            window.MouseWheel(new Point(100, 100), new Vector(0, 1));
            Assert.Equal(0, view.ScrollPixels.Y, 0.5);

            // A zoomed page is wider than the view, and the sideways wheel moves along it.
            view.SetZoom(2, new Point(0, 0));
            window.MouseWheel(new Point(100, 100), new Vector(-1, 0));
            Assert.Equal(48, view.ScrollPixels.X, 0.5);
            window.Close();
        });
    }

    [Fact]
    public async Task TheArrowKeysThePageKeysAndHomeAndEndScrollThePage()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Tall);
            view.Focus();
            await HtmlViewInput.KeyAsync(window, view, Key.Down);
            Assert.Equal(40, view.ScrollPixels.Y, 0.5);
            await HtmlViewInput.KeyAsync(window, view, Key.Up);
            Assert.Equal(0, view.ScrollPixels.Y, 0.5);
            view.SetZoom(2, new Point(0, 0));
            await HtmlViewInput.KeyAsync(window, view, Key.Right);
            Assert.Equal(40, view.ScrollPixels.X, 0.5);
            await HtmlViewInput.KeyAsync(window, view, Key.Left);
            Assert.Equal(0, view.ScrollPixels.X, 0.5);
            view.SetZoom(1);

            await HtmlViewInput.KeyAsync(window, view, Key.PageDown);
            var page = view.ScrollPixels.Y;
            Assert.True(page > 200 && page < 300, $"a page is most of the view, got {page}");
            await HtmlViewInput.KeyAsync(window, view, Key.Space);
            Assert.Equal(page * 2, view.ScrollPixels.Y, 1);
            await HtmlViewInput.KeyAsync(window, view, Key.PageUp);
            Assert.Equal(page, view.ScrollPixels.Y, 1);

            await HtmlViewInput.KeyAsync(window, view, Key.End, HeadlessSession.Command);
            Assert.True(view.ScrollPixels.Y > 3500);
            await HtmlViewInput.KeyAsync(window, view, Key.Home, HeadlessSession.Command);
            Assert.Equal(0, view.ScrollPixels.Y);
            window.Close();
        });
    }

    [Fact]
    public async Task ControlWithPlusMinusAndZeroZoomsAndResets()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Tall);
            view.Focus();
            await HtmlViewInput.KeyAsync(window, view, Key.OemPlus, HeadlessSession.Command);
            Assert.Equal(1.1, view.Zoom, 3);
            await HtmlViewInput.KeyAsync(window, view, Key.Add, HeadlessSession.Command);
            Assert.Equal(1.21, view.Zoom, 3);
            await HtmlViewInput.KeyAsync(window, view, Key.OemMinus, HeadlessSession.Command);
            await HtmlViewInput.KeyAsync(window, view, Key.Subtract, HeadlessSession.Command);
            Assert.Equal(1, view.Zoom, 3);
            await HtmlViewInput.KeyAsync(window, view, Key.OemPlus, HeadlessSession.Command);
            await HtmlViewInput.KeyAsync(window, view, Key.D0, HeadlessSession.Command);
            Assert.Equal(1, view.Zoom);
            await HtmlViewInput.KeyAsync(window, view, Key.OemPlus, HeadlessSession.Command);
            await HtmlViewInput.KeyAsync(window, view, Key.NumPad0, HeadlessSession.Command);
            Assert.Equal(1, view.Zoom);
            window.Close();
        });
    }

    [Fact]
    public async Task ControlAAndControlCSelectAndCopy()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync("<html><body><p>alpha beta</p></body></html>");
            view.Focus();
            await HtmlViewInput.KeyAsync(window, view, Key.A, HeadlessSession.Command);
            Assert.Contains("alpha beta", view.SelectedText);
            await HtmlViewInput.KeyAsync(window, view, Key.C, HeadlessSession.Command);
            await HtmlViewInput.KeyAsync(window, view, Key.F5);
            window.Close();
        });
    }

    [Fact]
    public async Task AnAutoHeightViewLeavesTheWheelAndTheKeysToItsParent()
    {
        await HeadlessSession.Run(async () =>
        {
            var view = new HtmlView { Html = Tall, ScrollMode = HtmlViewScrollMode.Disabled };
            var (window, shown, _) = await HeadlessSession.ShowViewAsync(view);
            shown.Focus();
            window.MouseWheel(new Point(100, 100), new Vector(0, -1));
            window.MouseWheel(new Point(100, 100), new Vector(0, 1), RawInputModifiers.Control);
            await HtmlViewInput.KeyAsync(window, shown, Key.Down);
            Assert.Equal(0, shown.ScrollPixels.Y);
            Assert.Equal(1, shown.Zoom);
            window.Close();
        });
    }
}

public class HoverTests
{
    private const string Page =
        "<html><head><style>#hot{width:200px;height:100px;background:#0000ff}#hot:hover{background:#00ff00}#hot:active{background:#ff0000}</style></head>" +
        "<body style='margin:0'><div id='hot'></div></body></html>";

    // The view sits to the right of a strip the pointer can leave it through.
    private static async Task<(Window Window, HtmlView View)> ShowBesideAStripAsync()
    {
        var view = new HtmlView { Html = Page };
        var strip = new Border { Width = 100, Background = global::Avalonia.Media.Brushes.Gray };
        DockPanel.SetDock(strip, Dock.Left);
        var panel = new DockPanel();
        panel.Children.Add(strip);
        panel.Children.Add(view);
        var window = new Window { Width = 500, Height = 300, Content = panel };
        var loaded = new TaskCompletionSource();
        view.DocumentLoaded += (_, _) => loaded.TrySetResult();
        window.Show();
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(60));
        await HtmlViewInput.SettleAsync(view);
        return (window, view);
    }

    private static (byte R, byte G, byte B) Pixel(Window window, int x, int y) => HeadlessSession.PixelAt(HeadlessSession.Capture(window), x, y);

    [Fact]
    public async Task HoverAndActiveStylesFollowThePointerAndClearWhenItLeaves()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await ShowBesideAStripAsync();
            var inside = new Point(150, 50);
            Assert.True(Pixel(window, 150, 50).B > 200, "blue at rest");

            window.MouseMove(inside);
            await HtmlViewInput.SettleAsync(view);
            Assert.True(Pixel(window, 150, 50).G > 200, "green under the pointer");

            window.MouseDown(inside, MouseButton.Left);
            await HtmlViewInput.SettleAsync(view);
            Assert.True(Pixel(window, 150, 50).R > 200, "red while pressed");

            // Moving while pressed keeps it pressed; releasing lets go of :active.
            window.MouseMove(inside + new Point(1, 1));
            await HtmlViewInput.SettleAsync(view);
            window.MouseUp(inside + new Point(1, 1), MouseButton.Left);
            await HtmlViewInput.SettleAsync(view);
            Assert.True(Pixel(window, 150, 50).G > 200, "green again once released");

            // Out of the view, onto the strip: nothing is hovered any more.
            window.MouseMove(new Point(50, 50));
            await HtmlViewInput.SettleAsync(view);
            Assert.True(Pixel(window, 150, 50).B > 200, "blue after the pointer left");
            Assert.Equal(StandardCursorType.Arrow, view.ShownKeywordCursor);
            window.Close();
        });
    }

    [Fact]
    public async Task LosingThePointerCaptureEndsThePress()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await ShowBesideAStripAsync();
            var inside = new Point(150, 50);
            window.MouseMove(inside);
            window.MouseDown(inside, MouseButton.Left);
            await HtmlViewInput.SettleAsync(view);
            Assert.True(Pixel(window, 150, 50).R > 200, "red while pressed");

            // Taking the view out of the tree releases the capture it holds, the press is over and nothing stays active.
            var pointer = view.GetType().GetMethod("OnPointerCaptureLost", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            pointer.Invoke(view, [new PointerCaptureLostEventArgs(view, new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true))]);
            await HtmlViewInput.SettleAsync(view);
            Assert.False(Pixel(window, 150, 50).R > 200 && Pixel(window, 150, 50).G < 60, "no longer pressed");
            window.Close();
        });
    }
}
