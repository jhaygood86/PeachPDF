using Android.App;
using Android.OS;
using Android.Util;
using PeachBrackets.Media;
using PeachBrackets.Media.Android;
using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Media.Android.Example;

[Activity(Name = "net.peachpdf.brackets.media.example.MainActivity", Label = "PeachBrackets media", MainLauncher = true, Exported = true)]
public sealed class MainActivity : Activity
{
    private const string Tag = "PEACH_MEDIA";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _ = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        try
        {
            await CheckAsync("canPlayType", () =>
            {
                var provider = new AndroidMediaProvider();
                return Task.FromResult(
                    provider.CanPlayType("video/mp4; codecs=\"avc1.42E01E\"") == "probably" && provider.CanPlayType("text/html") == "" ? null : "canPlayType said " + provider.CanPlayType("video/mp4; codecs=\"avc1.42E01E\""));
            });
            await CheckAsync("deterministic metadata and frames", DeterministicAsync);
            await CheckAsync("deterministic playback follows the clock", ClockAsync);
            await CheckAsync("real-time playback plays and ends", RealTimeAsync);
            await CheckAsync("audio session", AudioAsync);
            await CheckAsync("zero-copy hardware frame", () => GpuCheck.RunAsync(name => Assets!.Open(name)));
        }
        catch (Exception ex)
        {
            Log.Info(Tag, "FAIL crashed: " + ex);
        }

        Log.Info(Tag, "DONE");
    }

    private static async Task CheckAsync(string name, Func<Task<string?>> check)
    {
        try
        {
            var problem = await check();
            Log.Info(Tag, problem is null ? "OK " + name : "FAIL " + name + ": " + problem);
        }
        catch (Exception ex)
        {
            Log.Info(Tag, "FAIL " + name + ": " + ex.GetType().Name + " " + ex.Message);
        }
    }

    private MediaRequest Request(string asset, MediaKind kind = MediaKind.Video) =>
        new(new Uri("https://media.test/" + asset), kind, new RasterRenderContext(), _ => new ValueTask<RNetworkResponse?>(new RNetworkResponse(Assets!.Open(asset), null)))
        {
            Muted = true,
        };

    private static AndroidMediaProvider Deterministic() => new(new MediaProviderOptions { DeterministicClock = true });

    private static byte[] Bytes(MediaFrame frame) => frame.Image.GetPixels()!.Value.PremultipliedRgba.ToArray();

    private async Task<string?> DeterministicAsync()
    {
        using var session = await Deterministic().OpenAsync(Request("moving.mp4"));
        if (session is null)
            return "did not open";

        if (!session.HasVideo || session.VideoWidth != 64 || session.VideoHeight != 48)
            return $"size {session.VideoWidth}x{session.VideoHeight}, video {session.HasVideo}";

        if (session.Duration is not { } length || length.TotalSeconds is < 2.8 or > 3.3)
            return "duration " + session.Duration;

        using var first = session.AcquireFrame();
        session.Seek(TimeSpan.FromSeconds(1.5));
        using var later = session.AcquireFrame();
        if (first is null || later is null)
            return "no frame";

        return Bytes(first).AsSpan().SequenceEqual(Bytes(later)) ? "frames at 0 and 1.5 s are equal" : null;
    }

    private async Task<string?> ClockAsync()
    {
        using var played = await Deterministic().OpenAsync(Request("moving.mp4"));
        using var sought = await Deterministic().OpenAsync(Request("moving.mp4"));
        if (played is null || sought is null)
            return "did not open";

        played.Update(TimeSpan.Zero);
        played.Play();
        for (var step = 1; step <= 10; step++)
            played.Update(TimeSpan.FromMilliseconds(step * 150));

        sought.Seek(TimeSpan.FromMilliseconds(1500));
        using var a = played.AcquireFrame();
        using var b = sought.AcquireFrame();
        if (a is null || b is null)
            return "no frame";

        return Bytes(a).AsSpan().SequenceEqual(Bytes(b)) ? null : "played and sought frames differ";
    }

    private async Task<string?> RealTimeAsync()
    {
        using var session = await new AndroidMediaProvider().OpenAsync(Request("moving.mp4"));
        if (session is null)
            return "did not open";

        session.Play();
        MediaFrame? frame = null;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!session.Ended && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
            frame?.Dispose();
            frame = session.AcquireFrame() ?? frame;
        }

        using var _ = frame;
        if (!session.Ended)
            return "did not end";

        return frame is null ? "no picture while playing" : frame.Image.Width == 64 ? null : "picture width " + frame.Image.Width;
    }

    private async Task<string?> AudioAsync()
    {
        using var session = await Deterministic().OpenAsync(Request("moving.mp4", MediaKind.Audio));
        if (session is null)
            return "did not open";

        return session.HasVideo || session.AcquireFrame() is not null ? "an audio session had a picture" : null;
    }
}
