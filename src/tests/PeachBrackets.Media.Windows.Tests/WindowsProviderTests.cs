using PeachBrackets.Media.Platform;
using PeachBrackets.Media.Windows;
using PeachDrawing.Core;
using PeachDrawing.Win2D;

namespace PeachBrackets.Media.Windows.Tests;

/// <summary>The provider against the real Windows media stack: small H.264 clips played into Win2D bitmaps.</summary>
public class WindowsProviderTests
{
    private static void RequireWindows() => Assert.SkipUnless(WindowsMediaProvider.IsAvailable, "Media Foundation is not installed.");

    private static string Clip(string name) => Path.Combine(AppContext.BaseDirectory, "Assets", name);

    private static MediaRequest Request(Win2dRenderContext context, string clip, MediaKind kind = MediaKind.Video, bool muted = true) =>
        new(new Uri("https://media.test/" + clip), kind, context, _ => new ValueTask<RNetworkResponse?>(new RNetworkResponse(File.OpenRead(Clip(clip)), null)))
        {
            Muted = muted,
        };

    private static WindowsMediaProvider Deterministic() => new(new MediaProviderOptions { DeterministicClock = true });

    private static byte[] Bytes(MediaFrame frame) => frame.Image.GetPixels()!.Value.PremultipliedRgba.ToArray();

    [Fact]
    public void CanPlayType_SaysWhatThisWindowsCanPlay()
    {
        RequireWindows();
        var provider = new WindowsMediaProvider();

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
        using var context = Win2dRenderContext.CreateSoftware();
        using var session = await Deterministic().OpenAsync(Request(context, "moving.mp4"), TestContext.Current.CancellationToken);

        Assert.NotNull(session);
        Assert.True(session.HasVideo);
        Assert.Equal((64, 48), (session.VideoWidth, session.VideoHeight));
        Assert.InRange(session.Duration!.Value.TotalSeconds, 2.8, 3.3);
        Assert.Equal(MediaReadyState.EnoughData, session.ReadyState);

        using var frame = session.AcquireFrame();
        Assert.NotNull(frame);
        Assert.Equal((64, 48), (frame.Image.Width, frame.Image.Height));
        Assert.Equal(MediaColorSpace.Srgb, frame.ColorSpace);
    }

    [Fact]
    public async Task APicture_IsTheColorOfTheVideo()
    {
        RequireWindows();
        using var context = Win2dRenderContext.CreateSoftware();
        using var session = await Deterministic().OpenAsync(Request(context, "red.mp4"), TestContext.Current.CancellationToken);
        Assert.NotNull(session);

        using var frame = session.AcquireFrame();
        var pixels = Bytes(frame!);
        var center = ((24 * 64) + 32) * 4;

        // 0xE02020 in the file. Media Foundation converts a small picture its own way (its red here is a little more saturated than a Rec. 601 decode
        // gives), so this says "a strong red", and the HD test below pins the Rec. 709 matrix exactly.
        Assert.InRange(pixels[center], 200, 255);
        Assert.InRange(pixels[center + 1], 10, 70);
        Assert.InRange(pixels[center + 2], 10, 50);
        Assert.Equal(255, pixels[center + 3]);
    }

    [Fact]
    public async Task AnHdPicture_IsDecodedWithTheRec709Matrix_OnAHardwareDevice()
    {
        RequireWindows();
        // The player's frame server needs a hardware device for a picture this large; on a machine that has none (a hosted runner) the frame never comes.
        using var context = new Win2dRenderContext();
        using var session = await Deterministic().OpenAsync(Request(context, "red720.mp4"), TestContext.Current.CancellationToken);
        Assert.SkipUnless(session is not null, "The player did not open the clip.");

        using var frame = session.AcquireFrame();
        var pixels = Bytes(frame!);
        var center = ((360 * 1280) + 640) * 4;
        Assert.SkipUnless(pixels[center + 3] != 0, "This machine has no hardware device for the frame server.");

        Assert.InRange(pixels[center], 0xE0 - 14, 0xE0 + 14);
        Assert.InRange(pixels[center + 1], 0x20 - 14, 0x20 + 14);
        Assert.InRange(pixels[center + 2], 0x20 - 14, 0x20 + 14);
    }

    [Fact]
    public async Task ADeterministicSession_MovesOnlyWithTheDocumentClock_AndShowsThePictureAtThePosition()
    {
        RequireWindows();
        using var context = Win2dRenderContext.CreateSoftware();
        using var session = await Deterministic().OpenAsync(Request(context, "moving.mp4"), TestContext.Current.CancellationToken);
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
        using var context = Win2dRenderContext.CreateSoftware();
        using var played = await Deterministic().OpenAsync(Request(context, "moving.mp4"), TestContext.Current.CancellationToken);
        using var sought = await Deterministic().OpenAsync(Request(context, "moving.mp4"), TestContext.Current.CancellationToken);
        Assert.NotNull(played);
        Assert.NotNull(sought);

        played.Update(TimeSpan.Zero);
        played.Play();
        for (var step = 1; step <= 10; step++)
            played.Update(TimeSpan.FromMilliseconds(step * 150));

        sought.Seek(TimeSpan.FromMilliseconds(1500));

        using var a = played.AcquireFrame();
        using var b = sought.AcquireFrame();
        Assert.Equal(Bytes(a!), Bytes(b!));
    }

    [Fact]
    public async Task ADeterministicSession_EndsAtTheEnd_AndTellsTheDocument()
    {
        RequireWindows();
        using var context = Win2dRenderContext.CreateSoftware();
        using var session = await Deterministic().OpenAsync(Request(context, "red.mp4"), TestContext.Current.CancellationToken);
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
    }

    [Fact]
    public async Task AFrameStaysUsable_AfterItsSessionIsDisposed()
    {
        RequireWindows();
        using var context = Win2dRenderContext.CreateSoftware();
        var session = await Deterministic().OpenAsync(Request(context, "red.mp4"), TestContext.Current.CancellationToken);
        Assert.NotNull(session);
        var frame = session.AcquireFrame();
        Assert.NotNull(frame);

        session.Dispose();
        session.Dispose();

        Assert.Equal(64 * 48 * 4, Bytes(frame).Length);
        frame.Dispose();
    }

    [Fact]
    public async Task AnAudioSession_HasNoPicture_ButHasADuration()
    {
        RequireWindows();
        using var context = Win2dRenderContext.CreateSoftware();
        using var session = await Deterministic().OpenAsync(Request(context, "moving.mp4", MediaKind.Audio), TestContext.Current.CancellationToken);

        Assert.NotNull(session);
        Assert.False(session.HasVideo);
        Assert.Null(session.AcquireFrame());
        Assert.InRange(session.Duration!.Value.TotalSeconds, 2.8, 3.3);
    }

    [Fact]
    public async Task ARealTimeSession_PlaysByItsOwnClock_AndEnds()
    {
        RequireWindows();
        using var context = Win2dRenderContext.CreateSoftware();
        using var session = await new WindowsMediaProvider().OpenAsync(Request(context, "red.mp4"), TestContext.Current.CancellationToken);
        Assert.NotNull(session);

        session.Play();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!session.Ended && DateTime.UtcNow < deadline)
            await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.True(session.Ended);
        Assert.False(session.IsPlaying);
        Assert.Equal(MediaErrorCode.None, session.Error);
    }

    [Fact]
    public async Task OnWindows_ThePlatformProviderPlaysADocumentDrawnWithWin2D_ThroughMediaFoundation()
    {
        RequireWindows();
        var provider = PlatformMediaProvider.Create(new MediaProviderOptions { DeterministicClock = true });
        Assert.NotNull(provider);
        Assert.Equal("probably", provider.CanPlayType("video/mp4; codecs=\"avc1.42E01E\""));

        using var context = Win2dRenderContext.CreateSoftware();
        using var session = await provider.OpenAsync(Request(context, "moving.mp4"), TestContext.Current.CancellationToken);

        Assert.NotNull(session);
        using var frame = session.AcquireFrame();
        Assert.NotNull(frame);
        Assert.Equal((64, 48), (frame.Image.Width, frame.Image.Height));
    }

    [Fact]
    public async Task OnWindows_ADocumentDrawnAnyOtherWay_GetsItsPicturesAsPixels()
    {
        RequireWindows();
        var provider = PlatformMediaProvider.Create(new MediaProviderOptions { DeterministicClock = true });
        Assert.NotNull(provider);

        var raster = new PeachDrawing.RasterRenderContext();
        var request = new MediaRequest(new Uri("https://media.test/moving.mp4"), MediaKind.Video, raster,
            _ => new ValueTask<RNetworkResponse?>(new RNetworkResponse(File.OpenRead(Clip("moving.mp4")), null)));
        using var session = await provider.OpenAsync(request, TestContext.Current.CancellationToken);

        Assert.NotNull(session);
        Assert.InRange(session.Duration!.Value.TotalSeconds, 2.8, 3.3);

        // A picture read back from the shared device, as pixels any canvas can draw.
        using var frame = session.AcquireFrame();
        Assert.NotNull(frame);
        Assert.Equal((64, 48), (frame.Image.Width, frame.Image.Height));
        Assert.Equal(64 * 48 * 4, frame.Image.GetPixels()!.Value.PremultipliedRgba.Length);
    }

    [Fact]
    public async Task OpenAsync_ReturnsNull_ForDataItCannotPlay()
    {
        RequireWindows();
        using var context = Win2dRenderContext.CreateSoftware();
        var request = new MediaRequest(new Uri("https://media.test/junk.mp4"), MediaKind.Video, context,
            _ => new ValueTask<RNetworkResponse?>(new RNetworkResponse(new MemoryStream(Enumerable.Range(0, 4096).Select(i => (byte)(i * 31)).ToArray()), null)));

        Assert.Null(await Deterministic().OpenAsync(request, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OpenAsync_ReturnsNull_WhenTheLoaderHasNothing()
    {
        RequireWindows();
        using var context = Win2dRenderContext.CreateSoftware();
        var request = new MediaRequest(new Uri("https://media.test/none.mp4"), MediaKind.Video, context, _ => new ValueTask<RNetworkResponse?>((RNetworkResponse?)null));

        Assert.Null(await Deterministic().OpenAsync(request, TestContext.Current.CancellationToken));
    }
}
