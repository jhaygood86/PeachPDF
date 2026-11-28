using Android.Hardware;
using PeachDrawing.Core;
using PeachDrawing.Skia;

namespace PeachBrackets.Media.Android.Internal;

/// <summary>
/// One picture the player rendered, kept where the decoder left it: the reader's image stays open (so the decoder cannot reuse its buffer) until the session
/// and every frame leased from it are done, and a canvas drawing on the host's GPU samples the buffer as a texture, with no copy. A canvas with no GPU, or a
/// readback, gets the pixels instead, copied out of the buffer by the platform.
/// </summary>
internal sealed class GpuPicture
{
    private readonly global::Android.Media.Image _source;
    private readonly HardwareBuffer _buffer;
    private int _references = 1;

    [System.Runtime.Versioning.SupportedOSPlatform("android29.0")]
    public GpuPicture(SkiaRenderContext context, global::Android.Media.Image source, HardwareBuffer buffer)
    {
        _source = source;
        _buffer = buffer;
        Image = context.CreateGpuImage(source.Width, source.Height, gpu => HardwareBufferImporter.Import(gpu, buffer, source.Width, source.Height), ReadPixels);
    }

    /// <summary>The picture, upright (the player does not rotate, and a rotated stream is not given to this path).</summary>
    public PeachDrawing.Core.Image Image { get; }

    /// <summary>Takes another reference, for a frame handed out; the callback it returns gives it back.</summary>
    public Action Lease()
    {
        Interlocked.Increment(ref _references);
        return Release;
    }

    /// <summary>Gives back a reference; the last one frees the picture.</summary>
    public void Release()
    {
        if (Interlocked.Decrement(ref _references) != 0)
            return;

        Image.Dispose();
        _buffer.Dispose();
        try
        {
            _source.Close();
        }
        catch (Java.Lang.IllegalStateException)
        {
        }

        _source.Dispose();
    }

    private PixelBuffer? ReadPixels()
    {
        if (Volatile.Read(ref _references) <= 0)
            return null;

        // A buffer in the decoder's private format has no planes to read.
        var planes = _source.GetPlanes();
        if (planes is null || planes.Length < 3)
            return null;

        var pixels = AndroidMediaSession.Yuv420ToRgba(planes[0], planes[1], planes[2], _source.Width, _source.Height);
        return new PixelBuffer(_source.Width, _source.Height, pixels);
    }
}
