using PeachDrawing.Core;
using PeachDrawing.PDF;

namespace PeachReader.Core;

/// <summary>What a click on a link should do.</summary>
public enum LinkKind
{
    /// <summary>The point is not on a link.</summary>
    None,

    /// <summary>The link leads to another place in the document.</summary>
    Internal,

    /// <summary>The link opens an address with the system: a web page, a mail message, a telephone number.</summary>
    External,

    /// <summary>The link opens an address of a kind a document may not open (a <c>file:</c> or application scheme).</summary>
    Blocked,
}

/// <summary>The meaning of a link a reader clicked.</summary>
/// <param name="Kind">what to do</param>
/// <param name="Location">for <see cref="LinkKind.Internal"/>, where to go</param>
/// <param name="Uri">for <see cref="LinkKind.External"/> and <see cref="LinkKind.Blocked"/>, the address</param>
public readonly record struct LinkTarget(LinkKind Kind, ReaderLocation Location = default, string? Uri = null)
{
    /// <summary>The target of a point that is not on a link.</summary>
    public static LinkTarget None => default;
}

/// <summary>Finds the link under a point and decides what it may do.</summary>
public static class LinkResolver
{
    /// <summary>Whether an address may be handed to the system: only the schemes a document cannot abuse.</summary>
    /// <param name="uri">the address a link carries</param>
    /// <returns><see langword="true"/> for an absolute <c>http</c>, <c>https</c>, <c>mailto</c> or <c>tel</c> address</returns>
    public static bool IsLaunchable(string? uri) =>
        System.Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Scheme is "http" or "https" or "mailto" or "tel";

    /// <summary>What an address a document gives (a link, a table-of-contents entry) may do: open with the system, or be refused.</summary>
    /// <param name="uri">the address</param>
    /// <returns><see cref="LinkKind.External"/> for a safe scheme, <see cref="LinkKind.Blocked"/> for any other, and <see cref="LinkTarget.None"/> for no address</returns>
    public static LinkTarget ForUri(string? uri) =>
        string.IsNullOrWhiteSpace(uri) ? LinkTarget.None : new LinkTarget(IsLaunchable(uri) ? LinkKind.External : LinkKind.Blocked, Uri: uri);

    /// <summary>The link at a point of a page, if any. When links overlap the one drawn last (the last in the file) wins, as it is on top.</summary>
    /// <param name="links">the links of the page, from <see cref="PdfPageSource.GetLinks"/></param>
    /// <param name="point">a point in points from the top-left of the page</param>
    /// <returns>what the link does, or <see cref="LinkTarget.None"/></returns>
    public static LinkTarget Hit(IReadOnlyList<PdfSourceLink> links, PaintPoint point)
    {
        ArgumentNullException.ThrowIfNull(links);
        for (int i = links.Count - 1; i >= 0; i--)
        {
            var link = links[i];
            var b = link.Bounds;
            if (point.X < b.X || point.X > b.X + b.Width || point.Y < b.Y || point.Y > b.Y + b.Height) continue;
            if (link.Destination is { } destination) return new LinkTarget(LinkKind.Internal, new ReaderLocation(destination.PageIndex, destination.TopPoints));
            if (ForUri(link.Uri) is { Kind: not LinkKind.None } external) return external;
        }

        return LinkTarget.None;
    }
}
