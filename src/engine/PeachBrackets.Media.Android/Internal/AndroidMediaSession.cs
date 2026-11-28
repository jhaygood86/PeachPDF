using Android.Graphics;
using Android.Media;
using Android.OS;
using PeachDrawing.Core;
using Java.Nio;
using PeachDrawing.Skia;

namespace PeachBrackets.Media.Android.Internal;

/// <summary>
/// One resource, copied to a file in the app's cache. By the player's own clock a <see cref="MediaPlayer"/> plays it (sound to the default output) and, for a
/// video, renders into an <see cref="ImageReader"/> whose newest image is the picture. By the document's clock (<see cref="MediaProviderOptions.DeterministicClock"/>)
/// there is no player: the position is kept here and moved only by <see cref="Update"/>, and each picture is what <see cref="MediaMetadataRetriever"/> returns for
/// that position, so the same document shows the same pictures however fast the device is.
/// </summary>
internal sealed class AndroidMediaSession : IMediaSession
{
    private readonly MediaKind _kind;
    private readonly bool _deterministic;
    private readonly string _file;
    private readonly MediaMetadataRetriever _retriever;
    private readonly object _gate = new();

    private MediaPlayer? _player;
    private ImageReader? _reader;
    private bool _disposed;
    private TimeSpan? _duration;
    private int _width;
    private int _height;
    private int _rotation;
    private bool _hasVideo;
    // AHARDWAREBUFFER_USAGE_GPU_SAMPLED_IMAGE and CPU_READ_OFTEN: the buffers are sampled by the GPU and can also be read by the CPU.
    private const long GpuSampledImage = 0x100;
    private const long CpuReadOften = 0x3;

    private TimeSpan _frameDuration = TimeSpan.FromMilliseconds(40);

    private TimeSpan _position;
    private TimeSpan? _lastDocumentTime;
    private bool _playing;
    private bool _ended;
    private double _rate = 1;
    private double _volume = 1;
    private bool _muted;
    private bool _loop;
    private MediaErrorCode _error;

    private SkiaRenderContext? _gpuContext;
    private GpuPicture? _picture;
    private PixelImage? _image;
    private TimeSpan _imageStart;
    private TimeSpan _imageEnd;

    private AndroidMediaSession(MediaKind kind, bool deterministic, string file, MediaMetadataRetriever retriever, bool muted, bool loop, SkiaRenderContext? gpuContext)
    {
        _gpuContext = gpuContext;
        _kind = kind;
        _deterministic = deterministic;
        _file = file;
        _retriever = retriever;
        _muted = muted;
        _loop = loop;
    }

    public MediaKind Kind => _kind;

    public MediaReadyState ReadyState => _error != MediaErrorCode.None ? MediaReadyState.Nothing : MediaReadyState.EnoughData;

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

            try
            {
                _position = TimeSpan.FromMilliseconds(player.CurrentPosition);
            }
            catch (Java.Lang.IllegalStateException)
            {
            }

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
            if (_player is { } player && !_disposed)
                ApplyRate(player);
        }
    }

    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            ApplyVolume();
        }
    }

    public bool Muted
    {
        get => _muted;
        set
        {
            _muted = value;
            ApplyVolume();
        }
    }

    public bool Loop
    {
        get => _loop;
        set
        {
            _loop = value;
            if (_player is { } player && !_disposed)
                player.Looping = value;
        }
    }

    public event EventHandler<MediaChangedEventArgs>? Changed;

    public static async ValueTask<IMediaSession?> OpenAsync(MediaRequest request, MediaProviderOptions options, CancellationToken cancellationToken)
    {
        var response = await request.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (response?.ResourceStream is not { } source)
            return null;

        var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "peachbrackets-media-" + Guid.NewGuid().ToString("N"));
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

        // The platform's media classes block while they open and prepare, so that is done off the caller's thread.
        var session = await Task.Run(() => Open(request, options, file), cancellationToken).ConfigureAwait(false);
        if (session is null)
            TryDelete(file);

        return session;
    }

    private static AndroidMediaSession? Open(MediaRequest request, MediaProviderOptions options, string file)
    {
        var retriever = new MediaMetadataRetriever();
        try
        {
            retriever.SetDataSource(file);
        }
        catch (Exception ex) when (ex is Java.Lang.RuntimeException or Java.IO.IOException)
        {
            retriever.Dispose();
            return null;
        }

        var session = new AndroidMediaSession(request.Kind, options.DeterministicClock, file, retriever, request.Muted, request.Loop,
            request.Kind == MediaKind.Video && !options.DeterministicClock && request.RenderContext is SkiaRenderContext skia && HardwareBufferImporter.IsSupported() ? skia : null);
        if (!session.Learn() || (request.Kind == MediaKind.Video && !session._hasVideo && !session.HasAudioTrack()))
        {
            session.Dispose();
            return null;
        }

        // A stream stored rotated is turned on the CPU, so it takes the copying path.
        if (session._rotation != 0)
            session._gpuContext = null;

        if (!session._deterministic && !session.StartPlayer())
        {
            session.Dispose();
            return null;
        }

        if (session._deterministic && session._hasVideo && request.Kind == MediaKind.Video)
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
            player.Start();
            ApplyRate(player);
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
        if (_player is { } player)
        {
            // Before Android 8 a seek lands on the nearest keyframe before the position, which is as close as the platform goes.
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
                player.SeekTo((long)position.TotalMilliseconds, MediaPlayerSeekMode.Closest);
            else
                player.SeekTo((int)position.TotalMilliseconds);
        }

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

            if (_picture is not null)
                return new MediaFrame(_picture.Image, _imageStart, _picture.Lease()) { ColorSpace = MediaColorSpace.Srgb };

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
        if (_player is { } player)
        {
            player.Completion -= OnCompleted;
            player.Error -= OnFailed;
            player.Release();
            player.Dispose();
        }

        _picture?.Release();
        _picture = null;
        _reader?.Close();
        _reader?.Dispose();
        _retriever.Release();
        _retriever.Dispose();
        TryDelete(_file);
    }

    private bool Learn()
    {
        try
        {
            if (long.TryParse(_retriever.ExtractMetadata(MetadataKey.Duration), out var milliseconds) && milliseconds > 0)
                _duration = TimeSpan.FromMilliseconds(milliseconds);

            _hasVideo = string.Equals(_retriever.ExtractMetadata(MetadataKey.HasVideo), "yes", StringComparison.OrdinalIgnoreCase);
            if (_hasVideo)
            {
                int.TryParse(_retriever.ExtractMetadata(MetadataKey.VideoWidth), out _width);
                int.TryParse(_retriever.ExtractMetadata(MetadataKey.VideoHeight), out _height);
                int.TryParse(_retriever.ExtractMetadata(MetadataKey.VideoRotation), out _rotation);
                _rotation = ((_rotation % 360) + 360) % 360;
                if (_rotation is 90 or 270)
                    (_width, _height) = (_height, _width);

                if (double.TryParse(_retriever.ExtractMetadata(MetadataKey.CaptureFramerate), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var rate) && rate > 0)
                    _frameDuration = TimeSpan.FromSeconds(1 / rate);
            }

            return _duration is not null || _hasVideo || HasAudioTrack();
        }
        catch (Java.Lang.RuntimeException)
        {
            return false;
        }
    }

    private bool HasAudioTrack() => string.Equals(_retriever.ExtractMetadata(MetadataKey.HasAudio), "yes", StringComparison.OrdinalIgnoreCase);

    // The player of a session that plays by its own clock, prepared and paused at the start.
    private bool StartPlayer()
    {
        try
        {
            var player = new MediaPlayer();
            player.SetDataSource(_file);
            player.Looping = _loop;
            if (_kind == MediaKind.Video && _hasVideo && _width > 0 && _height > 0)
            {
                // The surface the player renders into is an image reader; a picture is its newest image. A decoder's surface takes YUV (an RGBA reader is refused),
                // which is converted here, and frames arrive as the video is stored, unrotated.
                var (storedWidth, storedHeight) = _rotation is 90 or 270 ? (_height, _width) : (_width, _height);
                // On a GPU host the pictures stay in the decoder's buffers and are sampled as textures, the GPU doing the YUV conversion; the same buffers
                // can be read on the CPU (for a canvas with no GPU, or a readback), where they are converted here.
                _reader = _gpuContext is not null && OperatingSystem.IsAndroidVersionAtLeast(29)
                    ? ImageReader.NewInstance(storedWidth, storedHeight, ImageFormatType.Yuv420888, 6, GpuSampledImage | CpuReadOften)
                    : ImageReader.NewInstance(storedWidth, storedHeight, ImageFormatType.Yuv420888, 3);
                player.SetSurface(_reader.Surface);
            }

            player.Prepare();
            player.Completion += OnCompleted;
            player.Error += OnFailed;
            _player = player;
            ApplyVolume();
            return true;
        }
        catch (Exception ex) when (ex is Java.Lang.RuntimeException or Java.IO.IOException)
        {
            return false;
        }
    }

    // Takes the picture at a position from the retriever: the one that shows there, and until the next one starts.
    private void TakePicture(TimeSpan at)
    {
        using var bitmap = _retriever.GetFrameAtTime((long)at.TotalMicroseconds, Option.Closest);
        if (bitmap is null)
            return;

        _image = FromBitmap(bitmap);
        _imageStart = at;
        _imageEnd = at + _frameDuration;
    }

    internal static PixelImage? FromBitmap(Bitmap bitmap)
    {
        // A hardware or 565 bitmap cannot be copied out as RGBA bytes; an ARGB_8888 copy always can.
        using var copy = bitmap.Copy(Bitmap.Config.Argb8888!, false);
        if (copy is null)
            return null;

        var width = copy.Width;
        var height = copy.Height;
        var buffer = ByteBuffer.Allocate(copy.ByteCount)!;
        copy.CopyPixelsToBuffer(buffer);
        buffer.Rewind();
        var raw = new byte[copy.ByteCount];
        buffer.Get(raw);

        // The copy holds premultiplied R, G, B, A bytes; the stride is what the bytes say, not what RowBytes claims.
        var rowBytes = width * 4;
        var stride = height > 0 ? raw.Length / height : rowBytes;
        if (stride < rowBytes)
            return null;

        var pixels = new byte[rowBytes * height];
        for (var y = 0; y < height; y++)
            System.Buffer.BlockCopy(raw, y * stride, pixels, y * rowBytes, rowBytes);

        return new PixelImage(width, height, pixels);
    }

    // Takes the newest picture the player rendered, if there is a newer one than the one held.
    private void TakeNewestPicture()
    {
        if (_reader is null)
            return;

        global::Android.Media.Image? image;
        try
        {
            image = _reader.AcquireLatestImage();
        }
        catch (Java.Lang.IllegalStateException)
        {
            // Every image the reader may hand out is still held by a frame; the picture shown stays.
            return;
        }

        if (image is null)
            return;

        if (_gpuContext is not null && OperatingSystem.IsAndroidVersionAtLeast(29) && image.HardwareBuffer is { } buffer)
        {
            var previous = _picture;
            _picture = new GpuPicture(_gpuContext, image, buffer);
            _imageStart = Position;
            previous?.Release();
            return;
        }

        byte[] pixels;
        int width, height;
        try
        {
            var planes = image.GetPlanes()!;
            width = image.Width;
            height = image.Height;
            pixels = Yuv420ToRgba(planes[0], planes[1], planes[2], width, height);
        }
        finally
        {
            // Disposing the binding does not close the image, and a reader holds only as many as it was made for.
            image.Close();
            image.Dispose();
        }

        var upright = Rotate(pixels, width, height, _rotation, out var outWidth, out var outHeight);
        _image = new PixelImage(outWidth, outHeight, upright);
        _imageStart = Position;
    }

    // Limited-range YUV 4:2:0 to RGBA, with the Rec. 601 matrix for a picture of 576 lines or fewer and Rec. 709 above, as a browser chooses for a stream that does
    // not say. Each plane has its own row and pixel stride.
    internal static byte[] Yuv420ToRgba(global::Android.Media.Image.Plane yPlane, global::Android.Media.Image.Plane uPlane, global::Android.Media.Image.Plane vPlane, int width, int height)
    {
        var y = ReadPlane(yPlane);
        var u = ReadPlane(uPlane);
        var v = ReadPlane(vPlane);
        var yRow = yPlane.RowStride;
        var yPixel = yPlane.PixelStride;
        var uRow = uPlane.RowStride;
        var uPixel = uPlane.PixelStride;
        var vRow = vPlane.RowStride;
        var vPixel = vPlane.PixelStride;

        // Fixed point, 16 bits: R = 1.164 (Y-16) + rv (V-128), and so on.
        var (rv, gu, gv, bu) = height <= 576 ? (104595, -25675, -53279, 132201) : (117989, -14003, -35073, 138680);
        var pixels = new byte[width * height * 4];
        var o = 0;
        for (var row = 0; row < height; row++)
        {
            for (var col = 0; col < width; col++)
            {
                var yy = (y[(row * yRow) + (col * yPixel)] - 16) * 76309;
                var cu = u[((row >> 1) * uRow) + ((col >> 1) * uPixel)] - 128;
                var cv = v[((row >> 1) * vRow) + ((col >> 1) * vPixel)] - 128;
                pixels[o++] = Clamp((yy + (rv * cv)) >> 16);
                pixels[o++] = Clamp((yy + (gu * cu) + (gv * cv)) >> 16);
                pixels[o++] = Clamp((yy + (bu * cu)) >> 16);
                pixels[o++] = 255;
            }
        }

        return pixels;
    }

    private static byte[] ReadPlane(global::Android.Media.Image.Plane plane)
    {
        var buffer = plane.Buffer!;
        var bytes = new byte[buffer.Remaining()];
        buffer.Get(bytes);
        return bytes;
    }

    private static byte Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);

    // The picture turned clockwise by the stream's rotation, which the player leaves to the viewer.
    private static byte[] Rotate(byte[] pixels, int width, int height, int degrees, out int outWidth, out int outHeight)
    {
        if (degrees == 0)
        {
            (outWidth, outHeight) = (width, height);
            return pixels;
        }

        (outWidth, outHeight) = degrees == 180 ? (width, height) : (height, width);
        var result = new byte[pixels.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (nx, ny) = degrees switch
                {
                    90 => (height - 1 - y, x),
                    180 => (width - 1 - x, height - 1 - y),
                    _ => (y, width - 1 - x),
                };

                System.Buffer.BlockCopy(pixels, ((y * width) + x) * 4, result, ((ny * outWidth) + nx) * 4, 4);
            }
        }

        return result;
    }

    private void ApplyVolume()
    {
        if (_player is not { } player || _disposed)
            return;

        var level = _muted ? 0f : (float)_volume;
        player.SetVolume(level, level);
    }

    private void ApplyRate(MediaPlayer player)
    {
        try
        {
            // Setting the speed of a paused player starts it, so a paused one is paused again.
            var wasPlaying = player.IsPlaying;
            player.PlaybackParams = new PlaybackParams().SetSpeed((float)_rate)!;
            if (!wasPlaying)
                player.Pause();
        }
        catch (Java.Lang.IllegalStateException)
        {
        }
    }

    private void OnCompleted(object? sender, EventArgs e)
    {
        if (_disposed)
            return;

        _playing = false;
        _ended = true;
        if (_duration is { } length)
            _position = length;

        Notify(MediaChange.Ended | MediaChange.State);
    }

    private void OnFailed(object? sender, MediaPlayer.ErrorEventArgs e)
    {
        if (_error != MediaErrorCode.None)
            return;

        _error = MediaErrorCode.Decode;
        _playing = false;
        Notify(MediaChange.Error | MediaChange.State);
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
