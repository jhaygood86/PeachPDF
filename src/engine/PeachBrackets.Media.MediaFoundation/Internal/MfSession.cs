using PeachDrawing.Core;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PeachBrackets.Media.MediaFoundation.Internal;

/// <summary>
/// One resource, copied to a file in the temporary directory and read by Media Foundation source readers: one for the pictures and, while it plays with sound on, one
/// for the audio that <see cref="AudioOutput"/> renders. The session keeps the position itself. By the player's own clock it is the stopwatch while the session plays;
/// with <see cref="MediaProviderOptions.DeterministicClock"/> it is moved only by <see cref="Update"/>. Either way a picture is the one that shows at the position, so the
/// same position gives the same picture.
/// </summary>
internal sealed class MfSession : IMediaSession
{
    private readonly MediaKind _kind;
    private readonly bool _deterministic;
    private readonly string _file;
    private readonly VideoSource _source;
    private readonly object _gate = new();
    private readonly Stopwatch _clock = new();

    private AudioOutput? _audio;
    private bool _disposed;
    private TimeSpan _position;
    private TimeSpan? _lastDocumentTime;
    private bool _playing;
    private bool _ended;
    private double _rate = 1;
    private double _volume = 1;
    private bool _muted;
    private bool _loop;

    private PixelImage? _image;
    private TimeSpan _imageTime;
    private TimeSpan _imageEnd;

    private MfSession(MediaKind kind, bool deterministic, string file, VideoSource source, bool muted, bool loop)
    {
        _kind = kind;
        _deterministic = deterministic;
        _file = file;
        _source = source;
        _muted = muted;
        _loop = loop;
    }

    public MediaKind Kind => _kind;

    public MediaReadyState ReadyState => MediaReadyState.EnoughData;

    public TimeSpan? Duration => _source.Duration;

    public bool HasVideo => _kind == MediaKind.Video && _source.HasVideo;

    public int VideoWidth => _source.Width;

    public int VideoHeight => _source.Height;

    public TimeSpan Position
    {
        get
        {
            Tick();
            return _position;
        }
    }

    public bool IsPlaying
    {
        get
        {
            Tick();
            return _playing;
        }
    }

    public bool Ended
    {
        get
        {
            Tick();
            return _ended;
        }
    }

    public MediaErrorCode Error => MediaErrorCode.None;

    /// <summary>Whether the sound is being rendered (there is a track and an output device, and the session was played with the sound on).</summary>
    internal bool HasSound => _audio is not null;

    public double PlaybackRate
    {
        get => _rate;
        set
        {
            if (value <= 0 || value == _rate)
                return;

            Tick();
                _rate = value;
            _audio?.SetRate(value);
        }
    }

    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            _audio?.SetVolume(_muted ? 0 : _volume);
        }
    }

    public bool Muted
    {
        get => _muted;
        set
        {
            _muted = value;
            _audio?.SetVolume(value ? 0 : _volume);
        }
    }

    public bool Loop
    {
        get => _loop;
        set => _loop = value;
    }

    public event EventHandler<MediaChangedEventArgs>? Changed;

    public static async ValueTask<IMediaSession?> OpenAsync(MediaRequest request, MediaProviderOptions options, CancellationToken cancellationToken)
    {
        var response = await request.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (response?.ResourceStream is not { } source)
            return null;

        // Media Foundation picks its container handler by the file's extension, so the copy keeps the address's.
        var extension = Path.GetExtension(request.Address.AbsolutePath);
        var file = Path.Combine(Path.GetTempPath(), "peachbrackets-media-" + Guid.NewGuid().ToString("N") + extension);
        try
        {
            await using (source.ConfigureAwait(false))
            await using (var target = File.Create(file))
            {
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(file);
            return null;
        }

        var session = await Task.Run(() => Open(request, options, file), cancellationToken).ConfigureAwait(false);
        if (session is null)
            TryDelete(file);

        return session;
    }

    private static MfSession? Open(MediaRequest request, MediaProviderOptions options, string file)
    {
        var source = VideoSource.Open(file);
        if (source is null)
            return null;

        if (request.Kind == MediaKind.Video && !source.HasVideo && source.Duration is null)
        {
            source.Dispose();
            return null;
        }

        var session = new MfSession(request.Kind, options.DeterministicClock, file, source, request.Muted, request.Loop);
        if (session.HasVideo)
            session.TakePicture(TimeSpan.Zero);

        return session;
    }

    public void Play()
    {
        if (_disposed)
            return;

        Tick();
        if (_ended)
            MoveTo(TimeSpan.Zero);

        _playing = true;
        _ended = false;
        if (!_deterministic)
        {
            _clock.Restart();
            EnsureAudio()?.Start(_position);
        }

        Notify(MediaChange.State);
    }

    public void Pause()
    {
        if (_disposed)
            return;

        Tick();
        _playing = false;
        _clock.Reset();
        _audio?.Stop();
        Notify(MediaChange.State);
    }

    public void Seek(TimeSpan position)
    {
        if (_disposed)
            return;

        Tick();
        MoveTo(Clamp(position));
        _ended = false;
        if (_playing && !_deterministic)
        {
            _clock.Restart();
            _audio?.Start(_position);
        }

        Notify(MediaChange.Position | MediaChange.Frame);
    }

    public void Update(TimeSpan documentTime)
    {
        if (_disposed)
            return;

        if (!_deterministic)
        {
            Tick();
            return;
        }

        var last = _lastDocumentTime;
        _lastDocumentTime = documentTime;
        if (last is not { } before || !_playing || documentTime <= before)
            return;

        Advance((documentTime - before) * _rate);
    }

    public MediaFrame? AcquireFrame()
    {
        if (_disposed || !HasVideo)
            return null;

        Tick();
        lock (_gate)
        {
            if (_image is null || _position < _imageTime || _position >= _imageEnd)
                TakePicture(_position);

            return _image is null ? null : new MediaFrame(_image, _imageTime) { ColorSpace = _source.ColorSpace };
        }
    }

    public ValueTask<MediaFrame?> AcquireFrameAtAsync(TimeSpan position, CancellationToken cancellationToken = default)
    {
        if (_disposed || !HasVideo)
            return new ValueTask<MediaFrame?>((MediaFrame?)null);

        position = Clamp(position);
        return new ValueTask<MediaFrame?>(Task.Run(() =>
        {
            lock (_gate)
            {
                TakePicture(position);
                return _image is null ? null : (MediaFrame?)new MediaFrame(_image, _imageTime) { ColorSpace = _source.ColorSpace };
            }
        }, cancellationToken));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _playing = false;
        _audio?.Dispose();
        lock (_gate)
            _source.Dispose();

        TryDelete(_file);
    }

    // By the player's own clock the position follows the stopwatch; this brings it up to now. A session on the document's clock moves only in Update.
    private void Tick()
    {
        if (_deterministic || !_playing || _disposed)
            return;

        var elapsed = _clock.Elapsed;
        _clock.Restart();
        Advance(elapsed * _rate);
    }

    // Moves the position on by a span of media time, handling the end: a loop starts again, otherwise the session stops there and says so.
    private void Advance(TimeSpan span)
    {
        var next = _position + span;
        var change = MediaChange.Frame;
        if (_source.Duration is { } length && next >= length)
        {
            if (_loop && length > TimeSpan.Zero)
            {
                next = TimeSpan.FromTicks(next.Ticks % length.Ticks);
                change |= MediaChange.Position;
                if (!_deterministic)
                    _audio?.Start(next);
            }
            else
            {
                next = length;
                _playing = false;
                _ended = true;
                _clock.Reset();
                _audio?.Stop();
                change |= MediaChange.Ended | MediaChange.State;
            }
        }

        _position = next;
        Notify(change);
    }

    private void MoveTo(TimeSpan position) => _position = position;

    // The sound is made the first time it is wanted, and only when there is a device and a track to play.
    private AudioOutput? EnsureAudio()
    {
        if (_audio is null && !_disposed)
        {
            _audio = AudioOutput.TryCreate(_file, _rate, _muted ? 0 : _volume);
        }

        return _audio;
    }

    // Takes the picture that shows at a position, and remembers until when it does.
    private void TakePicture(TimeSpan at)
    {
        var picture = _source.FrameAt(at);
        if (picture is null)
            return;

        _image = new PixelImage(picture.Width, picture.Height, picture.Rgba);
        _imageTime = picture.Time;
        _imageEnd = _source.CurrentEnd ?? picture.Time + picture.Duration;
    }

    private TimeSpan Clamp(TimeSpan position) =>
        position < TimeSpan.Zero ? TimeSpan.Zero : _source.Duration is { } length && position > length ? length : position;

    private void Notify(MediaChange change) => Changed?.Invoke(this, new MediaChangedEventArgs(change));

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
