using Microsoft.Graphics.Canvas;
using PeachDrawing.Win2D;
using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace PeachBrackets.Media.Windows.Internal;

/// <summary>
/// One <see cref="MediaPlayer"/> over a copy of the resource in memory. For a video the player is a frame server: each picture it renders is copied into a
/// bitmap on the document's Win2D device, and the newest is what <see cref="AcquireFrame"/> leases. By the player's own clock it plays and the sound goes to the
/// default output; with <see cref="MediaProviderOptions.DeterministicClock"/> it stays paused, the position is kept here and moved only by <see cref="Update"/>,
/// and each picture is found by seeking the player there and waiting for the picture it serves.
/// </summary>
internal sealed class WindowsMediaSession : IMediaSession
{
    private static readonly TimeSpan s_openTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan s_seekTimeout = TimeSpan.FromSeconds(5);

    private readonly MediaKind _kind;
    private readonly bool _deterministic;
    private readonly MediaPlayer _player;
    private readonly Win2dRenderContext? _context;

    // The device the pictures are made on: the document's own when it is drawn with Win2D (so a canvas draws them with no read back), else a shared one of ours,
    // and each picture is then read back into pixels any canvas can draw.
    private readonly CanvasDevice _device;
    private readonly object _gate = new();

    // The seek SeekAndWait is waiting on: where to, and what to signal when the picture served after it has been copied.
    private (TimeSpan At, TaskCompletionSource Arrived)? _wanted;

    private bool _disposed;
    private TimeSpan? _duration;
    private int _width;
    private int _height;
    private TimeSpan _frameDuration = TimeSpan.FromMilliseconds(33);

    private TimeSpan _position;
    private TimeSpan? _lastDocumentTime;
    private bool _playing;
    private bool _ended;
    private double _rate = 1;
    private MediaErrorCode _error;

    private FrameTarget? _latest;
    private TimeSpan _latestStart;
    private TimeSpan _latestEnd;

    private WindowsMediaSession(MediaRequest request, MediaProviderOptions options, MediaPlayer player)
    {
        _kind = request.Kind;
        _deterministic = options.DeterministicClock;
        _player = player;
        _context = request.RenderContext as Win2dRenderContext;
        _device = _context?.Device ?? CanvasDevice.GetSharedDevice();
    }

    public MediaKind Kind => _kind;

    public MediaReadyState ReadyState => _error != MediaErrorCode.None ? MediaReadyState.Nothing : MediaReadyState.EnoughData;

    public TimeSpan? Duration => _duration;

    public bool HasVideo => _kind == MediaKind.Video && _width > 0;

    public int VideoWidth => _width;

    public int VideoHeight => _height;

    public TimeSpan Position
    {
        get
        {
            if (_deterministic || _disposed)
                return _position;

            _position = _player.PlaybackSession.Position;
            return _position;
        }
    }

    public bool IsPlaying => _playing;

    public bool Ended => _ended;

    public MediaErrorCode Error => _error;

    public double PlaybackRate
    {
        get => _rate;
        set
        {
            if (value <= 0 || value == _rate)
                return;

            _rate = value;
            if (!_deterministic && !_disposed)
                _player.PlaybackSession.PlaybackRate = value;
        }
    }

    public double Volume
    {
        get => _player.Volume;
        set
        {
            if (!_disposed)
                _player.Volume = Math.Clamp(value, 0, 1);
        }
    }

    public bool Muted
    {
        get => _player.IsMuted;
        set
        {
            if (!_disposed)
                _player.IsMuted = value;
        }
    }

    public bool Loop
    {
        get => _player.IsLoopingEnabled;
        set
        {
            if (!_disposed)
                _player.IsLoopingEnabled = value;
        }
    }

    public event EventHandler<MediaChangedEventArgs>? Changed;

    public static async Task<IMediaSession?> OpenAsync(MediaRequest request, MediaProviderOptions options, CancellationToken cancellationToken)
    {
        var response = await request.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (response?.ResourceStream is not { } source)
            return null;

        byte[] bytes;
        await using (source.ConfigureAwait(false))
        {
            using var copy = new MemoryStream();
            await source.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);
            bytes = copy.ToArray();
        }

        var contentType = request.ContentType ?? FirstHeader(response, "Content-Type") ?? GuessContentType(request.Address);
        var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(bytes.AsBuffer());
        stream.Seek(0);

        var video = request.Kind == MediaKind.Video;
        var deterministic = options.DeterministicClock;
        var player = new MediaPlayer
        {
            AutoPlay = false,
            IsVideoFrameServerEnabled = video,
            IsMuted = request.Muted || deterministic,
            IsLoopingEnabled = request.Loop,
        };

        var session = new WindowsMediaSession(request, options, player);
        var opened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        player.MediaOpened += (_, _) => opened.TrySetResult(true);
        player.MediaFailed += (_, _) => opened.TrySetResult(false);
        player.VideoFrameAvailable += session.OnFrameAvailable;
        player.MediaEnded += session.OnEnded;
        player.MediaFailed += session.OnFailed;

        player.Source = MediaSource.CreateFromStream(stream, contentType);
        var completed = await Task.WhenAny(opened.Task, Task.Delay(s_openTimeout, cancellationToken)).ConfigureAwait(false);
        if (completed != opened.Task || !await opened.Task.ConfigureAwait(false))
        {
            session.Dispose();
            stream.Dispose();
            return null;
        }

        session.Learn();
        return session;
    }

    public void Play()
    {
        if (_disposed)
            return;

        if (_ended)
            Seek(TimeSpan.Zero);

        _playing = true;
        _ended = false;
        if (!_deterministic)
            _player.Play();

        Notify(MediaChange.State);
    }

    public void Pause()
    {
        if (_disposed)
            return;

        _playing = false;
        if (!_deterministic)
            _player.Pause();

        Notify(MediaChange.State);
    }

    public void Seek(TimeSpan position)
    {
        if (_disposed)
            return;

        position = Clamp(position);
        _position = position;
        _ended = false;
        if (!_deterministic)
            _player.PlaybackSession.Position = position;

        Notify(MediaChange.Position | MediaChange.Frame);
    }

    public void Update(TimeSpan documentTime)
    {
        if (_disposed || !_deterministic)
            return;

        var last = _lastDocumentTime;
        _lastDocumentTime = documentTime;
        if (last is not { } before || !_playing || documentTime <= before)
            return;

        var next = _position + ((documentTime - before) * _rate);
        var change = MediaChange.Frame;
        if (_duration is { } length && next >= length)
        {
            if (Loop && length > TimeSpan.Zero)
            {
                next = TimeSpan.FromTicks(next.Ticks % length.Ticks);
                change |= MediaChange.Position;
            }
            else
            {
                next = length;
                _playing = false;
                _ended = true;
                change |= MediaChange.Ended | MediaChange.State;
            }
        }

        _position = next;
        Notify(change);
    }

    public MediaFrame? AcquireFrame()
    {
        if (_disposed || _kind != MediaKind.Video)
            return null;

        if (_deterministic)
        {
            var at = _position;
            bool current;
            lock (_gate)
                current = _latest is not null && at >= _latestStart && at < _latestEnd;

            if (!current)
                SeekAndWait(at);
        }

        return Lease();
    }

    public ValueTask<MediaFrame?> AcquireFrameAtAsync(TimeSpan position, CancellationToken cancellationToken = default)
    {
        if (_disposed || _kind != MediaKind.Video)
            return new ValueTask<MediaFrame?>((MediaFrame?)null);

        position = Clamp(position);
        return new ValueTask<MediaFrame?>(Task.Run(() =>
        {
            SeekAndWait(position);
            return Lease();
        }, cancellationToken));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _playing = false;
        _player.VideoFrameAvailable -= OnFrameAvailable;
        _player.MediaEnded -= OnEnded;
        _player.MediaFailed -= OnFailed;
        _player.Pause();
        var source = _player.Source as IDisposable;
        _player.Source = null;
        source?.Dispose();
        _player.Dispose();
        lock (_gate)
        {
            _latest?.Retire();
            _latest = null;
        }
    }

    private void Learn()
    {
        var session = _player.PlaybackSession;
        var length = session.NaturalDuration;
        if (length > TimeSpan.Zero && length < TimeSpan.MaxValue)
            _duration = length;

        _width = (int)session.NaturalVideoWidth;
        _height = (int)session.NaturalVideoHeight;
        if (_kind == MediaKind.Video && _width > 0)
        {
            // The paused player serves one picture when it opens, and it is an empty one: the first real picture comes with a seek. Go some way in (to a different
            // picture, or the player serves nothing) and come back, so that the first frame is there, and is the first frame, when the session is handed over.
            SeekAndWait(_duration is { } whole ? TimeSpan.FromTicks(Math.Min(whole.Ticks / 2, TimeSpan.FromMilliseconds(500).Ticks)) : TimeSpan.FromMilliseconds(500));
            SeekAndWait(TimeSpan.Zero);
        }
    }

    // Moves the player to a position and waits for the picture it serves there. The player's frame is only valid inside its frame event, so the copy is made
    // there (see CopyFrame); this waits for the first event after the move, or for the first event ever when the player has not served one yet.
    private void SeekAndWait(TimeSpan at)
    {
        var session = _player.PlaybackSession;
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool moved;
        lock (_gate)
        {
            moved = session.Position != at;
            if (moved || _latest is null)
                _wanted = (at, arrived);
        }

        if (moved)
            session.Position = at;

        if (moved || _latest is null)
            arrived.Task.Wait(s_seekTimeout);

        lock (_gate)
        {
            _wanted = null;
            if (_latest is not null)
            {
                _latestStart = at;
                _latestEnd = at + _frameDuration;
            }
        }
    }

    private void OnFrameAvailable(MediaPlayer sender, object args)
    {
        // Each picture is copied as it is served: the player's frame is valid only now. By the player's own clock it is the newest and is announced; by the
        // document's it is the one a seek asked for, which SeekAndWait is waiting on.
        CopyFrame(sender, _deterministic ? null : sender.PlaybackSession.Position);
    }

    private void CopyFrame(MediaPlayer sender, TimeSpan? start)
    {
        if (_disposed)
            return;

        var width = _width > 0 ? _width : (int)sender.PlaybackSession.NaturalVideoWidth;
        var height = _height > 0 ? _height : (int)sender.PlaybackSession.NaturalVideoHeight;
        if (width < 1 || height < 1)
            return;

        try
        {
            var target = new CanvasRenderTarget(_device, width, height, 96);
            sender.CopyFrameToVideoSurface(target);
            var frame = new FrameTarget(target);
            TaskCompletionSource? waiter = null;
            lock (_gate)
            {
                _latest?.Retire();
                _latest = frame;
                if (start is { } known)
                {
                    _latestStart = known;
                    _latestEnd = known + _frameDuration;
                }

                if (_wanted is { } wanted)
                {
                    waiter = wanted.Arrived;
                    _wanted = null;
                }
            }

            waiter?.TrySetResult();
            if (start is not null)
                Notify(MediaChange.Frame);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ObjectDisposedException or InvalidOperationException)
        {
            // The device was lost or the player is closing; the next frame tries again.
        }
    }

    private MediaFrame? Lease()
    {
        FrameTarget? frame;
        TimeSpan start;
        lock (_gate)
        {
            frame = _latest;
            start = _latestStart;
            frame?.Lease();
        }

        if (frame is null)
            return null;

        var image = _context is not null ? _context.WrapBitmap(frame.Target) : ReadBack(frame.Target);
        return new MediaFrame(image, start, () =>
        {
            image.Dispose();
            lock (_gate)
                frame.Release();
        });
    }

    // A picture for a canvas of another backend: the bitmap's pixels (premultiplied BGRA) as premultiplied RGBA.
    private static PeachDrawing.Core.Image ReadBack(CanvasRenderTarget target)
    {
        var bgra = target.GetPixelBytes();
        var rgba = new byte[bgra.Length];
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            rgba[i] = bgra[i + 2];
            rgba[i + 1] = bgra[i + 1];
            rgba[i + 2] = bgra[i];
            rgba[i + 3] = bgra[i + 3];
        }

        return new PixelImage((int)target.SizeInPixels.Width, (int)target.SizeInPixels.Height, rgba);
    }

    private void OnEnded(MediaPlayer sender, object args)
    {
        if (_deterministic || _disposed)
            return;

        _playing = false;
        _ended = true;
        if (_duration is { } length)
            _position = length;

        Notify(MediaChange.Ended | MediaChange.State);
    }

    private void OnFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        if (_error != MediaErrorCode.None)
            return;

        _error = args.Error == MediaPlayerError.NetworkError ? MediaErrorCode.Network : args.Error == MediaPlayerError.SourceNotSupported ? MediaErrorCode.SourceNotSupported : MediaErrorCode.Decode;
        _playing = false;
        Notify(MediaChange.Error | MediaChange.State);
    }

    private TimeSpan Clamp(TimeSpan position) =>
        position < TimeSpan.Zero ? TimeSpan.Zero : _duration is { } length && position > length ? length : position;

    private void Notify(MediaChange change) => Changed?.Invoke(this, new MediaChangedEventArgs(change));

    private static string? FirstHeader(PeachDrawing.Core.RNetworkResponse response, string name)
    {
        if (response.ResponseHeaders is null)
            return null;

        foreach (var (key, values) in response.ResponseHeaders)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase) && values.Length > 0)
                return values[0];
        }

        return null;
    }

    private static string GuessContentType(Uri address) =>
        Path.GetExtension(address.AbsolutePath).ToLowerInvariant() switch
        {
            ".mp4" or ".m4v" => "video/mp4",
            ".m4a" => "audio/mp4",
            ".webm" => "video/webm",
            ".ogv" or ".ogg" => "video/ogg",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".flac" => "audio/flac",
            ".aac" => "audio/aac",
            _ => "video/mp4",
        };

    /// <summary>A served picture: the bitmap, leased to whoever acquired it, and disposed once it is no longer the newest and nobody holds it.</summary>
    private sealed class FrameTarget(CanvasRenderTarget target)
    {
        private int _leases;
        private bool _retired;

        public CanvasRenderTarget Target { get; } = target;

        public void Lease() => _leases++;

        public void Release()
        {
            _leases--;
            DisposeIfFree();
        }

        public void Retire()
        {
            _retired = true;
            DisposeIfFree();
        }

        private void DisposeIfFree()
        {
            if (_retired && _leases == 0)
                Target.Dispose();
        }
    }
}
