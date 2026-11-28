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
    private Colorimetry _colorimetry;
    private VideoPicture? _current;
    private VideoPicture? _pending;

    private VideoSource(IMFSourceReader* reader)
    {
        _reader = reader;
    }

    /// <summary>Whether the file has a picture.</summary>
    public bool HasVideo { get; private set; }

    /// <summary>The RGB space the pictures are in.</summary>
    public MediaColorSpace ColorSpace => _colorimetry.Space;

    /// <summary>What the stream says about its YUV (for tests).</summary>
    internal Colorimetry Colorimetry => _colorimetry;

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
            var rgb = PInvoke.MFVideoFormat_NV12;
            requested->SetGUID(&major, &video);
            requested->SetGUID(&subtype, &rgb);
            reader->SetCurrentMediaType(s_videoStream, null, requested);
            reader->GetCurrentMediaType(s_videoStream, &current);

            var sizeKey = PInvoke.MF_MT_FRAME_SIZE;
            ulong size;
            current->GetUINT64(&sizeKey, &size);
            Width = (int)(size >> 32);
            Height = (int)(size & 0xFFFFFFFF);

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

            _colorimetry = ReadColorimetry(current, reader);
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

    // What the stream says about its YUV: the matrix, the range and the primaries, on the type the decoder gives out or else on the one the file has. A stream that
    // says nothing is taken as a browser takes it.
    private Colorimetry ReadColorimetry(IMFMediaType* current, IMFSourceReader* reader)
    {
        IMFMediaType* native = null;
        try
        {
            try
            {
                reader->GetNativeMediaType(s_videoStream, 0, &native);
            }
            catch (COMException)
            {
            }

            var currentHandle = (nint)current;
            var nativeHandle = (nint)native;

            uint Read(Guid key)
            {
                foreach (var type in new[] { currentHandle, nativeHandle })
                {
                    if (type == 0)
                        continue;

                    try
                    {
                        uint value;
                        ((IMFMediaType*)type)->GetUINT32(&key, &value);
                        if (value != 0)
                            return value;
                    }
                    catch (COMException)
                    {
                    }
                }

                return 0;
            }

            var matrix = Read(PInvoke.MF_MT_YUV_MATRIX);
            var full = Read(PInvoke.MF_MT_VIDEO_NOMINAL_RANGE) == 1;
            var primaries = Read(PInvoke.MF_MT_VIDEO_PRIMARIES);

            // MFVideoTransferMatrix: 1 BT.709, 2 BT.601, 3 SMPTE 240M, 4 and 5 BT.2020. MFVideoPrimaries: 4 BT.470-2 B/G, 5 SMPTE 170M, 8 SMPTE C, 9 BT.2020, 11 DCI-P3.
            return matrix switch
            {
                1 => primaries == 11 ? Colorimetry.Rec709(full) with { Space = MediaColorSpace.DisplayP3 } : Colorimetry.Rec709(full),
                2 => Colorimetry.Rec601(full, manage: primaries is 0 or 4 or 5 or 8),
                3 => Colorimetry.Smpte240M(full),
                4 or 5 => Colorimetry.Rec2020(full),
                _ => primaries == 9 ? Colorimetry.Rec2020(full) : Colorimetry.Assumed(Height) with { FullRange = full },
            };
        }
        finally
        {
            if (native is not null)
                native->Release();
        }
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
                var rgba = YuvConverter.Nv12ToRgba(data, (int)used, Width, Height, _colorimetry);
                return rgba is null ? null : new VideoPicture(TimeSpan.FromTicks(time), TimeSpan.FromTicks(length), Width, Height, rgba);
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
