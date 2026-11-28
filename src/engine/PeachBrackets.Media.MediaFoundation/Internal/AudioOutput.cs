using System;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.Audio;
using Windows.Win32.Media.MediaFoundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Variant;

namespace PeachBrackets.Media.MediaFoundation.Internal;

/// <summary>
/// The sound of a file, rendered to the default output while a session plays by its own clock. A Media Foundation source reader decodes the first audio stream to 32-bit
/// float stereo at 48 kHz, and a thread feeds it to a shared-mode WASAPI client as fast as the client has room, so what is heard follows the session's position within
/// a buffer of 200 ms. The session's own stopwatch is the clock; this only has to start where it starts and keep up.
/// </summary>
internal sealed unsafe class AudioOutput : IDisposable
{
    private const int Channels = 2;
    private const int SampleRate = 48000;
    private const int BytesPerFrame = Channels * sizeof(float);
    private const long BufferDuration = 2_000_000; // 200 ms in units of 100 ns

    private static readonly uint s_audioStream = unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_FIRST_AUDIO_STREAM);
    private static readonly uint s_allStreams = unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_ALL_STREAMS);

    private readonly object _gate = new();
    private readonly ManualResetEventSlim _wake = new(false);
    private readonly Thread _thread;

    private IMFSourceReader* _reader;
    private IAudioClient* _client;
    private IAudioRenderClient* _render;
    private uint _bufferFrames;
    private volatile bool _disposed;
    private volatile bool _playing;
    private volatile bool _drained;
    private int _restart;
    private double _volume;
    private double _rate = 1;

    // Decoded sound that has not been given to the client yet.
    private byte[] _pending = [];
    private int _pendingOffset;
    private int _pendingLength;

    private AudioOutput(IMFSourceReader* reader, IAudioClient* client, IAudioRenderClient* render, uint bufferFrames, double volume)
    {
        _reader = reader;
        _client = client;
        _render = render;
        _bufferFrames = bufferFrames;
        _volume = volume;
        _thread = new Thread(Run) { IsBackground = true, Name = "PeachBrackets audio" };
        _thread.Start();
    }

    /// <summary>Opens the sound of a file on the default output, or returns null when it has none or there is no device.</summary>
    public static AudioOutput? TryCreate(string file, double rate, double volume)
    {
        Mf.Ensure();
        IMFSourceReader* reader = null;
        try
        {
            fixed (char* url = file)
                PInvoke.MFCreateSourceReaderFromURL(url, null, &reader).ThrowOnFailure();

            reader->SetStreamSelection(s_allStreams, false);
            reader->SetStreamSelection(s_audioStream, true);
            SetOutputType(reader);

            if (!OpenClient(rate, out var client, out var render, out var frames))
                return null;

            var output = new AudioOutput(reader, client, render, frames, volume) { _rate = rate };
            reader = null;
            return output;
        }
        catch (COMException)
        {
            // No audio stream: the video plays without sound.
            return null;
        }
        finally
        {
            if (reader is not null)
                reader->Release();
        }
    }

    // A shared-mode client on the default output for the sound at a rate. The sound is decoded at 48 kHz and the client is told it is a stream of 48 kHz times the rate, so
    // the system's own sample-rate converter plays it faster or slower (the pitch follows, as it does on tape). False when there is no device or it refuses the format.
    private static bool OpenClient(double rate, out IAudioClient* client, out IAudioRenderClient* render, out uint frames)
    {
        IMMDeviceEnumerator* enumerator = null;
        IMMDevice* device = null;
        IAudioClient* newClient = null;
        IAudioRenderClient* newRender = null;
        client = null;
        render = null;
        frames = 0;
        try
        {
            var clsid = typeof(MMDeviceEnumerator).GUID;
            var iid = IMMDeviceEnumerator.IID_Guid;
            PInvoke.CoCreateInstance(&clsid, null, CLSCTX.CLSCTX_ALL, &iid, (void**)&enumerator).ThrowOnFailure();
            enumerator->GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eConsole, &device);
            var clientIid = IAudioClient.IID_Guid;
            device->Activate(&clientIid, CLSCTX.CLSCTX_ALL, null, (void**)&newClient);

            var perSecond = (uint)Math.Clamp(Math.Round(SampleRate * rate), 8000, 384000);
            var format = new WAVEFORMATEX
            {
                wFormatTag = (ushort)PInvoke.WAVE_FORMAT_IEEE_FLOAT,
                nChannels = Channels,
                nSamplesPerSec = perSecond,
                wBitsPerSample = 32,
                nBlockAlign = BytesPerFrame,
                nAvgBytesPerSec = perSecond * BytesPerFrame,
            };
            newClient->Initialize(AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED, PInvoke.AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | PInvoke.AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY, BufferDuration, 0, &format, null);
            uint size;
            newClient->GetBufferSize(&size);
            var renderIid = IAudioRenderClient.IID_Guid;
            newClient->GetService(&renderIid, (void**)&newRender);

            client = newClient;
            render = newRender;
            frames = size;
            newClient = null;
            newRender = null;
            return true;
        }
        catch (COMException)
        {
            return false;
        }
        finally
        {
            if (newRender is not null)
                newRender->Release();

            if (newClient is not null)
                newClient->Release();

            if (device is not null)
                device->Release();

            if (enumerator is not null)
                enumerator->Release();
        }
    }

    private static void SetOutputType(IMFSourceReader* reader)
    {
        IMFMediaType* type = null;
        try
        {
            PInvoke.MFCreateMediaType(&type).ThrowOnFailure();
            var major = PInvoke.MF_MT_MAJOR_TYPE;
            var audio = PInvoke.MFMediaType_Audio;
            var subtype = PInvoke.MF_MT_SUBTYPE;
            var pcm = PInvoke.MFAudioFormat_Float;
            type->SetGUID(&major, &audio);
            type->SetGUID(&subtype, &pcm);
            SetUInt(type, PInvoke.MF_MT_AUDIO_NUM_CHANNELS, Channels);
            SetUInt(type, PInvoke.MF_MT_AUDIO_SAMPLES_PER_SECOND, SampleRate);
            SetUInt(type, PInvoke.MF_MT_AUDIO_BITS_PER_SAMPLE, 32);
            SetUInt(type, PInvoke.MF_MT_AUDIO_BLOCK_ALIGNMENT, BytesPerFrame);
            SetUInt(type, PInvoke.MF_MT_AUDIO_AVG_BYTES_PER_SECOND, SampleRate * BytesPerFrame);
            SetUInt(type, PInvoke.MF_MT_ALL_SAMPLES_INDEPENDENT, 1);
            reader->SetCurrentMediaType(s_audioStream, null, type);
        }
        finally
        {
            if (type is not null)
                type->Release();
        }
    }

    private static void SetUInt(IMFMediaType* type, Guid key, uint value) => type->SetUINT32(&key, value);

    /// <summary>Starts (or restarts) the sound at a position.</summary>
    public void Start(TimeSpan position)
    {
        if (_disposed)
            return;

        lock (_gate)
        {
            try
            {
                _client->Stop();
                _client->Reset();

                PROPVARIANT target = default;
                target.Anonymous.Anonymous.vt = VARENUM.VT_I8;
                target.Anonymous.Anonymous.Anonymous.hVal = position.Ticks;
                var none = Guid.Empty;
                _reader->SetCurrentPosition(&none, &target);
            }
            catch (COMException)
            {
                return;
            }

            _pendingOffset = _pendingLength = 0;
            _drained = false;
            _restart++;
            _playing = true;
            try
            {
                _client->Start();
            }
            catch (COMException)
            {
                _playing = false;
            }
        }

        _wake.Set();
    }

    /// <summary>Stops the sound where it is.</summary>
    public void Stop()
    {
        if (_disposed)
            return;

        lock (_gate)
        {
            _playing = false;
            try
            {
                _client->Stop();
            }
            catch (COMException)
            {
            }
        }
    }

    /// <summary>
    /// Moves the sound to another rate by opening a new client at it. The sound is stopped: the session starts it again at its position.
    /// </summary>
    public void SetRate(double rate)
    {
        if (_disposed || rate == _rate)
            return;

        lock (_gate)
        {
            _playing = false;
            if (!OpenClient(rate, out var client, out var render, out var frames))
                return;

            try
            {
                _client->Stop();
            }
            catch (COMException)
            {
            }

            _render->Release();
            _client->Release();
            _client = client;
            _render = render;
            _bufferFrames = frames;
            _rate = rate;
        }
    }

    public void SetVolume(double volume) => _volume = volume;

    // The thread that keeps the client's buffer full.
    private void Run()
    {
        Mf.Ensure();
        while (!_disposed)
        {
            if (!_playing)
            {
                _wake.Wait(100);
                _wake.Reset();
                continue;
            }

            var wrote = false;
            lock (_gate)
            {
                if (_disposed || !_playing)
                    continue;

                try
                {
                    wrote = Fill();
                }
                catch (COMException)
                {
                    _playing = false;
                }
            }

            if (!wrote)
                Thread.Sleep(5);
        }
    }

    // Gives the client as much of the decoded sound as it has room for. Returns whether anything was written.
    private bool Fill()
    {
        uint padding;
        _client->GetCurrentPadding(&padding);
        var room = (int)(_bufferFrames - padding);
        if (room <= 0)
            return false;

        if (_pendingOffset >= _pendingLength && !ReadMore())
            return false;

        var frames = Math.Min(room, (_pendingLength - _pendingOffset) / BytesPerFrame);
        if (frames <= 0)
            return false;

        byte* target;
        _render->GetBuffer((uint)frames, &target);
        var gain = (float)_volume;
        var count = frames * Channels;
        fixed (byte* source = &_pending[_pendingOffset])
        {
            var from = (float*)source;
            var to = (float*)target;
            for (var i = 0; i < count; i++)
                to[i] = from[i] * gain;
        }

        _render->ReleaseBuffer((uint)frames, 0);
        _pendingOffset += frames * BytesPerFrame;
        return true;
    }

    // Decodes the next piece of the sound; false at the end of it.
    private bool ReadMore()
    {
        if (_drained)
            return false;

        IMFSample* sample = null;
        IMFMediaBuffer* buffer = null;
        try
        {
            uint actual;
            uint flags;
            long time;
            _reader->ReadSample(s_audioStream, 0, &actual, &flags, &time, &sample);
            if (sample is null)
            {
                _drained = (flags & (uint)MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ENDOFSTREAM) != 0 || _drained;
                return false;
            }

            sample->ConvertToContiguousBuffer(&buffer);
            byte* data;
            uint maximum;
            uint used;
            buffer->Lock(&data, &maximum, &used);
            try
            {
                if (_pending.Length < used)
                    _pending = new byte[used];

                new ReadOnlySpan<byte>(data, (int)used).CopyTo(_pending);
                _pendingOffset = 0;
                _pendingLength = (int)used;
            }
            finally
            {
                buffer->Unlock();
            }

            return true;
        }
        catch (COMException)
        {
            _drained = true;
            return false;
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
        if (_disposed)
            return;

        _disposed = true;
        _playing = false;
        _wake.Set();
        _thread.Join(2000);
        lock (_gate)
        {
            try
            {
                _client->Stop();
            }
            catch (COMException)
            {
            }

            _render->Release();
            _client->Release();
            _reader->Release();
            _render = null;
            _client = null;
            _reader = null;
        }

        _wake.Dispose();
    }
}
