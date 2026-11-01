using PeachBrackets.Html.Dom;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;

namespace PeachBrackets.Updates;

/// <summary>
/// An element of a loaded document, as a handle to restyle it with <see cref="HtmlDocument.SetStyle"/>. Get one from
/// <see cref="HtmlDocument.GetElement"/> or <see cref="HtmlDocument.GetElementAt(int, double, double)"/>.
/// </summary>
public sealed class DocumentElement
{
    internal DocumentElement(CssBox box) => Box = box;

    internal CssBox Box { get; }

    /// <summary>The element's tag name, lower case (<c>p</c>, <c>span</c>).</summary>
    public string TagName => Box.HtmlTag?.Name ?? string.Empty;

    /// <summary>The element's <c>id</c>, or null if it has none.</summary>
    public string? Id => Box.HtmlTag?.TryGetAttribute("id") is { Length: > 0 } id ? id : null;

    /// <summary>The element a handle is for is the same one when the underlying element is.</summary>
    public override bool Equals(object? obj) => obj is DocumentElement other && ReferenceEquals(other.Box, Box);

    /// <inheritdoc/>
    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Box);
}

/// <summary>A rectangle of one page, in points from the top-left of the sheet, that has to be painted again.</summary>
/// <param name="PageIndex">the zero-based index of the page</param>
/// <param name="Bounds">the area to repaint</param>
public readonly record struct DirtyRegion(int PageIndex, Rect Bounds);

/// <summary>What an edit of the document's text did (<see cref="HtmlDocument.ReplaceTextAsync"/>).</summary>
/// <param name="Applied">whether the change was made; when false the document is exactly as it was</param>
/// <param name="Reason">why it was not applied, or null when it was</param>
/// <param name="PagesBefore">how many pages the document had before</param>
/// <param name="PagesAfter">how many it has now</param>
/// <param name="FirstDirtyPage">the first page whose text or its position changed - every page from it on may look different - or null when nothing moved</param>
/// <param name="DirtyRegions">the pages to repaint: each page from <paramref name="FirstDirtyPage"/> on, whole</param>
/// <param name="Caret">where a caret just after the inserted text is, in the document's new <see cref="HtmlDocument.TextRuns"/>, or null when no text is left to put it in</param>
/// <param name="Block">the paragraph a split created (<see cref="HtmlDocument.SplitParagraphAsync"/>), which has no text positions while it is empty; null for other edits</param>
/// <param name="ResumedFromPage">the page layout was picked up from - every earlier page was kept as it was and was not laid out again - or null when the whole document was laid out again</param>
public readonly record struct TextEditResult(
    bool Applied, string? Reason, int PagesBefore, int PagesAfter, int? FirstDirtyPage, IReadOnlyList<DirtyRegion> DirtyRegions, Text.TextPosition? Caret,
    DocumentElement? Block = null, int? ResumedFromPage = null)
{
    internal static TextEditResult NotApplied(string reason, int pages) => new(false, reason, pages, pages, null, [], null);
}

/// <summary>What <see cref="HtmlDocument.SetStyle"/> did.</summary>
/// <param name="Applied">whether the change was made; when false the document is exactly as it was</param>
/// <param name="Reason">why it was not applied, or null when it was</param>
/// <param name="DirtyRegions">the parts of the pages that now look different, to repaint (see <see cref="HtmlDocument.PaintPageRegionAsync"/>); empty when nothing was applied</param>
public readonly record struct UpdateResult(bool Applied, string? Reason, IReadOnlyList<DirtyRegion> DirtyRegions)
{
    internal static UpdateResult NotApplied(string reason) => new(false, reason, []);
}
