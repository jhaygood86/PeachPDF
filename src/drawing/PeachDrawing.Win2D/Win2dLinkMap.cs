using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PeachDrawing.Win2D;

/// <summary>
/// A link a document drew: an area of the canvas that leads to an address or to a named place in the same document.
/// </summary>
/// <param name="Bounds">the clickable area, in the canvas's layout units</param>
/// <param name="Quad">the four corners (top-left, top-right, bottom-left, bottom-right) when the area is not <paramref name="Bounds"/> itself, as for a rotated element; otherwise <see langword="null"/></param>
/// <param name="Uri">the address the link leads to, or <see langword="null"/> for a link to a place in the document</param>
/// <param name="Destination">the name of the place in the document the link leads to, or <see langword="null"/> for a link to an address</param>
public sealed record Win2dLink(Rect Bounds, IReadOnlyList<PaintPoint>? Quad, string? Uri, string? Destination)
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

/// <summary>Tells a <see cref="ILinkLauncher"/> to open an address; the default implementation hands it to the system's handler for the address's scheme.</summary>
public interface ILinkLauncher
{
    /// <summary>Opens <paramref name="uri"/> with whatever is registered to handle it.</summary>
    /// <param name="uri">the address to open</param>
    /// <returns><see langword="true"/> when something was started</returns>
    Task<bool> LaunchAsync(Uri uri);
}

/// <summary>What <see cref="Win2dLinkMap"/> may open, and how.</summary>
public sealed class Win2dLinkOptions
{
    /// <summary>
    /// The schemes a link may lead to. A document is untrusted content, and an address handler can do anything (open a file, start a
    /// program), so only these are launched; a link to any other scheme is refused. The default is <c>http</c>, <c>https</c>, <c>mailto</c>
    /// and <c>tel</c>.
    /// </summary>
    public ISet<string> AllowedSchemes { get; init; } = new HashSet<string>(new[] { "http", "https", "mailto", "tel" }, StringComparer.OrdinalIgnoreCase);

    /// <summary>What opens an address. <see langword="null"/> uses the system's default handler for the address.</summary>
    public ILinkLauncher? Launcher { get; init; }
}

/// <summary>Data for <see cref="Win2dLinkMap.LinkActivating"/>.</summary>
public sealed class Win2dLinkActivatingEventArgs : EventArgs
{
    internal Win2dLinkActivatingEventArgs(Win2dLink link) => Link = link;

    /// <summary>The link being activated.</summary>
    public Win2dLink Link { get; }

    /// <summary>Set to <see langword="true"/> to stop the link being followed, for example because the host handles it itself.</summary>
    public bool Cancel { get; set; }
}

/// <summary>An area of the canvas the document asked to show a pointer of its own over.</summary>
/// <param name="Bounds">the area, in the canvas's layout units</param>
/// <param name="Shape">the pointer to show, which stands in when none of <paramref name="Images"/> can be used</param>
/// <param name="Images">images to use as the pointer in order of preference; empty when there are none</param>
public sealed record Win2dCursorArea(Rect Bounds, CursorShape Shape, IReadOnlyList<CursorImage> Images);

/// <summary>
/// The links and named places a <see cref="Win2dCanvas"/> was told about while a document was painted. A Win2D canvas draws pixels, so a
/// link is not part of the picture: the host asks this map what is under the pointer and, when the user clicks, activates it.
/// </summary>
/// <remarks>
/// Positions are in the canvas's layout units. A host working in device-independent pixels divides by the canvas's
/// <see cref="Win2dCanvas.PixelsPerPoint"/> (see <see cref="HitTestDips"/>). The map holds no native resources and outlives its canvas,
/// so a host can hit-test after the painting callback has returned. Nothing is ever launched while a document is painted, only when <see cref="TryActivateAsync"/> is called.
/// </remarks>
public sealed class Win2dLinkMap
{
    private readonly List<Win2dLink> _links = [];
    private readonly Dictionary<string, double> _destinations = new(StringComparer.Ordinal);
    private readonly Win2dLinkOptions _options;
    private readonly double _unitsPerDip;

    internal Win2dLinkMap(Win2dLinkOptions? options, double unitsPerDip)
    {
        _options = options ?? new Win2dLinkOptions();
        _unitsPerDip = unitsPerDip > 0 ? unitsPerDip : 1;
    }

    /// <summary>The links, in the order they were added; a later link is on top.</summary>
    public IReadOnlyList<Win2dLink> Links => _links;

    /// <summary>The named places, and how far below the top of the page each is, in layout units.</summary>
    public IReadOnlyDictionary<string, double> Destinations => _destinations;

    /// <summary>Raised before a link is followed. Set <see cref="Win2dLinkActivatingEventArgs.Cancel"/> to handle it yourself.</summary>
    public event EventHandler<Win2dLinkActivatingEventArgs>? LinkActivating;

    private readonly List<Win2dCursorArea> _cursors = [];

    /// <summary>The areas that ask for a pointer of their own, in the order they were added; a later one is on top.</summary>
    public IReadOnlyList<Win2dCursorArea> Cursors => _cursors;

    internal void AddCursor(Win2dCursorArea area) => _cursors.Add(area);

    /// <summary>The topmost area asking for a pointer at <paramref name="x"/>, <paramref name="y"/> in layout units, or <see langword="null"/> when the document asks for none there (the host shows its own).</summary>
    /// <param name="x">the horizontal position</param>
    /// <param name="y">the vertical position</param>
    public Win2dCursorArea? CursorAt(double x, double y)
    {
        for (var i = _cursors.Count - 1; i >= 0; i--)
        {
            if (_cursors[i].Bounds.Contains(x, y))
                return _cursors[i];
        }

        return null;
    }

    /// <summary>Like <see cref="CursorAt"/>, for a position in the host's own pointer coordinates (see <see cref="HitTestDips"/>).</summary>
    /// <param name="xDips">the horizontal position</param>
    /// <param name="yDips">the vertical position</param>
    public Win2dCursorArea? CursorAtDips(double xDips, double yDips) => CursorAt(xDips * _unitsPerDip, yDips * _unitsPerDip);

    internal void Add(Win2dLink link) => _links.Add(link);

    internal void AddDestination(string name, double top) => _destinations.TryAdd(name, top);

    /// <summary>The topmost link at <paramref name="x"/>, <paramref name="y"/> in layout units, or <see langword="null"/>.</summary>
    /// <param name="x">the horizontal position</param>
    /// <param name="y">the vertical position</param>
    public Win2dLink? HitTest(double x, double y)
    {
        for (var i = _links.Count - 1; i >= 0; i--)
        {
            if (_links[i].Contains(x, y))
                return _links[i];
        }

        return null;
    }

    /// <summary>Like <see cref="HitTest"/>, for a position in device-independent pixels (a pointer position in the host's coordinates).</summary>
    /// <param name="xDips">the horizontal position</param>
    /// <param name="yDips">the vertical position</param>
    public Win2dLink? HitTestDips(double xDips, double yDips) => HitTest(xDips * _unitsPerDip, yDips * _unitsPerDip);

    /// <summary>How far below the top of the page the place called <paramref name="name"/> is, in layout units.</summary>
    /// <param name="name">the destination's name</param>
    /// <param name="top">its distance from the top</param>
    /// <returns><see langword="true"/> when there is such a place</returns>
    public bool TryGetDestination(string name, out double top) => _destinations.TryGetValue(name, out top);

    /// <summary>
    /// Follows <paramref name="link"/>: an address is opened with the system's handler for its scheme (if the scheme is allowed and the
    /// handler is not cancelled); a link to a place in the document only raises <see cref="LinkActivating"/> and leaves the scrolling to the host.
    /// </summary>
    /// <param name="link">the link to follow</param>
    /// <returns><see langword="true"/> when an address was opened; never throws for a refused, cancelled or failed launch</returns>
    public async Task<bool> ActivateAsync(Win2dLink link)
    {
        ArgumentNullException.ThrowIfNull(link);

        var args = new Win2dLinkActivatingEventArgs(link);
        LinkActivating?.Invoke(this, args);
        if (args.Cancel || link.Uri is null)
            return false;

        // Only an absolute address of an allowed scheme: a relative one has nowhere to go, and any other scheme is an unknown handler.
        if (!Uri.TryCreate(link.Uri, UriKind.Absolute, out var uri) || !_options.AllowedSchemes.Contains(uri.Scheme))
            return false;

        try
        {
            return await (_options.Launcher ?? SystemLinkLauncher.Instance).LaunchAsync(uri);
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

/// <summary>Opens an address with the system's default handler for its scheme.</summary>
internal sealed class SystemLinkLauncher : ILinkLauncher
{
    public static SystemLinkLauncher Instance { get; } = new();

    public async Task<bool> LaunchAsync(Uri uri) => await Windows.System.Launcher.LaunchUriAsync(uri);
}
