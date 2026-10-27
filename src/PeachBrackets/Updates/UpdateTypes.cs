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

/// <summary>What <see cref="HtmlDocument.SetStyle"/> did.</summary>
/// <param name="Applied">whether the change was made; when false the document is exactly as it was</param>
/// <param name="Reason">why it was not applied, or null when it was</param>
/// <param name="DirtyRegions">the parts of the pages that now look different, to repaint (see <see cref="HtmlDocument.PaintPageRegionAsync"/>); empty when nothing was applied</param>
public readonly record struct UpdateResult(bool Applied, string? Reason, IReadOnlyList<DirtyRegion> DirtyRegions)
{
    internal static UpdateResult NotApplied(string reason) => new(false, reason, []);
}
