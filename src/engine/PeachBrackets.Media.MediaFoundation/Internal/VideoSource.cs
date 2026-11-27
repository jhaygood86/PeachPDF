using System;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.MediaFoundation;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Variant;

namespace PeachBrackets.Media.MediaFoundation.Internal;

/// <summary>One picture of a video: where it starts, how long it lasts, and its pixels as premultiplied RGBA.</summary>
internal sealed record VideoPicture(TimeSpan Time, TimeSpan Duration, int Width, int Height, byte[] Rgba);

/// <summary>
/// A Media Foundation source reader that hands out the pictures of a file's first video stream as RGB. It decodes forward, and goes back to a key frame only when asked
/// for a position behind where it is or far ahead, so playing at the frame rate reads each picture once and the same position always gives the same picture.
/// </summary>
internal sealed unsafe class VideoSource : IDisposable
{
    private static readonly uint s_videoStream = unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_FIRST_VIDEO_STREAM);
    private static readonly uint s_allStreams = unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_ALL_STREAMS);
    private static readonly uint s_mediaSource = unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_MEDIASOURCE);

    private IMFSourceReader* _reader;
    private int _stride;
    private VideoPicture? _current;
    private VideoPicture? _pending;

    private VideoSource(IMFSourceReader* reader)
    {
        _reader = reader;
    }

    /// <summary>Whether the file has a picture.</summary>
    public bool HasVideo { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public TimeSpan? Duration { get; private set; }

    public TimeSpan FrameDuration { get; private set; } = TimeSpan.FromMilliseconds(40);

    /// <summary>Opens the video of a file, or returns null when it has none or cannot be read.</summary>
    public static VideoSource? Open(string path)
    {
        Mf.Ensure();
        IMFAttributes* attributes = null;
        IMFSourceReader* reader = null;
        try
        {
            PInvoke.MFCreateAttributes(&attributes, 1).ThrowOnFailure();
            var processing = PInvoke.MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING;
            attributes->SetUINT32(&processing, 1);
            fixed (char* url = path)
            {
                PInvoke.MFCreateSourceReaderFromURL(url, attributes, &reader).ThrowOnFailure();
            }

            var source = new VideoSource(reader);
            reader = null;
            return source.Learn() ? source : Dispose(source);
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            if (reader is not null)
                reader->Release();

            if (attributes is not null)
                attributes->Release();
        }

        static VideoSource? Dispose(VideoSource source)
        {
            source.Dispose();
            return null;
        }
    }

    // Asks for RGB output of the first video stream and learns its size, stride, frame rate and the length of the file. A method that fails throws a COMException.
    private bool Learn()
    {
        var reader = _reader;
        try
        {
            var durationKey = PInvoke.MF_PD_DURATION;
            PROPVARIANT value = default;
            reader->GetPresentationAttribute(s_mediaSource, &durationKey, &value);
            var ticks = value.Anonymous.Anonymous.Anonymous.uhVal;
            if (ticks > 0)
                Duration = TimeSpan.FromTicks((long)ticks);

            PInvoke.PropVariantClear(&value);
        }
        catch (COMException)
        {
        }

        IMFMediaType* requested = null;
        IMFMediaType* current = null;
        try
        {
            reader->SetStreamSelection(s_allStreams, false);
            reader->SetStreamSelection(s_videoStream, true);
            PInvoke.MFCreateMediaType(&requested).ThrowOnFailure();
            var major = PInvoke.MF_MT_MAJOR_TYPE;
            var video = PInvoke.MFMediaType_Video;
            var subtype = PInvoke.MF_MT_SUBTYPE;
            var rgb = PInvoke.MFVideoFormat_RGB32;
            requested->SetGUID(&major, &video);
            requested->SetGUID(&subtype, &rgb);
            reader->SetCurrentMediaType(s_videoStream, null, requested);
            reader->GetCurrentMediaType(s_videoStream, &current);

            var sizeKey = PInvoke.MF_MT_FRAME_SIZE;
            ulong size;
            current->GetUINT64(&sizeKey, &size);
            Width = (int)(size >> 32);
            Height = (int)(size & 0xFFFFFFFF);

            var strideKey = PInvoke.MF_MT_DEFAULT_STRIDE;
            uint stride;
            try
            {
                current->GetUINT32(&strideKey, &stride);
                _stride = (int)stride;
            }
            catch (COMException)
            {
                _stride = Width * 4;
            }

            try
            {
                var rateKey = PInvoke.MF_MT_FRAME_RATE;
                ulong rate;
                current->GetUINT64(&rateKey, &rate);
                var numerator = (uint)(rate >> 32);
                var denominator = (uint)(rate & 0xFFFFFFFF);
                if (numerator > 0 && denominator > 0)
                    FrameDuration = TimeSpan.FromSeconds((double)denominator / numerator);
            }
            catch (COMException)
            {
            }

            HasVideo = Width > 0 && Height > 0;
        }
        catch (COMException)
        {
            // No video stream, or none Media Foundation can turn into RGB.
        }
        finally
        {
            if (requested is not null)
                requested->Release();

            if (current is not null)
                current->Release();
        }

        return HasVideo || Duration is not null;
    }

    /// <summary>The picture that shows at a position: the last one that starts at or before it (the first one, when none does).</summary>
    public VideoPicture? FrameAt(TimeSpan position)
    {
        if (_reader is null || !HasVideo)
            return null;

        if (_current is null || position < _current.Time || position - _current.Time > TimeSpan.FromSeconds(2))
        {
            SeekTo(position);
            _current = null;
            _pending = null;
        }

        while (true)
        {
            var next = _pending ?? ReadOne();
            _pending = null;
            if (next is null)
                break;

            if (_current is null || next.Time <= position)
            {
                _current = next;
                continue;
            }

            _pending = next;
            break;
        }

        return _current;
    }

    /// <summary>Where the picture held now stops showing: where the next one starts, or after its own length at the end.</summary>
    public TimeSpan? CurrentEnd => _current is null ? null : _pending?.Time ?? _current.Time + _current.Duration;

    private void SeekTo(TimeSpan position)
    {
        PROPVARIANT target = default;
        target.Anonymous.Anonymous.vt = VARENUM.VT_I8;
        target.Anonymous.Anonymous.Anonymous.hVal = position.Ticks;
        var none = Guid.Empty;
        try
        {
            _reader->SetCurrentPosition(&none, &target);
        }
        catch (COMException)
        {
        }
    }

    private VideoPicture? ReadOne()
    {
        IMFSample* sample = null;
        IMFMediaBuffer* buffer = null;
        try
        {
            uint actual;
            uint flags;
            long time;
            _reader->ReadSample(s_videoStream, 0, &actual, &flags, &time, &sample);
            if (sample is null)
                return null;

            long length = 0;
            try
            {
                sample->GetSampleDuration(&length);
            }
            catch (COMException)
            {
            }

            if (length <= 0)
                length = FrameDuration.Ticks;

            sample->ConvertToContiguousBuffer(&buffer);
            byte* data;
            uint maximum;
            uint used;
            buffer->Lock(&data, &maximum, &used);
            try
            {
                return new VideoPicture(TimeSpan.FromTicks(time), TimeSpan.FromTicks(length), Width, Height, ToRgba(data, (int)used));
            }
            finally
            {
                buffer->Unlock();
            }
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            if (buffer is not null)
                buffer->Release();

            if (sample is not null)
                sample->Release();
        }
    }

    // The decoder gives X8R8G8B8 rows: bytes B, G, R, X in memory. A negative default stride means the rows run from the bottom of the picture up.
    private byte[] ToRgba(byte* data, int used)
    {
        var rowBytes = Math.Abs(_stride);
        var bottomUp = _stride < 0;
        var rgba = new byte[Width * Height * 4];
        for (var y = 0; y < Height; y++)
        {
            var sourceRow = bottomUp ? Height - 1 - y : y;
            if ((sourceRow + 1) * rowBytes > used && sourceRow * rowBytes + (Width * 4) > used)
                break;

            var source = data + (sourceRow * rowBytes);
            var target = y * Width * 4;
            for (var x = 0; x < Width; x++)
            {
                var s = x * 4;
                var t = target + s;
                rgba[t] = source[s + 2];
                rgba[t + 1] = source[s + 1];
                rgba[t + 2] = source[s];
                rgba[t + 3] = 255;
            }
        }

        return rgba;
    }

    public void Dispose()
    {
        if (_reader is not null)
        {
            _reader->Release();
            _reader = null;
        }

        _current = null;
        _pending = null;
    }
}
