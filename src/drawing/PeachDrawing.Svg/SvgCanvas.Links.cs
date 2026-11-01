using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace PeachDrawing.Svg;

public sealed partial class SvgCanvas
{
    // The links and destinations are kept until the document is written and then go after everything that was drawn, outside every group, so
    // they lie on top (what a pointer reaches first) and in the document's own coordinates whatever was pushed when they were added.
    private readonly List<string> _links = [];
    private readonly HashSet<string> _destinations = new(StringComparer.Ordinal);

    // Pointer areas lie under the links: a link's area is reached first (and the browser shows its own pointer for it), so one that a cursor
    // area happens to cover is still clickable.
    private readonly List<string> _cursors = [];

    /// <inheritdoc/>
    public override object? AddLink(Rect bounds, string uri, IReadOnlyList<PaintPoint>? quad = null)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return Link(SvgDefs.Escape(uri), bounds, quad);
    }

    /// <inheritdoc/>
    public override object? AddDestinationLink(Rect bounds, string destinationName, IReadOnlyList<PaintPoint>? quad = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(destinationName);
        return Link("#" + SvgDefs.Escape(Uri.EscapeDataString(AnchorId(destinationName))), bounds, quad);
    }

    /// <inheritdoc/>
    public override void AddCursor(Rect bounds, CursorShape shape, IReadOnlyList<CursorImage>? images = null)
    {
        if (_isTile || _completed is not null)
            return;

        var value = new StringBuilder();
        if (images is not null)
        {
            foreach (var image in images)
            {
                value.Append("url('").Append(image.Uri.Replace("\\", "\\\\").Replace("'", "\\'")).Append("')");
                if (image.HotspotX is { } x && image.HotspotY is { } y)
                    value.Append(' ').Append(N(x)).Append(' ').Append(N(y));
                value.Append(", ");
            }
        }

        value.Append(CssKeyword(shape));
        _cursors.Add($"<rect x=\"{N(bounds.X)}\" y=\"{N(bounds.Y)}\" width=\"{N(bounds.Width)}\" height=\"{N(bounds.Height)}\" fill=\"none\" pointer-events=\"all\" style=\"cursor:{SvgDefs.Escape(value.ToString())}\"/>");
    }

    private static string CssKeyword(CursorShape shape) => shape switch
    {
        CursorShape.None => "none",
        CursorShape.ContextMenu => "context-menu",
        CursorShape.Help => "help",
        CursorShape.Pointer => "pointer",
        CursorShape.Progress => "progress",
        CursorShape.Wait => "wait",
        CursorShape.Cell => "cell",
        CursorShape.Crosshair => "crosshair",
        CursorShape.Text => "text",
        CursorShape.VerticalText => "vertical-text",
        CursorShape.Alias => "alias",
        CursorShape.Copy => "copy",
        CursorShape.Move => "move",
        CursorShape.NoDrop => "no-drop",
        CursorShape.NotAllowed => "not-allowed",
        CursorShape.Grab => "grab",
        CursorShape.Grabbing => "grabbing",
        CursorShape.ResizeEast => "e-resize",
        CursorShape.ResizeNorth => "n-resize",
        CursorShape.ResizeNorthEast => "ne-resize",
        CursorShape.ResizeNorthWest => "nw-resize",
        CursorShape.ResizeSouth => "s-resize",
        CursorShape.ResizeSouthEast => "se-resize",
        CursorShape.ResizeSouthWest => "sw-resize",
        CursorShape.ResizeWest => "w-resize",
        CursorShape.ResizeHorizontal => "ew-resize",
        CursorShape.ResizeVertical => "ns-resize",
        CursorShape.ResizeDiagonalUp => "nesw-resize",
        CursorShape.ResizeDiagonalDown => "nwse-resize",
        CursorShape.ResizeColumn => "col-resize",
        CursorShape.ResizeRow => "row-resize",
        CursorShape.ScrollAll => "all-scroll",
        CursorShape.ZoomIn => "zoom-in",
        CursorShape.ZoomOut => "zoom-out",
        _ => "default",
    };

    /// <inheritdoc/>
    public override void AddDestination(string name, double top)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (_isTile || _completed is not null || !_destinations.Add(name))
            return;

        // An element with no size at the named height: where an address with this fragment scrolls to.
        _links.Add($"<rect id=\"{SvgDefs.Escape(AnchorId(name))}\" x=\"0\" y=\"{N(top)}\" width=\"0\" height=\"0\" fill=\"none\"/>");
    }

    /// <summary>The id a destination is written under: prefixed, so a name from a document can never collide with the ids this canvas writes.</summary>
    private static string AnchorId(string name) => "anchor-" + name;

    private object? Link(string href, Rect bounds, IReadOnlyList<PaintPoint>? quad)
    {
        if (_isTile || _completed is not null)
            return null;

        // Painted with nothing, yet reached by a pointer: it covers what is under it without changing how it looks.
        const string clickable = "fill=\"none\" pointer-events=\"all\"";
        string shape;
        if (quad is { Count: 4 })
        {
            // The corners come as top-left, top-right, bottom-left, bottom-right; a polygon goes round.
            var order = new[] { quad[0], quad[1], quad[3], quad[2] };
            var points = new StringBuilder();
            foreach (var corner in order)
            {
                if (points.Length > 0)
                    points.Append(' ');
                points.Append(N(corner.X)).Append(',').Append(N(corner.Y));
            }

            shape = $"<polygon points=\"{points}\" {clickable}/>";
        }
        else
        {
            shape = $"<rect x=\"{N(bounds.X)}\" y=\"{N(bounds.Y)}\" width=\"{N(bounds.Width)}\" height=\"{N(bounds.Height)}\" {clickable}/>";
        }

        _links.Add($"<a href=\"{href}\">{shape}</a>");
        return null;
    }

    private void AppendLinks()
    {
        foreach (var cursor in _cursors)
            _body.Append(cursor).Append('\n');

        foreach (var link in _links)
            _body.Append(link).Append('\n');
    }
}
