using PeachDrawing.Core;
using PeachImage;
using System;
using System.Collections.Generic;

namespace PeachDrawing;

/// <summary>
/// A <see cref="PeachDrawing.Core.Image"/> decoded directly through <c>PeachImage</c> - the portable decode library,
/// with no PDF type anywhere in the chain. This is what
/// <see cref="RasterRenderContext.ImageFromStreamInt"/> uses: a standalone raster canvas only ever wants an
/// image's pixels to sample, so it decodes eagerly and keeps nothing PDF-specific (no lazy JPEG/CMYK
/// pass-through for direct stream embedding, for example). This type lives in this package rather
/// than <c>PeachDrawing.Core</c> because it depends on <c>PeachImage</c> for the decode itself, a
/// dependency the abstraction layer deliberately does not take (a different <c>Canvas</c> backend may
/// decode images an entirely different way).
/// </summary>
internal sealed class DecodedImage : PeachDrawing.Core.Image
{
    private readonly PixelBuffer _pixels;

    // An animated image holds every frame composited, and the frame that is drawn.
    private (PixelBuffer Pixels, TimeSpan Duration)[]? _frames;
    private int _loopCount = 1;
    private volatile int _currentFrame;

    private DecodedImage(int width, int height, PixelBuffer pixels)
    {
        Width = width;
        Height = height;
        _pixels = pixels;
    }

    /// <summary>Decodes <paramref name="stream"/> to premultiplied RGBA8, or null when PeachImage cannot recognise the format.</summary>
    public static DecodedImage? TryDecode(System.IO.Stream stream)
    {
        // The bytes are kept for a moment: an animated image is read a second time, frame by frame, and a stream cannot always be wound back.
        var bytes = ReadAll(stream);
        if (!PeachImage.Image.TryLoad(new System.IO.MemoryStream(bytes), out var image, new DecoderOptions { TargetPixelFormat = PixelFormat.Rgba32 }) || image is null)
            return null;

        using (image)
        {
            var rgba = image.PixelMemory.Span.ToArray();
            Bitmap.Premultiply(rgba);
            var decoded = new DecodedImage(image.Width, image.Height, new PixelBuffer(image.Width, image.Height, rgba));
            if (image.IsAnimated)
                decoded.TryLoadFrames(bytes);

            return decoded;
        }
    }

    private static byte[] ReadAll(System.IO.Stream stream)
    {
        if (stream is System.IO.MemoryStream { Position: 0 } memory && memory.TryGetBuffer(out var buffer) && buffer.Offset == 0 && buffer.Count == buffer.Array!.Length)
            return buffer.Array;

        using var copy = new System.IO.MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    // Every frame of an animated GIF or WebP, composited. The image stays a still one when the file turns out to hold a single frame.
    private void TryLoadFrames(byte[] bytes)
    {
        try
        {
            var animation = AnimatedImage.Load(new System.IO.MemoryStream(bytes), new DecoderOptions { TargetPixelFormat = PixelFormat.Rgba32 });
            var frames = new List<(PixelBuffer, TimeSpan)>();
            foreach (var frame in animation.Frames)
            {
                if (frame.Image.Width != (int)Width || frame.Image.Height != (int)Height)
                    return;

                var rgba = frame.Image.PixelMemory.Span.ToArray();
                Bitmap.Premultiply(rgba);
                frames.Add((new PixelBuffer(frame.Image.Width, frame.Image.Height, rgba), frame.Duration));
            }

            if (frames.Count < 2 || (long)frames[0].Item1.Width * frames[0].Item1.Height * 4 * frames.Count > 256L * 1024 * 1024)
                return;

            _frames = [.. frames];
            _loopCount = animation.LoopCount;
        }
        catch (Exception ex) when (ex is System.IO.InvalidDataException or NotSupportedException or InvalidOperationException or System.IO.IOException)
        {
            // Not an animation this decoder can play: the first frame stands.
        }
    }

    public override double Width { get; }

    public override double Height { get; }

    public override bool Interpolate { get; set; } = true;

    public override void Dispose()
    {
    }

    public override PixelBuffer? GetPixels() => _frames is { } frames ? frames[_currentFrame].Pixels : _pixels;

    public override int FrameCount => _frames?.Length ?? 1;

    public override TimeSpan GetFrameDuration(int index) => _frames is { } frames && (uint)index < (uint)frames.Length ? frames[index].Duration : TimeSpan.Zero;

    public override int LoopCount => _loopCount;

    public override int CurrentFrame
    {
        get => _currentFrame;
        set
        {
            if (_frames is { } frames)
                _currentFrame = Math.Clamp(value, 0, frames.Length - 1);
        }
    }
}
