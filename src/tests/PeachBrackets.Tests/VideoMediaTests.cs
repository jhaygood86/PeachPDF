using PeachBrackets.Media;
using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

/// <summary>A <c>video</c> with a host's <see cref="IMediaProvider"/>: what it shows, how it is sized, and how it follows a playing session.</summary>
public class VideoMediaTests
{
    private sealed class SolidImage(int width, int height, byte r, byte g, byte b) : Image
    {
        public override double Width => width;

        public override double Height => height;

        public override bool Interpolate { get; set; }

        public override void Dispose()
        {
        }

        public override PixelBuffer? GetPixels()
        {
            var pixels = new byte[width * height * 4];
            for (var i = 0; i < pixels.Length; i += 4)
                (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (r, g, b, 255);

            return new PixelBuffer(width, height, pixels);
        }
    }

    private sealed class ScriptedSession(int width, int height) : IMediaSession
    {
        public (byte R, byte G, byte B) Color { get; set; } = (255, 0, 0);

        public TimeSpan FrameTime { get; set; }

        public TimeSpan? RequestedStart { get; private set; }

        public TimeSpan LastDocumentTime { get; private set; }

        public int FramesLeased { get; private set; }

        public int FramesReleased { get; private set; }

        public bool Disposed { get; private set; }

        public MediaKind Kind => MediaKind.Video;

        public MediaReadyState ReadyState => MediaReadyState.EnoughData;

        public TimeSpan? Duration => TimeSpan.FromSeconds(10);

        public bool HasVideo => true;

        public int VideoWidth => width;

        public int VideoHeight => height;

        public TimeSpan Position => FrameTime;

        public bool IsPlaying { get; set; }

        public bool Ended => false;

        public MediaErrorCode Error => MediaErrorCode.None;

        public double PlaybackRate { get; set; } = 1;

        public double Volume { get; set; } = 1;

        public bool Muted { get; set; }

        public bool Loop { get; set; }

        public event EventHandler<MediaChangedEventArgs>? Changed;

        public void Play() => IsPlaying = true;

        public void Pause() => IsPlaying = false;

        public void Seek(TimeSpan position) => FrameTime = position;

        public void Update(TimeSpan documentTime) => LastDocumentTime = documentTime;

        public MediaFrame? AcquireFrame()
        {
            FramesLeased++;
            return new MediaFrame(new SolidImage(width, height, Color.R, Color.G, Color.B), FrameTime, () => FramesReleased++);
        }

        public ValueTask<MediaFrame?> AcquireFrameAtAsync(TimeSpan position, CancellationToken cancellationToken = default)
        {
            RequestedStart = position;
            FrameTime = position;
            return new ValueTask<MediaFrame?>(AcquireFrame());
        }

        public void Dispose() => Disposed = true;

        public void Raise() => Changed?.Invoke(this, new MediaChangedEventArgs(MediaChange.Frame));
    }

    private sealed class ScriptedProvider(ScriptedSession? session, params string[] playable) : IMediaProvider
    {
        public List<MediaRequest> Requests { get; } = [];

        public string CanPlayType(string mimeType) => Array.Exists(playable, p => mimeType.StartsWith(p, StringComparison.OrdinalIgnoreCase)) ? "probably" : string.Empty;

        public ValueTask<IMediaSession?> OpenAsync(MediaRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return new ValueTask<IMediaSession?>(session);
        }
    }

    private static HtmlDocumentOptions Options(IMediaProvider provider, bool animate = false) =>
        new() { MediaType = MediaType.Screen, PageWidthPoints = 400, MediaProvider = provider, AnimationsEnabled = animate };

    [Fact]
    public async Task AVideo_ShowsThePictureOfItsMedia_AtTheSizeOfTheVideo()
    {
        var session = new ScriptedSession(40, 20) { Color = (255, 0, 0) };
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<video id=v src='https://media.test/clip.webm'></video>"), Options(new ScriptedProvider(session)));
        using var _ = document;

        // 40 x 20 pixels are 30 x 15 points: no width or height is given, so the picture's own size is the box's.
        var bounds = document.GetElementBounds("v")!.Value;
        Assert.Equal(30, bounds.Width, 0.5);
        Assert.Equal(15, bounds.Height, 0.5);

        using var canvas = await TestDocument.PaintAsync(context, document);
        var inside = TestDocument.Pixel(canvas, (int)(bounds.X + 10), (int)(bounds.Y + 7));
        Assert.Equal((255, 0, 0), (inside.R, inside.G, inside.B));
        Assert.Equal(255, TestDocument.Pixel(canvas, (int)(bounds.X + 35), (int)(bounds.Y + 5)).G);
    }

    [Fact]
    public async Task AVideoWithoutAProvider_ShowsNothingWithoutAPoster()
    {
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<video id=v src='https://media.test/clip.webm' style='width:40pt;height:20pt'></video><div style='height:100pt'></div>"), new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 400 });
        using var _ = document;

        using var canvas = await TestDocument.PaintAsync(context, document);
        Assert.Equal(255, TestDocument.Pixel(canvas, 30, 30).G);
    }

    [Fact]
    public async Task AProviderThatCannotOpenTheMedia_LeavesTheVideoAsItWouldBeWithoutOne()
    {
        var provider = new ScriptedProvider(null);
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<video id=v src='https://media.test/clip.webm' style='width:40pt;height:20pt'></video><div style='height:100pt'></div>"), Options(provider));
        using var _ = document;

        Assert.Single(provider.Requests);
        using var canvas = await TestDocument.PaintAsync(context, document);
        Assert.Equal(255, TestDocument.Pixel(canvas, 30, 30).G);
    }

    [Fact]
    public async Task ASourceElement_IsChosenByWhatTheProviderPlays()
    {
        var session = new ScriptedSession(40, 20);
        var provider = new ScriptedProvider(session, "video/webm");
        var (_, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap("<video id=v><source src='https://media.test/a.mp4' type='video/mp4'><source src='https://media.test/b.webm' type='video/webm; codecs=vp9'></video>"),
            Options(provider));
        using var _ = document;

        var request = Assert.Single(provider.Requests);
        Assert.Equal("https://media.test/b.webm", request.Address.AbsoluteUri);
        Assert.Equal("video/webm; codecs=vp9", request.ContentType);
        Assert.Equal(MediaKind.Video, request.Kind);
    }

    [Theory]
    [InlineData("#t=12.5", 12.5)]
    [InlineData("#t=npt:1:30", 90)]
    [InlineData("#t=5,9", 5)]
    [InlineData("#t=bad", 0)]
    [InlineData("", 0)]
    public async Task TheMediaFragmentChoosesThePictureShown(string fragment, double expectedSeconds)
    {
        var session = new ScriptedSession(40, 20);
        var provider = new ScriptedProvider(session);
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap($"<video src='https://media.test/clip.webm{fragment}'></video>"), Options(provider));
        using var _ = document;

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), session.RequestedStart);
    }

    [Fact]
    public async Task APlayingVideo_TakesTheSessionsCurrentPictureEachStep_AndRepaintsOnlyWhereItIs()
    {
        var session = new ScriptedSession(40, 20) { Color = (255, 0, 0) };
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<video id=v src='https://media.test/clip.webm' autoplay></video><div style='height:200pt'></div>"), Options(new ScriptedProvider(session), animate: true));
        using var _ = document;

        Assert.True(session.IsPlaying);
        var first = document.AdvanceAnimations(TimeSpan.Zero);
        Assert.True(first.IsRunning);

        // The session moves on to a green picture.
        session.Color = (0, 255, 0);
        session.FrameTime = TimeSpan.FromSeconds(0.1);
        var second = document.AdvanceAnimations(TimeSpan.FromSeconds(0.1));
        Assert.Equal(TimeSpan.FromSeconds(0.1), session.LastDocumentTime);
        Assert.True(second.IsRunning);
        Assert.NotEmpty(second.DirtyRegions);

        using var canvas = await TestDocument.PaintAsync(context, document);
        var bounds = document.GetElementBounds("v")!.Value;
        var pixel = TestDocument.Pixel(canvas, (int)(bounds.X + 10), (int)(bounds.Y + 7));
        Assert.Equal((0, 255, 0), (pixel.R, pixel.G, pixel.B));

        // The session pauses: the step that sees it says nothing is running, and a step with no new picture repaints nothing.
        session.Pause();
        var third = document.AdvanceAnimations(TimeSpan.FromSeconds(0.2));
        Assert.False(third.IsRunning);
        Assert.Empty(third.DirtyRegions);
    }

    [Fact]
    public async Task AVideoOfAPagedDocument_IsNotPlayed()
    {
        var session = new ScriptedSession(40, 20);
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<video src='https://media.test/clip.webm' autoplay></video>"), Options(new ScriptedProvider(session), animate: false));
        using var _ = document;

        Assert.False(session.IsPlaying);
    }

    [Fact]
    public async Task DisposingTheDocument_GivesBackTheFramesAndTheSession()
    {
        var session = new ScriptedSession(40, 20);
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<video src='https://media.test/clip.webm'></video>"), Options(new ScriptedProvider(session)));

        Assert.False(session.Disposed);
        document.Dispose();

        Assert.True(session.Disposed);
        Assert.Equal(session.FramesLeased, session.FramesReleased);
    }
}
