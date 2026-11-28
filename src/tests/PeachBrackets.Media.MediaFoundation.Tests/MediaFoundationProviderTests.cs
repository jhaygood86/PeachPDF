using PeachBrackets.Media.MediaFoundation;
using PeachBrackets.Media.MediaFoundation.Internal;
using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Media.MediaFoundation.Tests;

/// <summary>The provider against the real Windows media stack, from a plain net10.0 program: small H.264 clips played into pixels.</summary>
public class MediaFoundationProviderTests
{
    private static string Clip(string name) => Path.Combine(AppContext.BaseDirectory, "Assets", name);

    private static void RequireWindows() => Assert.SkipUnless(MediaFoundationMediaProvider.IsAvailable, "Media Foundation is Windows only.");

    private static MediaRequest Request(string clip, MediaKind kind = MediaKind.Video, bool muted = true) =>
        new(new Uri("https://media.test/" + clip), kind, new RasterRenderContext(), _ => new ValueTask<RNetworkResponse?>(new RNetworkResponse(File.OpenRead(Clip(clip)), null)))
        {
            Muted = muted,
        };

    private static MediaFoundationMediaProvider Deterministic() => new(new MediaProviderOptions { DeterministicClock = true });

    private static byte[] Bytes(MediaFrame frame) => frame.Image.GetPixels()!.Value.PremultipliedRgba.ToArray();

    [Fact]
    public void CanPlayType_SaysWhatThisWindowsCanPlay()
    {
        RequireWindows();
        var provider = new MediaFoundationMediaProvider();

        Assert.Equal("maybe", provider.CanPlayType("video/mp4"));
        Assert.Equal("probably", provider.CanPlayType("video/mp4; codecs=\"avc1.42E01E, mp4a.40.2\""));
        Assert.Equal("", provider.CanPlayType("video/mp4; codecs=\"no-such-codec\""));
        Assert.Equal("", provider.CanPlayType("text/html"));
        Assert.Equal("", provider.CanPlayType("video/x-unknown"));
    }

    [Fact]
    public async Task OpenAsync_LearnsTheDurationAndThePictureSize_AndHasTheFirstPicture()
    {
        RequireWindows();
        using var session = await Deterministic().OpenAsync(Request("moving.mp4"), TestContext.Current.CancellationToken);

        Assert.NotNull(session);
        Assert.True(session.HasVideo);
        Assert.Equal((64, 48), (session.VideoWidth, session.VideoHeight));
        Assert.InRange(session.Duration!.Value.TotalSeconds, 2.8, 3.3);
        Assert.Equal(MediaReadyState.EnoughData, session.ReadyState);

        using var frame = session.AcquireFrame();
        Assert.NotNull(frame);
        Assert.Equal((64, 48), (frame.Image.Width, frame.Image.Height));
        Assert.Equal(64 * 48 * 4, Bytes(frame).Length);
    }

    [Fact]
    public async Task APicture_IsUprightAndTheColorOfTheVideo()
    {
        RequireWindows();
        using var session = await Deterministic().OpenAsync(Request("halves.mp4"), TestContext.Current.CancellationToken);
        Assert.NotNull(session);

        using var frame = session.AcquireFrame();
        var pixels = Bytes(frame!);
        int Px(int x, int y, int channel) => pixels[(((y * 64) + x) * 4) + channel];

        // The top half of the clip is red, the bottom half blue (0xE02020 and 0x2020E0 in Rec. 601): a picture that came out upside down would swap them.
        Assert.InRange(Px(32, 6, 0), 190, 255);
        Assert.InRange(Px(32, 6, 2), 0, 70);
        Assert.InRange(Px(32, 42, 0), 0, 70);
        Assert.InRange(Px(32, 42, 2), 190, 255);
        Assert.Equal(255, Px(32, 6, 3));
    }

    [Fact]
    public async Task ARec709Picture_ThatTheDecoderPadsToABlockSize_IsDecodedByItsOwnMatrix()
    {
        RequireWindows();
        using var session = await Deterministic().OpenAsync(Request("red709-1000x600.mp4"), TestContext.Current.CancellationToken);
        Assert.NotNull(session);
        Assert.Equal((1000, 600), (session.VideoWidth, session.VideoHeight));

        using var frame = session.AcquireFrame();
        var pixels = Bytes(frame!);
        int Px(int x, int y, int channel) => pixels[(((y * 1000) + x) * 4) + channel];

        // 0xFF0100 through Rec. 709. The decoder pads a 1000 x 600 picture to rows of 1008 bytes and 608 rows, which a layout taken from the media type would read as noise; a
        // picture of more than 576 lines is taken as Rec. 709, where a small one is taken as Rec. 601 (Media Foundation does not pass an H.264 stream's color tags on).
        foreach (var (x, y) in new[] { (5, 5), (500, 300), (994, 594) })
        {
            Assert.InRange(Px(x, y, 0), 250, 255);
            Assert.InRange(Px(x, y, 1), 0, 8);
            Assert.InRange(Px(x, y, 2), 0, 8);
        }

        Assert.Equal(MediaColorSpace.Srgb, frame!.ColorSpace);
    }

    [Fact]
    public async Task ADeterministicSession_MovesOnlyWithTheDocumentClock_AndShowsThePictureAtThePosition()
    {
        RequireWindows();
        using var session = await Deterministic().OpenAsync(Request("moving.mp4"), TestContext.Current.CancellationToken);
        Assert.NotNull(session);

        session.Update(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.Zero, session.Position);

        using var first = session.AcquireFrame();
        session.Play();
        session.Update(TimeSpan.FromSeconds(11.5));
        Assert.Equal(TimeSpan.FromSeconds(1.5), session.Position);
        using var later = session.AcquireFrame();

        Assert.NotNull(first);
        Assert.NotNull(later);
        Assert.NotEqual(Bytes(first), Bytes(later));

        session.Pause();
        session.Update(TimeSpan.FromSeconds(30));
        Assert.Equal(TimeSpan.FromSeconds(1.5), session.Position);
    }

    [Fact]
    public async Task ADeterministicSession_ShowsTheSamePicture_HoweverItGotToThePosition()
    {
        RequireWindows();
        using var played = await Deterministic().OpenAsync(Request("moving.mp4"), TestContext.Current.CancellationToken);
        using var sought = await Deterministic().OpenAsync(Request("moving.mp4"), TestContext.Current.CancellationToken);
        Assert.NotNull(played);
        Assert.NotNull(sought);

        played.Update(TimeSpan.Zero);
        played.Play();
        for (var step = 1; step <= 10; step++)
        {
            played.Update(TimeSpan.FromMilliseconds(step * 150));
            played.AcquireFrame()?.Dispose();
        }

        sought.Seek(TimeSpan.FromMilliseconds(1500));

        using var a = played.AcquireFrame();
        using var b = sought.AcquireFrame();
        Assert.Equal(Bytes(a!), Bytes(b!));
        Assert.Equal(a!.Time, b!.Time);
    }

    [Fact]
    public async Task APictureHoldsUntilTheNextOneStarts_SoTheFrameTimeIsTheSameBetweenThem()
    {
        RequireWindows();
        using var session = await Deterministic().OpenAsync(Request("moving.mp4"), TestContext.Current.CancellationToken);
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
    public async Task ADeterministicSession_EndsAtTheEnd_AndTellsTheDocument_AndLoops()
    {
        RequireWindows();
        using var session = await Deterministic().OpenAsync(Request("red.mp4"), TestContext.Current.CancellationToken);
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

        session.Loop = true;
        session.Seek(TimeSpan.Zero);
        session.Play();
        session.Update(TimeSpan.FromSeconds(30.5));
        Assert.False(session.Ended);
        Assert.True(session.IsPlaying);
        Assert.InRange(session.Position.TotalSeconds, 0, 1);
    }

    [Fact]
    public async Task AnAudioSession_HasNoPicture_ButHasADuration()
    {
        RequireWindows();
        using var session = await Deterministic().OpenAsync(Request("moving.mp4", MediaKind.Audio), TestContext.Current.CancellationToken);

        Assert.NotNull(session);
        Assert.False(session.HasVideo);
        Assert.Null(session.AcquireFrame());
        Assert.InRange(session.Duration!.Value.TotalSeconds, 2.8, 3.3);
    }

    [Fact]
    public async Task ARealTimeSession_PlaysByItsOwnClock_AndEnds()
    {
        RequireWindows();
        using var session = await new MediaFoundationMediaProvider().OpenAsync(Request("red.mp4"), TestContext.Current.CancellationToken);
        Assert.NotNull(session);

        session.Play();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!session.Ended && DateTime.UtcNow < deadline)
        {
            session.AcquireFrame()?.Dispose();
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.True(session.Ended);
        Assert.False(session.IsPlaying);
        Assert.Equal(MediaErrorCode.None, session.Error);
    }

    [Fact]
    public async Task ARealTimeSession_WithTheSoundOn_RendersItsSound_ThroughTheDefaultOutput()
    {
        RequireWindows();
        using var session = (MfSession?)await new MediaFoundationMediaProvider().OpenAsync(Request("moving.mp4", muted: false), TestContext.Current.CancellationToken);
        Assert.NotNull(session);

        session.Volume = 0.01;
        session.Play();
        await Task.Delay(1200, TestContext.Current.CancellationToken);

        // A machine with no output device (a hosted runner) plays the clip without sound: the session still runs.
        Assert.SkipWhen(!session.HasSound && Environment.GetEnvironmentVariable("CI") is not null, "This machine has no audio output device.");
        Assert.True(session.HasSound, "the sound should be rendered through the default output");
        Assert.InRange(session.Position.TotalSeconds, 0.8, 2.0);
        session.Seek(TimeSpan.FromSeconds(2));
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.True(session.IsPlaying);
        session.Pause();
        Assert.False(session.IsPlaying);
    }

    [Fact]
    public async Task ASessionPlayedFaster_MovesItsPositionFaster_AndKeepsItsSound()
    {
        RequireWindows();
        using var session = (MfSession?)await new MediaFoundationMediaProvider().OpenAsync(Request("moving.mp4", muted: false), TestContext.Current.CancellationToken);
        Assert.NotNull(session);

        session.Volume = 0.01;
        session.Play();
        await Task.Delay(300, TestContext.Current.CancellationToken);
        session.PlaybackRate = 2;
        var before = session.Position;
        await Task.Delay(800, TestContext.Current.CancellationToken);
        var moved = (session.Position - before).TotalSeconds;

        Assert.SkipWhen(!session.HasSound && Environment.GetEnvironmentVariable("CI") is not null, "This machine has no audio output device.");
        Assert.InRange(moved, 1.2, 2.1);
        Assert.True(session.HasSound, "the sound should still be rendered after the rate changed");
        Assert.True(session.IsPlaying);
    }

    [Fact]
    public async Task OpenAsync_ReturnsNull_ForDataItCannotPlay_AndForNothing()
    {
        RequireWindows();
        var junk = new MediaRequest(new Uri("https://media.test/junk.mp4"), MediaKind.Video, new RasterRenderContext(),
            _ => new ValueTask<RNetworkResponse?>(new RNetworkResponse(new MemoryStream(Enumerable.Range(0, 4096).Select(i => (byte)(i * 31)).ToArray()), null)));
        var none = new MediaRequest(new Uri("https://media.test/none.mp4"), MediaKind.Video, new RasterRenderContext(), _ => new ValueTask<RNetworkResponse?>((RNetworkResponse?)null));

        Assert.Null(await Deterministic().OpenAsync(junk, TestContext.Current.CancellationToken));
        Assert.Null(await Deterministic().OpenAsync(none, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisposingASession_DeletesTheCopyOfTheResource()
    {
        RequireWindows();
        // A session an earlier test left undisposed deletes its copy when it is finalized; let that happen first, or it lands between the counts.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var before = Directory.GetFiles(Path.GetTempPath(), "peachbrackets-media-*").Length;
        var session = await Deterministic().OpenAsync(Request("red.mp4"), TestContext.Current.CancellationToken);
        Assert.NotNull(session);
        Assert.Equal(before + 1, Directory.GetFiles(Path.GetTempPath(), "peachbrackets-media-*").Length);

        session.Dispose();
        session.Dispose();

        Assert.Equal(before, Directory.GetFiles(Path.GetTempPath(), "peachbrackets-media-*").Length);
    }
}
