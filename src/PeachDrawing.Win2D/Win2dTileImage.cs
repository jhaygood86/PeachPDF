using Microsoft.Graphics.Canvas;
using PeachDrawing.Core;
using System;

namespace PeachDrawing.Win2D;

/// <summary>
/// The image half of a tile: what a <see cref="Win2dCanvas"/> drew on the canvas <see cref="Win2dCanvas.CreateTile"/> returned with it,
/// recorded as a vector command list. The list belongs to this image, which releases it (and the canvas that fills it, if that canvas is
/// still open) on <see cref="Dispose"/>.
/// </summary>
internal sealed class Win2dTileImage : PeachDrawing.Core.Image
{
    private readonly Win2dCanvas _canvas;
    private readonly CanvasCommandList _list;
    private readonly Win2dResourceCounts _counts;
    private bool _disposed;

    public Win2dTileImage(double width, double height, Win2dCanvas canvas, CanvasCommandList list, Win2dResourceCounts counts)
    {
        Width = width;
        Height = height;
        _canvas = canvas;
        _list = list;
        _counts = counts;
    }

    public override double Width { get; }

    public override double Height { get; }

    public override bool Interpolate { get; set; } = true;

    /// <summary>The recorded content, ready to draw: the canvas that filled it is closed first, which a command list needs.</summary>
    public CanvasCommandList Content
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _canvas.Dispose();
            return _list;
        }
    }

    public override void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _canvas.Dispose();
        _list.Dispose();
        _counts.CommandList(-1);
    }
}
