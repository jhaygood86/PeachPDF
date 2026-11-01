using PeachDrawing.Core;
using SkiaSharp;
using System;

namespace PeachDrawing.Skia;

/// <summary>
/// The image half of a tile: what a <see cref="SkiaCanvas"/> drew on the canvas <see cref="SkiaCanvas.CreateTile"/> returned with it,
/// recorded as a vector picture. The recorder belongs to this image, which releases it (and the canvas that fills it, if that canvas is
/// still open) on <see cref="Dispose"/>.
/// </summary>
internal sealed class SkiaTileImage : PeachDrawing.Core.Image
{
    private readonly SkiaCanvas _canvas;
    private readonly SKPictureRecorder _recorder;
    private readonly SkiaResourceCounts _counts;
    private SKPicture? _picture;
    private bool _disposed;

    public SkiaTileImage(double width, double height, SkiaCanvas canvas, SKPictureRecorder recorder, SkiaResourceCounts counts)
    {
        Width = width;
        Height = height;
        _canvas = canvas;
        _recorder = recorder;
        _counts = counts;
    }

    public override double Width { get; }

    public override double Height { get; }

    public override bool Interpolate { get; set; } = true;

    /// <summary>The recorded content, ready to draw: the canvas that filled it is closed first, which ends the recording.</summary>
    public SKPicture Content
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_picture is null)
            {
                _canvas.Dispose();
                _picture = _recorder.EndRecording();
            }

            return _picture;
        }
    }

    public override void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _canvas.Dispose();
        _picture?.Dispose();
        _recorder.Dispose();
        _counts.Recorder(-1);
    }
}
