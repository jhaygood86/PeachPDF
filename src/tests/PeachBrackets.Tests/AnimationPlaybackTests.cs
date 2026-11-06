using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

/// <summary>
/// The live animation clock (<c>HtmlDocumentOptions.AnimationsEnabled</c> and <c>HtmlDocument.AdvanceAnimations</c>): animations and transitions play over
/// time, change what is painted without a layout pass, and say what to repaint.
/// </summary>
public class AnimationPlaybackTests
{
    private const string Fade = "@keyframes fade { from { opacity: 1 } to { opacity: 0 } }";
    private const string Slide = "@keyframes slide { from { transform: translateX(0pt) } to { transform: translateX(100pt) } }";

    private static HtmlDocumentOptions Options() => new() { MediaType = MediaType.Screen, PageWidthPoints = 400, AnimationsEnabled = true };

    private static async Task<(RasterRenderContext Context, HtmlDocument Document)> LoadAsync(string css, string body) =>
        await TestDocument.LoadAsync(TestDocument.Wrap(body, $"<style>{css}</style>"), Options());

    private static async Task<(byte R, byte G, byte B, byte A)> PixelAtAsync(RasterRenderContext context, HtmlDocument document, int x, int y)
    {
        using var canvas = await TestDocument.PaintAsync(context, document);
        return TestDocument.Pixel(canvas, x, y);
    }

    [Fact]
    public async Task AnOpacityAnimation_FadesTheBoxOverTime_AndStopsAtItsEnd()
    {
        var (context, document) = await LoadAsync(Fade + " #a { width: 100pt; height: 100pt; background: rgb(255, 0, 0); animation: fade 1s linear forwards }", "<div id=a></div>");

        Assert.True(document.HasRunningAnimations);
        var frame = document.AdvanceAnimations(TimeSpan.Zero);
        Assert.True(frame.IsRunning);
        var start = await PixelAtAsync(context, document, 50, 50);
        Assert.Equal(0, start.G);

        frame = document.AdvanceAnimations(TimeSpan.FromSeconds(0.5));
        Assert.True(frame.IsRunning);

        // The faded box is drawn apart from the page (DocumentPaintLayer.Animated), so the surface under it has nothing to repaint.
        Assert.True(frame.OverlayChanged);
        var half = await PixelAtAsync(context, document, 50, 50);
        Assert.InRange(half.G, 100, 160);

        frame = document.AdvanceAnimations(TimeSpan.FromSeconds(1.5));
        Assert.False(frame.IsRunning);
        var end = await PixelAtAsync(context, document, 50, 50);
        Assert.Equal(255, end.G);
        Assert.False(document.HasRunningAnimations);
    }

    [Fact]
    public async Task ATransformAnimation_MovesTheBoxWithoutMovingItsLayout()
    {
        var (context, document) = await LoadAsync(Slide + " #a { width: 50pt; height: 50pt; background: rgb(0, 0, 255); animation: slide 1s linear forwards }", "<div id=a></div>");
        var before = document.GetElementBounds("a")!.Value;

        document.AdvanceAnimations(TimeSpan.Zero);
        document.AdvanceAnimations(TimeSpan.FromSeconds(0.5));

        // The box's place in the layout is the same; what is painted has moved.
        Assert.Equal(before, document.GetElementBounds("a")!.Value);
        var moved = await PixelAtAsync(context, document, 75, 25);
        Assert.Equal(255, moved.B);
        Assert.Equal(0, moved.R);
        var vacated = await PixelAtAsync(context, document, 10, 25);
        Assert.Equal(255, vacated.R);
    }

    [Fact]
    public async Task APausedAnimation_DoesNotAdvance_AndADelayedOneWaits()
    {
        var (context, document) = await LoadAsync(Fade + " #p { width: 50pt; height: 50pt; background: rgb(255, 0, 0); animation: fade 1s linear forwards; animation-play-state: paused }"
            + " #d { width: 50pt; height: 50pt; background: rgb(255, 0, 0); animation: fade 1s linear 2s forwards }", "<div id=p></div><div id=d></div>");

        document.AdvanceAnimations(TimeSpan.Zero);
        var frame = document.AdvanceAnimations(TimeSpan.FromSeconds(1));

        var paused = await PixelAtAsync(context, document, 25, 25);
        var delayed = await PixelAtAsync(context, document, 25, 75);
        Assert.True(paused.G == 0, $"paused {paused}");
        Assert.True(delayed.G == 0, $"delayed {delayed}");
        // The delayed one is still to start, so the clock keeps running.
        Assert.True(frame.IsRunning);
    }

    [Fact]
    public async Task ADocumentThatIsNotPlayed_NeverRuns()
    {
        var options = Options();
        options.AnimationsEnabled = false;
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<div id=a></div>", $"<style>{Fade} #a {{ animation: fade 1s }}</style>"), options);

        var frame = document.AdvanceAnimations(TimeSpan.FromSeconds(1));

        Assert.False(frame.IsRunning);
        Assert.Empty(frame.DirtyRegions);
        Assert.False(document.HasRunningAnimations);
    }

    [Fact]
    public async Task AHoverTransition_RunsFromTheOldValueToTheNew()
    {
        var (context, document) = await LoadAsync("#a { width: 100pt; height: 100pt; background: rgb(255, 0, 0); opacity: 1; transition: opacity 1s linear } #a:hover { opacity: 0 }", "<div id=a></div>");
        using var measuring = context.CreateCanvas(1, 1);
        document.AdvanceAnimations(TimeSpan.Zero);

        var result = await document.SetPointerStateAsync(0, 50, 50, false, measuring);
        Assert.True(result.Applied);
        Assert.True(document.HasRunningAnimations);

        // Right after the change it is still the old look, not the new one.
        document.AdvanceAnimations(TimeSpan.FromSeconds(0.1));
        var early = await PixelAtAsync(context, document, 50, 50);
        Assert.InRange(early.G, 0, 60);

        document.AdvanceAnimations(TimeSpan.FromSeconds(0.6));
        var middle = await PixelAtAsync(context, document, 50, 50);
        Assert.InRange(middle.G, 90, 190);

        var end = document.AdvanceAnimations(TimeSpan.FromSeconds(2));
        Assert.False(end.IsRunning);
        Assert.Equal(255, (await PixelAtAsync(context, document, 50, 50)).G);
    }
}

public class TransitionInterruptionTests
{
    [Fact]
    public async Task HoveringOutMidTransition_SettlesOnTheBaseValue_AndTheClockStops()
    {
        var (context, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap("<div id=a></div>", "<style>#a { width: 100pt; height: 100pt; background: rgb(255, 0, 0); transition: opacity 1s linear } #a:hover { opacity: 0 }</style>"),
            new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 400, AnimationsEnabled = true });
        using var measuring = context.CreateCanvas(1, 1);
        document.AdvanceAnimations(TimeSpan.Zero);

        await document.SetPointerStateAsync(0, 50, 50, false, measuring);
        document.AdvanceAnimations(TimeSpan.FromSeconds(0.1));
        await document.SetPointerStateAsync(0, 300, 300, false, measuring);
        document.AdvanceAnimations(TimeSpan.FromSeconds(0.2));
        var frame = document.AdvanceAnimations(TimeSpan.FromSeconds(3));

        Assert.False(frame.IsRunning);
        using var canvas = await TestDocument.PaintAsync(context, document);
        Assert.Equal(0, TestDocument.Pixel(canvas, 50, 50).G);
    }
}

/// <summary>Animated images (GIF, WebP) play through the same clock: the frame an image shows follows its own frame durations and loop count.</summary>
[Collection("HostResourceLoader state")]
public class AnimatedImagePlaybackTests
{
    private static Stream Solid(int r, int g, int b)
    {
        var raster = new RasterRenderContext();
        using var canvas = raster.CreateCanvas(8, 8);
        canvas.DrawRectangle(raster.GetSolidBrush(PaintColor.FromArgb(255, r, g, b)), 0, 0, 8, 8);
        var stream = new MemoryStream();
        canvas.Save(stream, "png", new PeachImage.Formats.Png.PngEncoderOptions());
        stream.Position = 0;
        return stream;
    }

    // Red for 100 ms, then blue for 300 ms; plays `loops` times (0 = forever).
    private static string Gif(int loops)
    {
        var frames = new[]
        {
            new PeachImage.AnimatedImageFrame(PeachImage.Image.Load(Solid(255, 0, 0)), TimeSpan.FromMilliseconds(100), default),
            new PeachImage.AnimatedImageFrame(PeachImage.Image.Load(Solid(0, 0, 255)), TimeSpan.FromMilliseconds(300), default),
        };

        using var output = new MemoryStream();
        new PeachImage.AnimatedImage(frames, 8, 8, loops).Save(output, "gif", new PeachImage.Formats.Gif.GifEncoderOptions());
        return "data:image/gif;base64," + Convert.ToBase64String(output.ToArray());
    }

    private static async Task<(RasterRenderContext Context, HtmlDocument Document)> LoadAsync(int loops)
    {
        var context = await TestDocument.NewContextAsync();
        context.ResourceLoader = uri => HostResourceLoader.LoadAsync(uri);
        var document = await HtmlDocument.LoadAsync(TestDocument.Wrap($"<img id=i width=40pt height=40pt src=\"{Gif(loops)}\">"), context,
            new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 200, AnimationsEnabled = true });
        using var measuring = context.CreateCanvas(1, 1);
        await document.LayoutAsync(measuring);
        return (context, document);
    }

    private static async Task<(byte R, byte G, byte B, byte A)> PixelAsync(RasterRenderContext context, HtmlDocument document)
    {
        using var canvas = await TestDocument.PaintAsync(context, document);
        return TestDocument.Pixel(canvas, 20, 20);
    }

    [Fact]
    public async Task AnAnimatedGif_ShowsEachFrameForItsDuration_AndLoops()
    {
        var (context, document) = await LoadAsync(0);
        Assert.True(document.HasRunningAnimations);

        var frame = document.AdvanceAnimations(TimeSpan.Zero);
        Assert.True(frame.IsRunning);
        var first = await PixelAsync(context, document);
        Assert.Equal(255, first.R);
        Assert.Equal(0, first.B);

        frame = document.AdvanceAnimations(TimeSpan.FromMilliseconds(150));
        Assert.NotEmpty(frame.DirtyRegions);
        var second = await PixelAsync(context, document);
        Assert.Equal(255, second.B);
        Assert.Equal(0, second.R);

        // 400 ms is one whole pass, so it is back on the first frame.
        document.AdvanceAnimations(TimeSpan.FromMilliseconds(450));
        Assert.Equal(255, (await PixelAsync(context, document)).R);
    }

    [Fact]
    public async Task AGifThatPlaysOnce_StopsOnItsLastFrame_AndTheClockIdles()
    {
        var (context, document) = await LoadAsync(1);

        document.AdvanceAnimations(TimeSpan.Zero);
        var frame = document.AdvanceAnimations(TimeSpan.FromSeconds(2));

        Assert.False(frame.IsRunning);
        var last = await PixelAsync(context, document);
        Assert.Equal(255, last.B);
        Assert.Equal(0, last.R);
    }
}
