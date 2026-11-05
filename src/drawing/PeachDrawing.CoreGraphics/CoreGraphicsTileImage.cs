using CoreGraphics;
using System;

namespace PeachDrawing.CoreGraphics;

/// <summary>
/// The image half of a tile: what a <see cref="CoreGraphicsCanvas"/> drew on the canvas <see cref="CoreGraphicsCanvas.CreateTile"/> returned with
/// it, in a bitmap sized for the device it was made for. The bitmap belongs to this image, which releases it (and the canvas that fills it, if
/// that canvas is still open) on <see cref="Dispose"/>.
/// </summary>
internal sealed class CoreGraphicsTileImage : PeachDrawing.Core.Image
{
    private readonly CoreGraphicsCanvas _canvas;
    private readonly CoreGraphicsOffscreen _offscreen;
    private CGImage? _content;
    private bool _disposed;

    public CoreGraphicsTileImage(double width, double height, CoreGraphicsCanvas canvas, CoreGraphicsOffscreen offscreen)
    {
        Width = width;
        Height = height;
        _canvas = canvas;
        _offscreen = offscreen;
    }

    public override double Width { get; }

    public override double Height { get; }

    public override bool Interpolate { get; set; } = true;

    /// <summary>The recorded content, ready to draw: the canvas that filled it is closed first. Owned by this image.</summary>
    public CGImage Content
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_content is null)
            {
                _canvas.Dispose();
                _content = _offscreen.Snapshot();
            }

            return _content;
        }
    }

    public override void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _canvas.Dispose();
        _offscreen.Dispose();
    }
}
