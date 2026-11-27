using PeachDrawing.Core;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using static PeachBrackets.Media.GStreamer.Internal.GstNative;

namespace PeachBrackets.Media.GStreamer.Internal;

/// <summary>
/// One <c>playbin</c> pipeline over a copy of the resource in a temporary file. A video's pictures come from an <c>appsink</c> after <c>videoconvert</c>,
/// as straight RGBA, and are polled (no native callbacks). Two ways to play:
/// by the player's own clock (the pipeline plays; sound goes to the default output), or, with <see cref="MediaProviderOptions.DeterministicClock"/>, by the
/// document's clock: the pipeline stays paused, the position is kept here, advanced only by <see cref="Update"/>, and each picture is found by an accurate
/// flushing seek to that position, so the same document shows the same pictures however fast the machine is.
/// </summary>
internal sealed class GstSession : IMediaSession
{
    private const long NoTime = -1;

    private readonly MediaKind _kind;
    private readonly bool _deterministic;
    private readonly string _file;
    private readonly nint _pipeline;
    private readonly nint _sink;
    private readonly nint _convert;
    private readonly nint _bus;

    private bool _disposed;
    private bool _keepFile;
    private int _width;
    private int _height;
    private TimeSpan _frameDuration = TimeSpan.FromMilliseconds(40);
    private MediaColorSpace _colorSpace;
    private TimeSpan? _duration;

    private TimeSpan _position;
    private TimeSpan? _lastDocumentTime;
    private bool _playing;
    private bool _ended;
    private double _rate = 1;
    private double _volume = 1;
    private bool _muted;
    private bool _loop;
    private MediaErrorCode _error;

    private PixelImage? _image;
    private TimeSpan _imageStart;
    private TimeSpan _imageEnd;

    private GstSession(MediaKind kind, bool deterministic, string file, nint pipeline, nint sink, nint convert, bool muted, bool loop)
    {
        _kind = kind;
        _deterministic = deterministic;
        _file = file;
        _pipeline = pipeline;
        _sink = sink;
        _convert = convert;
        _bus = gst_element_get_bus(pipeline);
        _muted = muted;
        _loop = loop;
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

            Pump();
            if (gst_element_query_position(_pipeline, FormatTime, out var ns) != 0 && ns >= 0)
                _position = FromNanoseconds(ns);

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
                gst_element_seek(_pipeline, value, FormatTime, SeekFlush | SeekAccurate, SeekTypeSet, ToNanoseconds(Position), SeekTypeNone, 0);
        }
    }

    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            if (!_disposed)
                SetDouble(_pipeline, "volume", _volume);
        }
    }

    public bool Muted
    {
        get => _muted;
        set
        {
            _muted = value;
            if (!_disposed)
                SetBoolean(_pipeline, "mute", value);
        }
    }

    public bool Loop
    {
        get => _loop;
        set => _loop = value;
    }

    public event EventHandler<MediaChangedEventArgs>? Changed;

    /// <summary>Opens a resource: copies it to a file, prerolls the pipeline and learns the duration and picture size.</summary>
    public static async ValueTask<GstSession?> OpenAsync(MediaRequest request, MediaProviderOptions options, CancellationToken cancellationToken)
    {
        var response = await request.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (response?.ResourceStream is not { } source)
            return null;

        var file = Path.Combine(Path.GetTempPath(), "peachbrackets-media-" + Guid.NewGuid().ToString("N"));
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

        var session = await StartAsync(file, request, options, cancellationToken).ConfigureAwait(false);
        if (session is null)
            TryDelete(file);

        return session;
    }

    private static async ValueTask<GstSession?> StartAsync(string file, MediaRequest request, MediaProviderOptions options, CancellationToken cancellationToken)
    {
        var video = request.Kind == MediaKind.Video;
        var deterministic = options.DeterministicClock;
        var audioSink = deterministic || request.Muted ? "fakesink" : "autoaudiosink";

        // A video leaves the player through an appsink after a videoconvert, which gives straight RGBA and applies the stream's YUV matrix and range.
        var videoSink = video
            ? "videoconvert name=convert ! appsink name=sink caps=video/x-raw,format=RGBA sync=" + (deterministic ? "false" : "true")
                + " wait-on-eos=false max-buffers=1 drop=" + (deterministic ? "false" : "true")
            : "fakesink";
        var description = "playbin uri=\"" + new Uri(file).AbsoluteUri + "\" audio-sink=\"" + audioSink + "\" video-sink=\"" + videoSink + "\"";

        var pipeline = gst_parse_launch(description, out _);
        if (pipeline == 0)
            return null;

        gst_element_set_state(pipeline, StatePaused);
        var result = await Task.Run(() => gst_element_get_state(pipeline, out _, out _, 15UL * (ulong)Second), cancellationToken).ConfigureAwait(false);

        // The sink joins the player's own bin once it has a stream to show, so it is looked up after the preroll.
        var sink = video ? gst_bin_get_by_name(pipeline, "sink") : 0;
        var convert = video ? gst_bin_get_by_name(pipeline, "convert") : 0;
        var session = new GstSession(request.Kind, deterministic, file, pipeline, sink, convert, request.Muted, request.Loop);
        if (result == ChangeFailure || gst_bus_pop_filtered(session._bus, MessageError) is var failure && failure != 0)
        {
            session._keepFile = true;
            session.Dispose();
            return null;
        }

        session.Learn();
        if (request.Muted)
            SetBoolean(pipeline, "mute", true);

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
            gst_element_set_state(_pipeline, StatePlaying);

        Notify(MediaChange.State);
    }

    public void Pause()
    {
        if (_disposed)
            return;

        _playing = false;
        if (!_deterministic)
            gst_element_set_state(_pipeline, StatePaused);

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
            gst_element_seek(_pipeline, _rate, FormatTime, SeekFlush | SeekAccurate, SeekTypeSet, ToNanoseconds(position), SeekTypeNone, 0);

        Notify(MediaChange.Position | MediaChange.Frame);
    }

    public void Update(TimeSpan documentTime)
    {
        if (_disposed)
            return;

        if (!_deterministic)
        {
            Pump();
            return;
        }

        var last = _lastDocumentTime;
        _lastDocumentTime = documentTime;
        if (last is not { } before || !_playing || documentTime <= before)
            return;

        var moved = (documentTime - before) * _rate;
        var next = _position + moved;
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
        if (_disposed || _sink == 0)
            return null;

        if (_deterministic)
        {
            var at = _position;
            if (_image is null || at < _imageStart || at >= _imageEnd)
            {
                // Found, not played to: an accurate flushing seek leaves the picture at the position as the preroll sample.
                gst_element_seek_simple(_pipeline, FormatTime, SeekFlush | SeekAccurate, ToNanoseconds(at));
                var sample = gst_app_sink_try_pull_preroll(_sink, 5UL * (ulong)Second);
                if (sample != 0)
                    TakePicture(sample, at);
            }
        }
        else
        {
            Pump();
            var sample = gst_app_sink_try_pull_sample(_sink, 0);
            if (sample == 0 && _image is null)
                sample = gst_app_sink_try_pull_preroll(_sink, 0);

            if (sample != 0)
                TakePicture(sample, null);
        }

        return _image is null ? null : new MediaFrame(_image, _imageStart) { ColorSpace = _colorSpace };
    }

    public ValueTask<MediaFrame?> AcquireFrameAtAsync(TimeSpan position, CancellationToken cancellationToken = default)
    {
        if (_disposed || _sink == 0)
            return new ValueTask<MediaFrame?>((MediaFrame?)null);

        position = Clamp(position);
        return new ValueTask<MediaFrame?>(Task.Run(() =>
        {
            gst_element_seek_simple(_pipeline, FormatTime, SeekFlush | SeekAccurate, ToNanoseconds(position));
            var sample = gst_app_sink_try_pull_preroll(_sink, 5UL * (ulong)Second);
            if (sample == 0)
                return null;

            TakePicture(sample, position);
            return _image is null ? null : (MediaFrame?)new MediaFrame(_image, _imageStart) { ColorSpace = _colorSpace };
        }, cancellationToken));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _playing = false;
        gst_element_set_state(_pipeline, StateNull);
        if (_bus != 0)
            gst_object_unref(_bus);

        if (_sink != 0)
            gst_object_unref(_sink);

        if (_convert != 0)
            gst_object_unref(_convert);

        gst_object_unref(_pipeline);
        if (!_keepFile)
            TryDelete(_file);
    }

    /// <summary>Reads what the prerolled pipeline knows: the duration, and for a video the picture size, its rate and its colorimetry.</summary>
    private void Learn()
    {
        if (gst_element_query_duration(_pipeline, FormatTime, out var duration) != 0 && duration > 0)
            _duration = FromNanoseconds(duration);

        if (_sink == 0)
            return;

        var sample = gst_app_sink_try_pull_preroll(_sink, 5UL * (ulong)Second);
        if (sample != 0)
            TakePicture(sample, TimeSpan.Zero);

        if (_convert != 0 && gst_element_get_static_pad(_convert, "sink") is var pad && pad != 0)
        {
            var caps = gst_pad_get_current_caps(pad);
            if (caps != 0)
            {
                var structure = gst_caps_get_structure(caps, 0);
                var colorimetry = structure == 0 ? null : GetString(structure, "colorimetry");
                _colorSpace = colorimetry is not null && colorimetry.Contains("2020", StringComparison.Ordinal) ? MediaColorSpace.Rec2020
                    : colorimetry is not null && colorimetry.Contains("432", StringComparison.Ordinal) ? MediaColorSpace.DisplayP3
                    : MediaColorSpace.Srgb;
                gst_mini_object_unref(caps);
            }

            gst_object_unref(pad);
        }
    }

    /// <summary>Makes the sample the current picture and gives the sample back.</summary>
    private unsafe void TakePicture(nint sample, TimeSpan? expectedAt)
    {
        try
        {
            var buffer = gst_sample_get_buffer(sample);
            var caps = gst_sample_get_caps(sample);
            if (buffer == 0 || caps == 0)
                return;

            var structure = gst_caps_get_structure(caps, 0);
            if (structure == 0 || gst_structure_get_int(structure, "width", out var width) == 0 || gst_structure_get_int(structure, "height", out var height) == 0
                || width < 1 || height < 1)
                return;

            if (gst_structure_get_fraction(structure, "framerate", out var numerator, out var denominator) != 0 && numerator > 0 && denominator > 0)
                _frameDuration = TimeSpan.FromSeconds((double)denominator / numerator);

            var info = stackalloc byte[128];
            new Span<byte>(info, 128).Clear();
            if (gst_buffer_map(buffer, (nint)info, 1) == 0)
                return;

            try
            {
                // GstMapInfo: memory (0), flags (8), data (16), size (24).
                var data = *(byte**)(info + 16);
                var size = (long)*(nuint*)(info + 24);
                if (data == null || size < (long)width * height * 4)
                    return;

                var pixels = new byte[width * height * 4];
                for (var i = 0; i < pixels.Length; i += 4)
                {
                    int alpha = data[i + 3];
                    pixels[i + 3] = (byte)alpha;
                    if (alpha == 255)
                    {
                        pixels[i] = data[i];
                        pixels[i + 1] = data[i + 1];
                        pixels[i + 2] = data[i + 2];
                    }
                    else
                    {
                        pixels[i] = (byte)((data[i] * alpha + 127) / 255);
                        pixels[i + 1] = (byte)((data[i + 1] * alpha + 127) / 255);
                        pixels[i + 2] = (byte)((data[i + 2] * alpha + 127) / 255);
                    }
                }

                _width = width;
                _height = height;
                _image = new PixelImage(width, height, pixels);
            }
            finally
            {
                gst_buffer_unmap(buffer, (nint)info);
            }

            // GstBuffer (64-bit): the mini object is 64 bytes, then pool (64), pts (72), dts (80), duration (88).
            var start = expectedAt ?? _position;
            if (IntPtr.Size == 8)
            {
                var pts = Marshal.ReadInt64(buffer, 72);
                if (pts != NoTime)
                    start = FromNanoseconds(pts);

                var duration = Marshal.ReadInt64(buffer, 88);
                if (duration > 0)
                    _frameDuration = FromNanoseconds(duration);
            }

            // A picture shows from where it starts until the next one does; a position the player placed just before it still belongs to it.
            if (expectedAt is { } at && at < start)
                start = at;

            _imageStart = start;
            _imageEnd = start + _frameDuration;
            if (expectedAt is { } wanted && wanted >= _imageEnd)
                _imageEnd = wanted + _frameDuration;
        }
        finally
        {
            gst_mini_object_unref(sample);
        }
    }

    /// <summary>Reads what the player reported since last time.</summary>
    private void Pump()
    {
        if (_disposed)
            return;

        // Messages that do not match the filter are discarded as the bus is read, so the error and the end are taken in one read and told apart by type.
        for (var message = gst_bus_pop_filtered(_bus, MessageError | MessageEos); message != 0; message = gst_bus_pop_filtered(_bus, MessageError | MessageEos))
        {
            // GstMessage: the 64-byte mini object (36 on a 32-bit process), then the type.
            var type = (uint)Marshal.ReadInt32(message, IntPtr.Size == 8 ? 64 : 36);
            gst_mini_object_unref(message);

            if (type == MessageError)
            {
                if (_error == MediaErrorCode.None)
                {
                    _error = MediaErrorCode.Decode;
                    _playing = false;
                    Notify(MediaChange.Error | MediaChange.State);
                }
            }
            else if (!_deterministic)
            {
                if (_loop)
                {
                    gst_element_seek(_pipeline, _rate, FormatTime, SeekFlush | SeekAccurate, SeekTypeSet, 0, SeekTypeNone, 0);
                    Notify(MediaChange.Position);
                }
                else
                {
                    _playing = false;
                    _ended = true;
                    if (_duration is { } length)
                        _position = length;

                    Notify(MediaChange.Ended | MediaChange.State);
                }
            }
        }
    }

    private TimeSpan Clamp(TimeSpan position) =>
        position < TimeSpan.Zero ? TimeSpan.Zero : _duration is { } length && position > length ? length : position;

    private void Notify(MediaChange change) => Changed?.Invoke(this, new MediaChangedEventArgs(change));

    private static long ToNanoseconds(TimeSpan time) => time.Ticks * 100;

    private static TimeSpan FromNanoseconds(long nanoseconds) => TimeSpan.FromTicks(nanoseconds / 100);

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
