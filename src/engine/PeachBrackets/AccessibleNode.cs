using PeachDrawing.Core;

namespace PeachBrackets;

/// <summary>What a piece of a document is to someone who reads it through an assistive technology rather than by looking at it.</summary>
public enum AccessibleRole
{
    /// <summary>A block of text, such as a paragraph.</summary>
    Text,

    /// <summary>A heading; <see cref="AccessibleNode.HeadingLevel"/> says which.</summary>
    Heading,

    /// <summary>An item of a list.</summary>
    ListItem,

    /// <summary>A link, which has an address (or a place in the document) it leads to.</summary>
    Link,
}

/// <summary>
/// One thing a screen reader can land on: a block of the document's text, or a link inside one. A host that has an accessibility tree (an
/// automation peer on Windows, an accessibility node provider on Android) builds it from these.
/// </summary>
/// <param name="Role">what the node is</param>
/// <param name="HeadingLevel">1 to 6 for a <see cref="AccessibleRole.Heading"/>, otherwise 0</param>
/// <param name="Text">the text that is read: the lines of a block joined by spaces, or the words of a link</param>
/// <param name="PageIndex">the zero-based index of the page the node is on (the first of them, for a block that crosses a page break; the rest are further nodes)</param>
/// <param name="Bounds">where it is on its page, in points from the top-left of the sheet</param>
/// <param name="Uri">where a link leads: an address, or <c>#name</c> for a place in the document; <see langword="null"/> for anything but a link</param>
/// <param name="ParentIndex">for a link, the index in the list of the block that contains it; otherwise -1</param>
public sealed record AccessibleNode(AccessibleRole Role, int HeadingLevel, string Text, int PageIndex, Rect Bounds, string? Uri, int ParentIndex);
