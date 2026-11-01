using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachDrawing.Win2D;

public sealed partial class Win2dCanvas
{
    // A link is not part of a picture of pixels; it is recorded for the host to hit-test. Only the page canvas records them: a tile or a
    // layer is composited into the page later, in coordinates that no longer mean anything to a pointer.

    /// <inheritdoc />
    public override object? AddLink(Rect bounds, string uri, IReadOnlyList<PaintPoint>? quad = null)
    {
        Guard();
        ArgumentNullException.ThrowIfNull(uri);
        if (!_isTile)
            Links.Add(new Win2dLink(bounds, quad is { Count: 4 } ? quad.ToArray() : null, uri, null));

        return null;
    }

    /// <inheritdoc />
    public override object? AddDestinationLink(Rect bounds, string destinationName, IReadOnlyList<PaintPoint>? quad = null)
    {
        Guard();
        ArgumentException.ThrowIfNullOrEmpty(destinationName);
        if (!_isTile)
            Links.Add(new Win2dLink(bounds, quad is { Count: 4 } ? quad.ToArray() : null, null, destinationName));

        return null;
    }

    /// <inheritdoc />
    public override void AddCursor(Rect bounds, CursorShape shape, IReadOnlyList<CursorImage>? images = null)
    {
        Guard();
        if (!_isTile)
            Links.AddCursor(new Win2dCursorArea(bounds, shape, images ?? []));
    }

    /// <inheritdoc />
    public override void AddDestination(string name, double top)
    {
        Guard();
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (!_isTile)
            Links.AddDestination(name, top);
    }
}
