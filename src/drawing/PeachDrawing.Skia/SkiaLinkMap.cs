using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PeachDrawing.Skia;

/// <summary>
/// A link a document drew: an area of the canvas that leads to an address or to a named place in the same document.
/// </summary>
/// <param name="Bounds">the clickable area, in the canvas's layout units</param>
/// <param name="Quad">the four corners (top-left, top-right, bottom-left, bottom-right) when the area is not <paramref name="Bounds"/> itself, as for a rotated element; otherwise <see langword="null"/></param>
/// <param name="Uri">the address the link leads to, or <see langword="null"/> for a link to a place in the document</param>
/// <param name="Destination">the name of the place in the document the link leads to, or <see langword="null"/> for a link to an address</param>
public sealed record SkiaLink(Rect Bounds, IReadOnlyList<PaintPoint>? Quad, string? Uri, string? Destination)
{
    /// <summary>Whether <paramref name="x"/>, <paramref name="y"/> (layout units) is inside the clickable area.</summary>
    /// <param name="x">the horizontal position</param>
    /// <param name="y">the vertical position</param>
    public bool Contains(double x, double y)
    {
        if (Quad is not { Count: 4 } quad)
            return Bounds.Contains(x, y);

        // The corners come as top-left, top-right, bottom-left, bottom-right; going round the shape is 0, 1, 3, 2.
        PaintPoint[] ring = [quad[0], quad[1], quad[3], quad[2]];
        var inside = false;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
        {
            var a = ring[i];
            var b = ring[j];
            if ((a.Y > y) != (b.Y > y) && x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }

        return inside;
    }
}

/// <summary>Opens an address on the host's behalf: implement it with whatever the platform offers for starting a handler for a URI.</summary>
public interface ISkiaLinkLauncher
{
    /// <summary>Opens <paramref name="uri"/> with whatever is registered to handle it.</summary>
    /// <param name="uri">the address to open</param>
    /// <returns><see langword="true"/> when something was started</returns>
    Task<bool> LaunchAsync(Uri uri);
}

/// <summary>What <see cref="SkiaLinkMap"/> may open, and how.</summary>
public sealed class SkiaLinkOptions
{
    /// <summary>
    /// The schemes a link may lead to. A document is untrusted content, and an address handler can do anything (open a file, start a
    /// program), so only these are launched; a link to any other scheme is refused. The default is <c>http</c>, <c>https</c>, <c>mailto</c>
    /// and <c>tel</c>.
    /// </summary>
    public ISet<string> AllowedSchemes { get; init; } = new HashSet<string>(new[] { "http", "https", "mailto", "tel" }, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// What opens an address. Skia is not tied to a platform, so there is no default: with <see langword="null"/> no address is opened and the host
    /// follows the link itself from <see cref="SkiaLinkMap.LinkActivating"/> (on Android, by starting an intent for the address).
    /// </summary>
    public ISkiaLinkLauncher? Launcher { get; init; }
}

/// <summary>Data for <see cref="SkiaLinkMap.LinkActivating"/>.</summary>
public sealed class SkiaLinkActivatingEventArgs : EventArgs
{
    internal SkiaLinkActivatingEventArgs(SkiaLink link) => Link = link;

    /// <summary>The link being activated.</summary>
    public SkiaLink Link { get; }

    /// <summary>Set to <see langword="true"/> to stop the link being followed, for example because the host handles it itself.</summary>
    public bool Cancel { get; set; }
}

/// <summary>An area of the canvas the document asked to show a pointer of its own over.</summary>
/// <param name="Bounds">the area, in the canvas's layout units</param>
/// <param name="Shape">the pointer to show, which stands in when none of <paramref name="Images"/> can be used</param>
/// <param name="Images">images to use as the pointer in order of preference; empty when there are none</param>
public sealed record SkiaCursorArea(Rect Bounds, CursorShape Shape, IReadOnlyList<CursorImage> Images);

/// <summary>
/// The links and named places a <see cref="SkiaCanvas"/> was told about while a document was painted. A Skia canvas draws pixels, so a
/// link is not part of the picture: the host asks this map what is under the pointer and, when the user clicks, activates it.
/// </summary>
/// <remarks>
/// Positions are in the canvas's layout units. A host that has a pointer position in the coordinates of its own <c>SKCanvas</c> - the pixels of
/// the surface, before the matrix the host set on it (a DPI scale, a zoom) - uses <see cref="HitTestDevice"/> instead. The map holds no native resources and outlives its canvas,
/// so a host can hit-test after the painting callback has returned. Nothing is ever launched while a document is painted, only when <see cref="TryActivateAsync"/> is called.
/// </remarks>
public sealed class SkiaLinkMap
{
    private readonly List<SkiaLink> _links = [];
    private readonly Dictionary<string, double> _destinations = new(StringComparer.Ordinal);
    private readonly SkiaLinkOptions _options;
    private readonly double _unitsPerDevicePixel;

    internal SkiaLinkMap(SkiaLinkOptions? options, double unitsPerDevicePixel)
    {
        _options = options ?? new SkiaLinkOptions();
        _unitsPerDevicePixel = unitsPerDevicePixel > 0 ? unitsPerDevicePixel : 1;
    }

    /// <summary>The links, in the order they were added; a later link is on top.</summary>
    public IReadOnlyList<SkiaLink> Links => _links;

    /// <summary>The named places, and how far below the top of the page each is, in layout units.</summary>
    public IReadOnlyDictionary<string, double> Destinations => _destinations;

    /// <summary>Raised before a link is followed. Set <see cref="SkiaLinkActivatingEventArgs.Cancel"/> to handle it yourself.</summary>
    public event EventHandler<SkiaLinkActivatingEventArgs>? LinkActivating;

    private readonly List<SkiaCursorArea> _cursors = [];

    /// <summary>The areas that ask for a pointer of their own, in the order they were added; a later one is on top.</summary>
    public IReadOnlyList<SkiaCursorArea> Cursors => _cursors;

    internal void AddCursor(SkiaCursorArea area) => _cursors.Add(area);

    /// <summary>The topmost area asking for a pointer at <paramref name="x"/>, <paramref name="y"/> in layout units, or <see langword="null"/> when the document asks for none there (the host shows its own).</summary>
    /// <param name="x">the horizontal position</param>
    /// <param name="y">the vertical position</param>
    public SkiaCursorArea? CursorAt(double x, double y)
    {
        for (var i = _cursors.Count - 1; i >= 0; i--)
        {
            if (_cursors[i].Bounds.Contains(x, y))
                return _cursors[i];
        }

        return null;
    }

    /// <summary>Like <see cref="CursorAt"/>, for a position in the host's own pointer coordinates (see <see cref="HitTestDevice"/>).</summary>
    /// <param name="xDevice">the horizontal position</param>
    /// <param name="yDevice">the vertical position</param>
    public SkiaCursorArea? CursorAtDevice(double xDevice, double yDevice) => CursorAt(xDevice * _unitsPerDevicePixel, yDevice * _unitsPerDevicePixel);

    internal void Add(SkiaLink link) => _links.Add(link);

    internal void AddDestination(string name, double top) => _destinations.TryAdd(name, top);

    /// <summary>The topmost link at <paramref name="x"/>, <paramref name="y"/> in layout units, or <see langword="null"/>.</summary>
    /// <param name="x">the horizontal position</param>
    /// <param name="y">the vertical position</param>
    public SkiaLink? HitTest(double x, double y)
    {
        for (var i = _links.Count - 1; i >= 0; i--)
        {
            if (_links[i].Contains(x, y))
                return _links[i];
        }

        return null;
    }

    /// <summary>Like <see cref="HitTest"/>, for a position in the coordinates of the host's own <c>SKCanvas</c> (a pointer position), which the matrix the host had set on it maps onto layout units.</summary>
    /// <param name="xDevice">the horizontal position</param>
    /// <param name="yDevice">the vertical position</param>
    public SkiaLink? HitTestDevice(double xDevice, double yDevice) => HitTest(xDevice * _unitsPerDevicePixel, yDevice * _unitsPerDevicePixel);

    /// <summary>How far below the top of the page the place called <paramref name="name"/> is, in layout units.</summary>
    /// <param name="name">the destination's name</param>
    /// <param name="top">its distance from the top</param>
    /// <returns><see langword="true"/> when there is such a place</returns>
    public bool TryGetDestination(string name, out double top) => _destinations.TryGetValue(name, out top);

    /// <summary>
    /// Follows <paramref name="link"/>: an address is opened with <see cref="SkiaLinkOptions.Launcher"/> (if there is one, the scheme is allowed and the
    /// event is not cancelled); a link to a place in the document only raises <see cref="LinkActivating"/> and leaves the scrolling to the host.
    /// </summary>
    /// <param name="link">the link to follow</param>
    /// <returns><see langword="true"/> when an address was opened; never throws for a refused, cancelled or failed launch</returns>
    public async Task<bool> ActivateAsync(SkiaLink link)
    {
        ArgumentNullException.ThrowIfNull(link);

        var args = new SkiaLinkActivatingEventArgs(link);
        LinkActivating?.Invoke(this, args);
        if (args.Cancel || link.Uri is null)
            return false;

        // Only an absolute address of an allowed scheme: a relative one has nowhere to go, and any other scheme is an unknown handler.
        if (!Uri.TryCreate(link.Uri, UriKind.Absolute, out var uri) || !_options.AllowedSchemes.Contains(uri.Scheme))
            return false;

        if (_options.Launcher is not { } launcher)
            return false;

        try
        {
            return await launcher.LaunchAsync(uri);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Follows the topmost link at the position, if there is one (layout units).</summary>
    /// <param name="x">the horizontal position</param>
    /// <param name="y">the vertical position</param>
    /// <returns><see langword="true"/> when an address was opened</returns>
    public async Task<bool> TryActivateAsync(double x, double y) =>
        HitTest(x, y) is { } link && await ActivateAsync(link);
}
