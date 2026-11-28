using AVFoundation;
using CoreFoundation;
using CoreGraphics;
using CoreMedia;
using CoreVideo;
using Foundation;
using PeachDrawing.Core;

namespace PeachBrackets.Media.Apple.Internal;

/// <summary>
/// One resource, copied to a file in the temporary directory. By the player's own clock an <see cref="AVPlayer"/> plays it (sound to the default output) and, for a
/// video, an <see cref="AVPlayerItemVideoOutput"/> hands over the picture that belongs to the time now. By the document's clock
/// (<see cref="MediaProviderOptions.DeterministicClock"/>) there is no player: the position is kept here and moved only by <see cref="Update"/>, and each picture is what
/// an <see cref="AVAssetImageGenerator"/> renders at exactly that position, so the same document shows the same pictures however fast the machine is.
/// </summary>
internal sealed class AppleMediaSession : IMediaSession
{
    private static readonly TimeSpan s_openTimeout = TimeSpan.FromSeconds(20);

    private readonly MediaKind _kind;
    private readonly bool _deterministic;
    private readonly string _file;
    private readonly AVUrlAsset _asset;
    private readonly object _gate = new();

    private AVPlayer? _player;
    private AVPlayerItem? _item;
    private AVPlayerItemVideoOutput? _output;
    private AVAssetImageGenerator? _generator;
    private NSObject? _endObserver;
    private bool _disposed;
    private TimeSpan? _duration;
    private int _width;
    private int _height;
    private bool _hasVideo;
    private TimeSpan _frameDuration = TimeSpan.FromMilliseconds(40);

    private TimeSpan _position;
    private TimeSpan? _lastDocumentTime;
    private bool _playing;
    private bool _ended;
    private double _rate = 1;
    private double _volume = 1;
    private bool _muted;
    private bool _loop;

    private PixelImage? _image;
    private TimeSpan _imageStart;
    private TimeSpan _imageEnd;

    private AppleMediaSession(MediaKind kind, bool deterministic, string file, AVUrlAsset asset, bool muted, bool loop)
    {
        _kind = kind;
        _deterministic = deterministic;
        _file = file;
        _asset = asset;
        _muted = muted;
        _loop = loop;
    }

    public MediaKind Kind => _kind;

    public MediaReadyState ReadyState => Error != MediaErrorCode.None ? MediaReadyState.Nothing : MediaReadyState.EnoughData;

    public TimeSpan? Duration => _duration;

    public bool HasVideo => _kind == MediaKind.Video && _hasVideo;

    public int VideoWidth => _width;

    public int VideoHeight => _height;

    public TimeSpan Position
    {
        get
        {
            if (_deterministic || _disposed || _player is not { } player)
                return _position;

            var seconds = player.CurrentTime.Seconds;
            if (!double.IsNaN(seconds) && !double.IsInfinity(seconds))
                _position = TimeSpan.FromSeconds(Math.Max(0, seconds));

            return _position;
        }
    }

    public bool IsPlaying => _playing;

    public bool Ended => _ended;

    public MediaErrorCode Error => _item?.Status == AVPlayerItemStatus.Failed ? MediaErrorCode.Decode : MediaErrorCode.None;

    public double PlaybackRate
    {
        get => _rate;
        set
        {
            if (value <= 0 || value == _rate)
                return;

            _rate = value;
            if (_player is { } player && !_disposed && _playing)
                player.Rate = (float)value;
        }
    }

    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            if (_player is { } player && !_disposed)
                player.Volume = (float)_volume;
        }
    }

    public bool Muted
    {
        get => _muted;
        set
        {
            _muted = value;
            if (_player is { } player && !_disposed)
                player.Muted = value;
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

        // AVFoundation chooses a container by the file's extension when it has no other word, so the copy keeps the address's.
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

    private static AppleMediaSession? Open(MediaRequest request, MediaProviderOptions options, string file)
    {
        var asset = AVUrlAsset.Create(NSUrl.FromFilename(file));
        if (asset is null || !asset.Playable)
        {
            asset?.Dispose();
            return null;
        }

        var session = new AppleMediaSession(request.Kind, options.DeterministicClock, file, asset, request.Muted, request.Loop);
        session.Learn();
        if (session._deterministic)
        {
            if (session._hasVideo && request.Kind == MediaKind.Video)
                session.PrepareGenerator();
        }
        else if (!session.StartPlayer())
        {
            session.Dispose();
            return null;
        }

        if (session._deterministic && session._generator is not null)
            session.TakePicture(TimeSpan.Zero);

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
        if (_player is { } player)
        {
            player.Play();
            player.Rate = (float)_rate;
        }

        Notify(MediaChange.State);
    }

    public void Pause()
    {
        if (_disposed)
            return;

        _playing = false;
        _player?.Pause();
        Notify(MediaChange.State);
    }

    public void Seek(TimeSpan position)
    {
        if (_disposed)
            return;

        position = Clamp(position);
        _position = position;
        _ended = false;
        _player?.Seek(CMTime.FromSeconds(position.TotalSeconds, 600), CMTime.Zero, CMTime.Zero);
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
            if (_loop && length > TimeSpan.Zero)
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
        if (_disposed || _kind != MediaKind.Video || !_hasVideo)
            return null;

        lock (_gate)
        {
            if (_deterministic)
            {
                if (_image is null || _position < _imageStart || _position >= _imageEnd)
                    TakePicture(_position);
            }
            else
            {
                TakeNewestPicture();
            }

            return _image is null ? null : new MediaFrame(_image, _imageStart) { ColorSpace = MediaColorSpace.Srgb };
        }
    }

    public ValueTask<MediaFrame?> AcquireFrameAtAsync(TimeSpan position, CancellationToken cancellationToken = default)
    {
        if (_disposed || _kind != MediaKind.Video || !_hasVideo)
            return new ValueTask<MediaFrame?>((MediaFrame?)null);

        position = Clamp(position);
        return new ValueTask<MediaFrame?>(Task.Run(() =>
        {
            lock (_gate)
            {
                PrepareGenerator();
                TakePicture(position);
                return _image is null ? null : (MediaFrame?)new MediaFrame(_image, _imageStart) { ColorSpace = MediaColorSpace.Srgb };
            }
        }, cancellationToken));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _playing = false;
        _endObserver?.Dispose();
        _player?.Pause();
        if (_output is not null)
            _item?.RemoveOutput(_output);

        _output?.Dispose();
        _player?.Dispose();
        _item?.Dispose();
        _generator?.Dispose();
        _asset.Dispose();
        TryDelete(_file);
    }

    private void Learn()
    {
        var length = _asset.Duration;
        if (!length.IsInvalid && !length.IsIndefinite && length.Seconds > 0)
            _duration = TimeSpan.FromSeconds(length.Seconds);

        var tracks = _asset.TracksWithMediaType(AVMediaTypes.Video.GetConstant()!);
        if (tracks is { Length: > 0 })
        {
            var track = tracks[0];
            _hasVideo = true;

            // The size is of the picture as it is stored; a quarter turn in the track's transform makes the shown one the other way round.
            var size = track.NaturalSize;
            var turned = Math.Abs(track.PreferredTransform.C) > 0.5;
            _width = (int)Math.Round(turned ? size.Height : size.Width);
            _height = (int)Math.Round(turned ? size.Width : size.Height);
            if (track.NominalFrameRate > 0)
                _frameDuration = TimeSpan.FromSeconds(1.0 / track.NominalFrameRate);
        }
    }

    // The generator that renders the picture at any position: exactly there (no tolerance) and turned the way the track says.
    private void PrepareGenerator()
    {
        if (_generator is not null)
            return;

        _generator = new AVAssetImageGenerator(_asset)
        {
            AppliesPreferredTrackTransform = true,
            RequestedTimeToleranceBefore = CMTime.Zero,
            RequestedTimeToleranceAfter = CMTime.Zero,
        };
    }

    // The player of a session that plays by its own clock, ready and paused at the start.
    private bool StartPlayer()
    {
        _item = AVPlayerItem.FromAsset(_asset);
        if (_kind == MediaKind.Video && _hasVideo)
        {
            _output = new AVPlayerItemVideoOutput(new CVPixelBufferAttributes { PixelFormatType = CVPixelFormatType.CV32BGRA });
            _item.AddOutput(_output);
        }

        _player = new AVPlayer(_item)
        {
            ActionAtItemEnd = AVPlayerActionAtItemEnd.None,
            Muted = _muted,
            Volume = (float)_volume,
        };
        _endObserver = NSNotificationCenter.DefaultCenter.AddObserver(AVPlayerItem.DidPlayToEndTimeNotification, OnEnded, _item);

        var deadline = DateTime.UtcNow + s_openTimeout;
        // AVFoundation reports the status through the run loop of the thread that created the item, so the wait runs that loop, on whatever thread this is.
        while (_item.Status == AVPlayerItemStatus.Unknown && DateTime.UtcNow < deadline)
            NSRunLoop.Current.RunUntil(NSDate.Now.AddSeconds(0.02));

        return _item.Status == AVPlayerItemStatus.ReadyToPlay;
    }

    // Takes the picture at a position from the generator: the one that shows there, and until the next one starts.
    private void TakePicture(TimeSpan at)
    {
        if (_generator is null)
            return;

        using var cgImage = _generator.CopyCGImageAtTime(CMTime.FromSeconds(at.TotalSeconds, 600), out _, out var error);
        if (cgImage is null || error is not null)
            return;

        _image = FromCgImage(cgImage);
        _imageStart = at;
        _imageEnd = at + _frameDuration;
    }

    // Draws an image into premultiplied RGBA of its own size, in sRGB: the system converts from the image's color space as it draws.
    private static PixelImage FromCgImage(CGImage image)
    {
        var width = (int)image.Width;
        var height = (int)image.Height;
        var pixels = new byte[width * height * 4];
        using var space = CGColorSpace.CreateSrgb()!;
        using var context = new CGBitmapContext(pixels, width, height, 8, width * 4, space, CGImageAlphaInfo.PremultipliedLast);
        context.DrawImage(new CGRect(0, 0, width, height), image);
        return new PixelImage(width, height, pixels);
    }

    // Takes the newest picture the output has for the time now, if it has a newer one than the one held.
    private void TakeNewestPicture()
    {
        if (_output is null)
            return;

        var itemTime = _output.GetItemTime(CoreAnimation.CAAnimation.CurrentMediaTime());
        if (!_output.HasNewPixelBufferForItemTime(itemTime))
            return;

        CMTime presented = default;
        using var buffer = _output.CopyPixelBuffer(itemTime, ref presented);
        if (buffer is null)
            return;

        buffer.Lock(CVPixelBufferLock.ReadOnly);
        try
        {
            var width = (int)buffer.Width;
            var height = (int)buffer.Height;
            var stride = (int)buffer.BytesPerRow;
            var source = buffer.BaseAddress;
            var pixels = new byte[width * height * 4];
            var row = new byte[stride];
            for (var y = 0; y < height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(source + (y * stride), row, 0, stride);

                // BGRA, opaque, to RGBA.
                for (var x = 0; x < width; x++)
                {
                    var i = x * 4;
                    var o = ((y * width) + x) * 4;
                    pixels[o] = row[i + 2];
                    pixels[o + 1] = row[i + 1];
                    pixels[o + 2] = row[i];
                    pixels[o + 3] = 255;
                }
            }

            _image = new PixelImage(width, height, pixels);
            _imageStart = TimeSpan.FromSeconds(Math.Max(0, presented.Seconds));
        }
        finally
        {
            buffer.Unlock(CVPixelBufferLock.ReadOnly);
        }
    }

    private void OnEnded(NSNotification notification)
    {
        if (_disposed)
            return;

        if (_loop && _player is { } player)
        {
            player.Seek(CMTime.Zero);
            player.Play();
            player.Rate = (float)_rate;
            Notify(MediaChange.Position);
            return;
        }

        _playing = false;
        _ended = true;
        if (_duration is { } length)
            _position = length;

        Notify(MediaChange.Ended | MediaChange.State);
    }

    private TimeSpan Clamp(TimeSpan position) =>
        position < TimeSpan.Zero ? TimeSpan.Zero : _duration is { } length && position > length ? length : position;

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
