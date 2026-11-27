using PeachBrackets.Media.GStreamer;
using PeachDrawing;
using PeachDrawing.Core;
using System.Diagnostics;

namespace PeachBrackets.Media.GStreamer.Tests;

/// <summary>The provider against a real GStreamer: clips made with <c>gst-launch-1.0</c> from <c>videotestsrc</c> and <c>audiotestsrc</c>.</summary>
public class GStreamerProviderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "peachbrackets-gst-tests-" + Guid.NewGuid().ToString("N"));

    public GStreamerProviderTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, true);
        }
        catch (IOException)
        {
        }
    }

    private static void RequireGStreamer()
    {
        Assert.SkipUnless(GStreamerMediaProvider.IsAvailable && RunTool("gst-launch-1.0", "--version", out _), "GStreamer and gst-launch-1.0 are not installed.");
    }

    private static bool RunTool(string tool, string arguments, out string output)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(tool, arguments) { RedirectStandardOutput = true, RedirectStandardError = true })!;
            output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            return process.WaitForExit(60_000) && process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            output = "";
            return false;
        }
    }

    private string MakeClip(string name, string pipeline)
    {
        var path = Path.Combine(_directory, name);
        Assert.True(RunTool("gst-launch-1.0", "-q " + pipeline + " ! filesink location=" + path, out var output), output);
        return path;
    }

    /// <summary>A moving ball, 64x48 at 10 frames a second: <paramref name="frames"/> frames.</summary>
    private string BallClip(int frames = 30) => MakeClip("ball" + frames + ".webm",
        $"videotestsrc num-buffers={frames} pattern=ball ! video/x-raw,width=64,height=48,framerate=10/1 ! vp9enc cpu-used=8 deadline=1 ! webmmux");

    private string RedClip() => MakeClip("red.webm",
        "videotestsrc num-buffers=10 pattern=solid-color foreground-color=0xffff0000 ! video/x-raw,width=64,height=48,framerate=10/1 ! vp9enc cpu-used=8 deadline=1 ! webmmux");

    private static MediaRequest Request(string path, MediaKind kind = MediaKind.Video, bool muted = true, bool loop = false) =>
        new(new Uri("https://media.test/" + Path.GetFileName(path)), kind, new RasterRenderContext(),
            _ => new ValueTask<RNetworkResponse?>(new RNetworkResponse(File.OpenRead(path), null)))
        { Muted = muted, Loop = loop };

    private static GStreamerMediaProvider Deterministic() => new(new MediaProviderOptions { DeterministicClock = true });

    private static byte[] Bytes(MediaFrame frame) => frame.Image.GetPixels()!.Value.PremultipliedRgba.ToArray();

    [Fact]
    public void CanPlayType_SaysWhatTheInstalledGStreamerCanPlay()
    {
        Assert.SkipUnless(GStreamerMediaProvider.IsAvailable, "GStreamer is not installed.");
        var provider = new GStreamerMediaProvider();

        Assert.Equal("maybe", provider.CanPlayType("video/webm"));
        Assert.Equal("", provider.CanPlayType("video/x-unknown"));
        Assert.Equal("", provider.CanPlayType("video/webm; codecs=\"no-such-codec\""));
        Assert.Equal("", provider.CanPlayType("text/html"));
        Assert.Equal("probably", provider.CanPlayType("video/webm; codecs=\"vp9\""));
    }

    [Fact]
    public async Task OpenAsync_LearnsTheDurationAndThePictureSize()
    {
        RequireGStreamer();
        using var session = await Deterministic().OpenAsync(Request(BallClip()));

        Assert.NotNull(session);
        Assert.True(session.HasVideo);
        Assert.Equal((64, 48), (session.VideoWidth, session.VideoHeight));
        Assert.InRange(session.Duration!.Value.TotalSeconds, 2.9, 3.2);
        Assert.Equal(MediaReadyState.EnoughData, session.ReadyState);
        Assert.Equal(TimeSpan.Zero, session.Position);
        Assert.False(session.IsPlaying);

        using var frame = session.AcquireFrame();
        Assert.NotNull(frame);
        Assert.Equal(64, frame.Image.Width);
        Assert.Equal(48, frame.Image.Height);
        Assert.Equal(MediaColorSpace.Srgb, frame.ColorSpace);
    }

    [Fact]
    public async Task OpenAsync_ReturnsNull_ForDataItCannotPlay()
    {
        RequireGStreamer();
        var path = Path.Combine(_directory, "junk.webm");
        await File.WriteAllBytesAsync(path, Enumerable.Range(0, 4096).Select(i => (byte)(i * 31)).ToArray());

        Assert.Null(await Deterministic().OpenAsync(Request(path)));
    }

    [Fact]
    public async Task OpenAsync_ReturnsNull_WhenTheLoaderHasNothing()
    {
        RequireGStreamer();
        var request = new MediaRequest(new Uri("https://media.test/none.webm"), MediaKind.Video, new RasterRenderContext(), _ => new ValueTask<RNetworkResponse?>((RNetworkResponse?)null));

        Assert.Null(await Deterministic().OpenAsync(request));
    }

    [Fact]
    public async Task ADeterministicSession_MovesOnlyWithTheDocumentClock_AndShowsThePictureAtThePosition()
    {
        RequireGStreamer();
        using var session = await Deterministic().OpenAsync(Request(BallClip()));
        Assert.NotNull(session);

        session.Update(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.Zero, session.Position);

        using var first = session.AcquireFrame();
        session.Play();
        session.Update(TimeSpan.FromSeconds(11));
        Assert.Equal(TimeSpan.FromSeconds(1), session.Position);
        using var later = session.AcquireFrame();
        Assert.NotNull(first);
        Assert.NotNull(later);
        Assert.NotEqual(first.Time, later.Time);
        Assert.NotEqual(Bytes(first), Bytes(later));

        session.Pause();
        session.Update(TimeSpan.FromSeconds(20));
        Assert.Equal(TimeSpan.FromSeconds(1), session.Position);
        Assert.False(session.IsPlaying);
    }

    [Fact]
    public async Task ADeterministicSession_ShowsTheSamePicture_HoweverItGotToThePosition()
    {
        RequireGStreamer();
        var clip = BallClip();
        using var played = await Deterministic().OpenAsync(Request(clip));
        using var sought = await Deterministic().OpenAsync(Request(clip));
        Assert.NotNull(played);
        Assert.NotNull(sought);

        played.Update(TimeSpan.Zero);
        played.Play();
        for (var step = 1; step <= 15; step++)
            played.Update(TimeSpan.FromMilliseconds(step * 100));

        sought.Seek(TimeSpan.FromMilliseconds(1500));

        using var a = played.AcquireFrame();
        using var b = sought.AcquireFrame();
        Assert.Equal(TimeSpan.FromMilliseconds(1500), played.Position);
        Assert.Equal(Bytes(a!), Bytes(b!));
        Assert.Equal(a!.Time, b!.Time);
    }

    [Fact]
    public async Task APictureHoldsUntilTheNextOneStarts_SoTheFrameTimeIsTheSameBetweenThem()
    {
        RequireGStreamer();
        using var session = await Deterministic().OpenAsync(Request(BallClip()));
        Assert.NotNull(session);

        session.Seek(TimeSpan.FromMilliseconds(510));
        using var a = session.AcquireFrame();
        session.Seek(TimeSpan.FromMilliseconds(540));
        using var b = session.AcquireFrame();
        session.Seek(TimeSpan.FromMilliseconds(610));
        using var c = session.AcquireFrame();

        Assert.Equal(a!.Time, b!.Time);
        Assert.NotEqual(a.Time, c!.Time);
    }

    [Fact]
    public async Task ADeterministicSession_EndsAtTheEnd_AndTellsTheDocument()
    {
        RequireGStreamer();
        using var session = await Deterministic().OpenAsync(Request(BallClip(10)));
        Assert.NotNull(session);
        var changes = MediaChange.None;
        session.Changed += (_, e) => changes |= e.Changes;

        session.Update(TimeSpan.Zero);
        session.Play();
        session.Update(TimeSpan.FromSeconds(30));

        Assert.True(session.Ended);
        Assert.False(session.IsPlaying);
        Assert.Equal(session.Duration, session.Position);
        Assert.True(changes.HasFlag(MediaChange.Ended));
        Assert.True(changes.HasFlag(MediaChange.Frame));
    }

    [Fact]
    public async Task ALoopingSession_WrapsAround()
    {
        RequireGStreamer();
        using var session = await Deterministic().OpenAsync(Request(BallClip(10), loop: true));
        Assert.NotNull(session);

        session.Update(TimeSpan.Zero);
        session.Play();
        session.Update(TimeSpan.FromMilliseconds(1500));

        Assert.False(session.Ended);
        Assert.True(session.IsPlaying);
        Assert.InRange(session.Position.TotalSeconds, 0.4, 0.6);
    }

    [Fact]
    public async Task APlaybackRate_ScalesHowFarTheClockMovesThePosition()
    {
        RequireGStreamer();
        using var session = await Deterministic().OpenAsync(Request(BallClip()));
        Assert.NotNull(session);

        session.Update(TimeSpan.Zero);
        session.PlaybackRate = 2;
        session.Play();
        session.Update(TimeSpan.FromSeconds(1));

        Assert.Equal(TimeSpan.FromSeconds(2), session.Position);
    }

    [Fact]
    public async Task APictureIsTheColorOfTheVideo_AsStraightRgbaPremultipliedForDrawing()
    {
        RequireGStreamer();
        using var session = await Deterministic().OpenAsync(Request(RedClip()));
        Assert.NotNull(session);

        using var frame = session.AcquireFrame();
        var pixels = Bytes(frame!);
        // Rec. 601 red color-managed into sRGB, as a browser shows it: a pure red is (247, 37, 0), not (255, 0, 0).
        Assert.InRange(pixels[0], 235, 255);
        Assert.InRange(pixels[1], 25, 50);
        Assert.InRange(pixels[2], 0, 12);
        Assert.Equal(255, pixels[3]);
    }

    [Fact]
    public async Task AcquireFrameAtAsync_FindsThePictureAtAPosition_WithoutMovingThePlayer()
    {
        RequireGStreamer();
        using var session = await Deterministic().OpenAsync(Request(BallClip()));
        Assert.NotNull(session);

        using var start = await session.AcquireFrameAtAsync(TimeSpan.Zero);
        using var middle = await session.AcquireFrameAtAsync(TimeSpan.FromSeconds(1.5));

        Assert.NotNull(start);
        Assert.NotNull(middle);
        Assert.NotEqual(Bytes(start), Bytes(middle));
        Assert.Equal(TimeSpan.Zero, session.Position);
    }

    [Fact]
    public async Task AnAudioSession_HasNoPicture_ButHasADuration()
    {
        RequireGStreamer();
        var clip = MakeClip("tone.ogg", "audiotestsrc num-buffers=100 samplesperbuffer=4410 ! audio/x-raw,rate=44100 ! audioconvert ! vorbisenc ! oggmux");
        using var session = await Deterministic().OpenAsync(Request(clip, MediaKind.Audio));

        Assert.NotNull(session);
        Assert.False(session.HasVideo);
        Assert.Null(session.AcquireFrame());
        Assert.InRange(session.Duration!.Value.TotalSeconds, 9, 11);
    }

    [Fact]
    public async Task ARealTimeSession_PlaysByItsOwnClock_AndEnds()
    {
        RequireGStreamer();
        using var session = await new GStreamerMediaProvider().OpenAsync(Request(BallClip(10)));
        Assert.NotNull(session);

        session.Play();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!session.Ended && DateTime.UtcNow < deadline)
        {
            session.Update(TimeSpan.Zero);
            await Task.Delay(50);
        }

        Assert.True(session.Ended);
        Assert.False(session.IsPlaying);
        Assert.Equal(MediaErrorCode.None, session.Error);
        using var frame = session.AcquireFrame();
        Assert.NotNull(frame);
    }

    [Fact]
    public async Task DisposingASession_DeletesTheCopyOfTheResource()
    {
        RequireGStreamer();
        var before = Directory.GetFiles(Path.GetTempPath(), "peachbrackets-media-*").Length;
        var session = await Deterministic().OpenAsync(Request(BallClip()));
        Assert.NotNull(session);
        Assert.Equal(before + 1, Directory.GetFiles(Path.GetTempPath(), "peachbrackets-media-*").Length);

        session.Dispose();
        session.Dispose();

        Assert.Equal(before, Directory.GetFiles(Path.GetTempPath(), "peachbrackets-media-*").Length);
    }

    [Fact]
    public async Task AScriptDrawsTheVideoOnACanvas_ThroughTheProvider()
    {
        RequireGStreamer();
        var clip = RedClip();
        var logger = new LogCollector();
        var host = new PeachBrackets.Scripting.JintScriptHost(new PeachBrackets.Scripting.JintScriptOptions { Logger = logger });
        var context = new RasterRenderContext
        {
            ResourceLoader = uri => uri.Host == "media.test" ? new ValueTask<RNetworkResponse?>(new RNetworkResponse(File.OpenRead(clip), null)) : default,
        };
        var options = new HtmlDocumentOptions
        {
            MediaType = MediaType.Screen,
            PageWidthPoints = 200,
            ScriptHost = host,
            MeasuringCanvasFactory = () => context.CreateCanvas(1, 1),
            MediaProvider = Deterministic(),
        };
        options.SetMargins(0);

        using var document = await HtmlDocument.LoadAsync("""
            <!DOCTYPE html><html><body><script>
            var v = document.createElement('video'); v.setAttribute('crossorigin', 'anonymous');
            v.src = 'https://media.test/red.webm';
            setTimeout(function () {
                var c = document.createElement('canvas'); c.width = 8; c.height = 8;
                var g = c.getContext('2d'); g.drawImage(v, 0, 0, 8, 8);
                var d = g.getImageData(4, 4, 1, 1).data;
                Log.info(v.videoWidth + 'x' + v.videoHeight + ' ' + (d[0] > 200 && d[1] < 40 && d[2] < 40 && d[3] === 255));
            }, 3000);
            </script></body></html>
            """, context, options);

        Assert.Empty(logger.Errors);
        Assert.Equal(["64x48 true"], logger.Texts);
    }

    [Fact]
    public async Task CreateImageBitmap_OfALoadedVideo_RejectsABadCrop_AndMakesABitmapOtherwise()
    {
        RequireGStreamer();
        var clip = RedClip();
        var (logger, document) = await LoadScriptAsync(clip, """
            var v = document.createElement('video'); v.setAttribute('crossorigin', 'anonymous'); v.preload = 'auto';
            v.oncanplaythrough = function () { Log.info('cpt');
                createImageBitmap(v, 0, 0, 0, 10).then(function () { Log.info('resolved'); }, function (e) { Log.info('rejected ' + e.name);
                    createImageBitmap(v).then(function (b) { Log.info('bitmap ' + b.width + 'x' + b.height); }, function (e2) { Log.info('failed ' + e2.name); });
                });
            };
            v.src = 'https://media.test/red.webm';
            setTimeout(function () { Log.info('end'); }, 3000);
            """);
        using var _ = document;

        Assert.Empty(logger.Errors);
        Assert.Equal(["cpt", "rejected RangeError", "bitmap 64x48", "end"], logger.Texts);
    }

    private async Task<(LogCollector Logger, HtmlDocument Document)> LoadScriptAsync(string clip, string script)
    {
        var logger = new LogCollector();
        var host = new PeachBrackets.Scripting.JintScriptHost(new PeachBrackets.Scripting.JintScriptOptions { Logger = logger });
        var context = new RasterRenderContext
        {
            ResourceLoader = uri => uri.Host == "media.test" ? new ValueTask<RNetworkResponse?>(new RNetworkResponse(File.OpenRead(clip), null)) : default,
        };
        var options = new HtmlDocumentOptions
        {
            MediaType = MediaType.Screen,
            PageWidthPoints = 200,
            ScriptHost = host,
            MeasuringCanvasFactory = () => context.CreateCanvas(1, 1),
            MediaProvider = Deterministic(),
        };
        options.SetMargins(0);
        var document = await HtmlDocument.LoadAsync("<!DOCTYPE html><html><body><script>" + script + "</script></body></html>", context, options);
        return (logger, document);
    }

    private sealed class LogCollector : Microsoft.Extensions.Logging.ILogger
    {
        public List<string> Texts { get; } = [];

        public List<string> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var text = formatter(state, exception);
            if (eventId == PeachBrackets.Scripting.ScriptLogEvents.Message)
                Texts.Add(text);
            else if (eventId == PeachBrackets.Scripting.ScriptLogEvents.ScriptFailed || eventId == PeachBrackets.Scripting.ScriptLogEvents.ScriptLoadFailed)
                Errors.Add(text);
        }
    }
}
