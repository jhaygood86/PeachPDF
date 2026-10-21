#nullable enable

using PeachDrawing.Core;

namespace PeachDrawing.PDF
{
    /// <summary>A place in an opened PDF that a link or an outline entry leads to.</summary>
    /// <param name="PageIndex">the zero-based page of the file</param>
    /// <param name="TopPoints">how far below the top edge of the page, as it is shown, the destination is, in points</param>
    public sealed record PdfSourceDestination(int PageIndex, double TopPoints);

    /// <summary>A link on a page of an opened PDF: a rectangle that opens a web address or leads to another place in the file.</summary>
    /// <param name="Bounds">the clickable rectangle, in points from the top-left corner of the page as it is shown</param>
    /// <param name="Uri">the address it opens, or <see langword="null"/> for a link that leads somewhere in the file</param>
    /// <param name="Destination">where it leads in the file, or <see langword="null"/> for a link that opens an address</param>
    public sealed record PdfSourceLink(Rect Bounds, string? Uri, PdfSourceDestination? Destination);

    /// <summary>The document information of an opened PDF; a field the file does not give is <see langword="null"/>.</summary>
    public sealed record PdfSourceMetadata(string? Title, string? Author, string? Subject, string? Keywords, string? Creator, string? Producer);
}
