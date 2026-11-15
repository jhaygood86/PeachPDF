using Avalonia;
using Avalonia.Headless;
using PeachBrackets.Avalonia.Tests.TestSupport;
using PeachBrackets.HtmlViewCore;
using SkiaSharp;
using Amount = global::Avalonia.Automation.Provider.ScrollAmount;

namespace PeachBrackets.Avalonia.Tests;

public class PaintingTests
{
    private static (byte R, byte G, byte B) Pixel(global::Avalonia.Controls.Window window, int x, int y) => HeadlessSession.PixelAt(HeadlessSession.Capture(window), x, y);

    private static bool Is(global::Avalonia.Controls.Window window, int x, int y, byte r, byte g, byte b)
    {
        var p = Pixel(window, x, y);
        return Math.Abs(p.R - r) < 12 && Math.Abs(p.G - g) < 12 && Math.Abs(p.B - b) < 12;
    }

    // Three bands, each 400px tall.
    private const string Bands =
        "<html><body style='margin:0'><div style='height:400px;background:#ff0000'></div><div style='height:400px;background:#00ff00'></div><div style='height:400px;background:#0000ff'></div></body></html>";

    [Fact]
    public async Task ScrollingPaintsTheBandThatComesIntoView()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Bands, 300, 300);
            Assert.True(Is(window, 100, 100, 255, 0, 0), "red at the top");

            view.ScrollToElement("none");
            window.MouseWheel(new Point(100, 100), new Vector(0, -10));
            Assert.True(view.ScrollPixels.Y >= 400, $"scrolled to {view.ScrollPixels.Y}");
            Assert.True(Is(window, 100, 100, 0, 255, 0) || Is(window, 100, 100, 0, 0, 255), "a lower band is painted after the scroll");

            window.MouseWheel(new Point(100, 100), new Vector(0, 10));
            Assert.True(Is(window, 100, 100, 255, 0, 0), "red again at the top");
            window.Close();
        });
    }

    [Fact]
    public async Task ZoomingScalesWhatIsPainted()
    {
        await HeadlessSession.Run(async () =>
        {
            var html = "<html><body style='margin:0'><div style='width:100px;height:100px;background:#ff0000'></div></body></html>";
            var (window, view) = await HeadlessSession.ShowAsync(html, 300, 300);
            Assert.True(Is(window, 50, 50, 255, 0, 0));
            Assert.False(Is(window, 150, 50, 255, 0, 0), "the box ends at 100");

            view.SetZoom(2, new Point(0, 0));
            Assert.True(Is(window, 150, 50, 255, 0, 0), "twice as wide when zoomed");
            Assert.False(Is(window, 250, 50, 255, 0, 0));
            window.Close();
        });
    }

    [Fact]
    public async Task AFixedBoxStaysPutAndAViewportBackgroundIsPaintedAtEveryScroll()
    {
        await HeadlessSession.Run(async () =>
        {
            var html =
                "<html><body style='margin:0;background:linear-gradient(#ffffff,#ffffff) fixed'>" +
                "<div style='position:fixed;left:0;top:0;width:60px;height:60px;background:#ff0000'></div>" +
                "<div style='height:3000px'></div></body></html>";
            var (window, view) = await HeadlessSession.ShowAsync(html, 300, 300);
            Assert.True(view.Document!.HasViewportBackgrounds);
            Assert.True(Is(window, 30, 30, 255, 0, 0));
            window.MouseWheel(new Point(200, 200), new Vector(0, -10));
            Assert.True(view.ScrollPixels.Y > 100);
            Assert.True(Is(window, 30, 30, 255, 0, 0), "the fixed box is still in its corner");
            window.Close();
        });
    }

    [Fact]
    public async Task AStickyHeaderFollowsTheTopOfTheView()
    {
        await HeadlessSession.Run(async () =>
        {
            var html =
                "<html><body style='margin:0'><div style='position:sticky;top:0;height:40px;background:#ff0000'></div>" +
                "<div style='height:3000px;background:#eeeeee'></div></body></html>";
            var (window, view) = await HeadlessSession.ShowAsync(html, 300, 300);
            window.MouseWheel(new Point(200, 200), new Vector(0, -10));
            Assert.True(view.ScrollPixels.Y > 100);
            Assert.True(Is(window, 100, 20, 255, 0, 0), "the sticky bar is at the top of the view after the scroll");
            Assert.True(Is(window, 100, 150, 238, 238, 238));
            window.Close();
        });
    }

    [Fact]
    public async Task ThePageBackgroundShowsWhereThereIsNoPage()
    {
        await HeadlessSession.Run(async () =>
        {
            var html = "<html><body style='margin:0'><div style='height:50px'></div></body></html>";
            var (window, view) = await HeadlessSession.ShowAsync(html, 300, 300, v => v.PageBackground = global::Avalonia.Media.Color.FromRgb(0, 128, 0));
            // The document does not paint its own canvas here, so what lies past it is the host's color.
            Assert.True(Is(window, 150, 280, 255, 255, 255) || Is(window, 150, 280, 0, 128, 0));
            view.PageBackground = global::Avalonia.Media.Color.FromRgb(128, 0, 128);
            Assert.True(Is(window, 150, 280, 128, 0, 128) || Is(window, 150, 280, 255, 255, 255));
            window.Close();
        });
    }

    [Fact]
    public async Task ALongPageShowsAScrollIndicatorThatFollowsTheScroll()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Bands, 300, 300);
            var top = Pixel(window, 297 - 1, 10);
            Assert.True(top.R < 250 || top.G > 0, "the thumb at the top darkens the page's red");
            window.MouseWheel(new Point(100, 100), new Vector(0, -100));
            var bottom = Pixel(window, 296, 290);
            Assert.NotEqual((255, 0, 0), bottom);
            window.Close();
        });
    }

    [Fact]
    public async Task AControlWithNoRoomPaintsNothingAndOneWithoutADocumentOnlyItsBackground()
    {
        await HeadlessSession.Run(async () =>
        {
            var view = new HtmlView { PageBackground = global::Avalonia.Media.Color.FromRgb(0, 0, 255) };
            var window = new global::Avalonia.Controls.Window { Width = 200, Height = 200, Content = view };
            window.Show();
            await HeadlessSession.PumpUntilAsync(() => view.FramesPainted > 0, 2000);
            Assert.True(Is(window, 100, 100, 0, 0, 255), "the host color shows with no document");
            Assert.Null(view.Document);
            window.Close();
        });
    }

    [Fact]
    public async Task TheTilePathPaintsOnARenderThreadWithoutTouchingTheControl()
    {
        await HeadlessSession.Run(async () =>
        {
            var html = "<html><body style='margin:0'><div style='height:2600px;background:#00aa00'></div></body></html>";
            var (window, view) = await HeadlessSession.ShowAsync(html, 300, 300, v => v.PageBackground = global::Avalonia.Media.Color.FromRgb(0, 0, 255));
            view.TilesOnCpuForTests = true;

            // Captured here, on the UI thread, as a render does; painted on a thread that is not allowed to read the control's properties.
            var paint = view.CapturePaintForTests();
            using var bitmap = new SKBitmap(300, 300);
            using var canvas = new SKCanvas(bitmap);
            await Task.Run(() => paint(canvas, 300, 300));

            var pixel = bitmap.GetPixel(100, 100);
            Assert.True(pixel.Green > 120 && pixel.Blue < 60, $"the tile of the page should be drawn, got {pixel}");
            window.Close();
        });
    }

    [Fact]
    public async Task ATileForARegionThatChangedIsDroppedAndTheOthersStay()
    {
        await HeadlessSession.Run(async () =>
        {
            var html = "<html><body style='margin:0'><div style='height:2600px;background:#eeeeee'></div><p id='low' style='margin:0;height:50px;background:#eeeeee'>low</p></body></html>";
            var (window, view) = await HeadlessSession.ShowAsync(html, 300, 300);
            var tiles = view.SeedTilesForTests();
            foreach (var row in new[] { 0, 1, 2 })
                tiles.Add(new TileKey(0, row), new SKBitmap(1, 1) is var bitmap ? SKImage.FromBitmap(bitmap) : null!);

            Assert.True(tiles.TryGet(new TileKey(0, 2), out _));
            var document = view.Document!;
            using var measuring = new HtmlView.MeasuringCanvas(view.RenderContext!);
            var result = await document.SetAttributeAsync(document.GetElement("low")!, "style", "margin:0;height:50px;background:#ff0000", measuring.Canvas);
            Assert.True(result.Applied);
            view.ApplyUpdate(result);

            Assert.False(tiles.TryGet(new TileKey(0, 2), out _), "the tile that holds the changed box is painted again");
            Assert.True(tiles.TryGet(new TileKey(0, 0), out _), "a tile the change did not touch is kept");
            window.Close();
        });
    }

    private static async Task PaintOnceAsync(HtmlView view)
    {
        var paint = view.CapturePaintForTests();
        using var bitmap = new SKBitmap(300, 300);
        using var canvas = new SKCanvas(bitmap);
        await Task.Run(() => paint(canvas, 300, 300));
    }

    [Fact]
    public async Task AFrameThatFoundEveryTileItNeededPaintsOneMoreInTheDirectionOfTheScroll()
    {
        await HeadlessSession.Run(async () =>
        {
            var html = "<html><body style='margin:0'><div style='height:4000px;background:#00aa00'></div></body></html>";
            var (window, view) = await HeadlessSession.ShowAsync(html, 300, 300);
            view.TilesOnCpuForTests = true;

            await PaintOnceAsync(view);
            Assert.Equal(1, view.TileCountForTests);

            // Not yet scrolling anywhere: nothing is painted ahead.
            await PaintOnceAsync(view);
            Assert.Equal(1, view.TileCountForTests);

            // Scrolled down a little, the visible tile is held, so the frame has time to paint the one below it; once that is held there is nothing more to do.
            view.ScrollDocumentFromAutomation(Amount.NoAmount, Amount.SmallIncrement);
            await PaintOnceAsync(view);
            Assert.Equal(2, view.TileCountForTests);
            await PaintOnceAsync(view);
            Assert.Equal(2, view.TileCountForTests);

            // At the end the frame has to paint its own tile; the next one finds nothing beyond the last row to paint ahead.
            view.ScrollDocumentToPercentFromAutomation(-1, 100);
            await PaintOnceAsync(view);
            Assert.Equal(3, view.TileCountForTests);
            await PaintOnceAsync(view);
            Assert.Equal(3, view.TileCountForTests);

            // Scrolling back up paints ahead on the other side.
            view.ScrollDocumentFromAutomation(Amount.NoAmount, Amount.SmallDecrement);
            await PaintOnceAsync(view);
            Assert.Equal(4, view.TileCountForTests);
            window.Close();
        });
    }

    [Fact]
    public async Task APaintThatFailsIsReportedOnceAsAFailureOfTheDocument()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync("<html><body><p>text</p></body></html>", 300, 300);
            var failures = new List<Exception>();
            view.DocumentFailed += (_, e) => failures.Add(e.Exception);

            // Every frame fails, and the application is told once, not once a frame.
            view.PaintHookForTests = () => throw new InvalidOperationException("the paint failed");
            await PaintOnceAsync(view);
            await PaintOnceAsync(view);
            Assert.True(await HeadlessSession.PumpUntilAsync(() => failures.Count > 0));
            await HeadlessSession.PumpUntilAsync(() => false, 100);
            Assert.Single(failures);
            Assert.Equal("the paint failed", failures[0].Message);
            view.PaintHookForTests = null;
            window.Close();
        });
    }
}
