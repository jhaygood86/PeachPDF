namespace PeachDrawing.Core;

/// <summary>
/// The pointer a surface that shows the drawing to someone with a pointing device should display over an area of it: the shapes a
/// document can ask for, in the vocabulary the CSS <c>cursor</c> property uses.
/// </summary>
public enum CursorShape
{
    /// <summary>The ordinary arrow.</summary>
    Default,
    /// <summary>No pointer at all.</summary>
    None,
    /// <summary>An arrow with a menu.</summary>
    ContextMenu,
    /// <summary>An arrow with a question mark.</summary>
    Help,
    /// <summary>A hand, for something that can be activated.</summary>
    Pointer,
    /// <summary>The arrow with a busy indicator: the program is working but can still be used.</summary>
    Progress,
    /// <summary>The busy indicator: the program cannot be used meanwhile.</summary>
    Wait,
    /// <summary>A thick plus, for a cell of a table.</summary>
    Cell,
    /// <summary>Cross hairs.</summary>
    Crosshair,
    /// <summary>The I-beam, for text that can be selected or edited.</summary>
    Text,
    /// <summary>The I-beam turned on its side, for vertical text.</summary>
    VerticalText,
    /// <summary>An arrow with a shortcut: something will be made a shortcut to.</summary>
    Alias,
    /// <summary>An arrow with a plus: something will be copied.</summary>
    Copy,
    /// <summary>Four arrows: something will be moved.</summary>
    Move,
    /// <summary>The thing under the pointer cannot be dropped here.</summary>
    NoDrop,
    /// <summary>The action is not allowed.</summary>
    NotAllowed,
    /// <summary>An open hand: something can be grabbed.</summary>
    Grab,
    /// <summary>A closed hand: something is being dragged.</summary>
    Grabbing,
    /// <summary>An edge can be moved towards the east.</summary>
    ResizeEast,
    /// <summary>An edge can be moved towards the north.</summary>
    ResizeNorth,
    /// <summary>A corner can be moved towards the north east.</summary>
    ResizeNorthEast,
    /// <summary>A corner can be moved towards the north west.</summary>
    ResizeNorthWest,
    /// <summary>An edge can be moved towards the south.</summary>
    ResizeSouth,
    /// <summary>A corner can be moved towards the south east.</summary>
    ResizeSouthEast,
    /// <summary>A corner can be moved towards the south west.</summary>
    ResizeSouthWest,
    /// <summary>An edge can be moved towards the west.</summary>
    ResizeWest,
    /// <summary>Something can be resized sideways (either east or west).</summary>
    ResizeHorizontal,
    /// <summary>Something can be resized up and down (either north or south).</summary>
    ResizeVertical,
    /// <summary>Something can be resized along the diagonal from the north east to the south west.</summary>
    ResizeDiagonalUp,
    /// <summary>Something can be resized along the diagonal from the north west to the south east.</summary>
    ResizeDiagonalDown,
    /// <summary>A column can be resized.</summary>
    ResizeColumn,
    /// <summary>A row can be resized.</summary>
    ResizeRow,
    /// <summary>Four arrows: the content can be scrolled in any direction.</summary>
    ScrollAll,
    /// <summary>A magnifying glass with a plus.</summary>
    ZoomIn,
    /// <summary>A magnifying glass with a minus.</summary>
    ZoomOut
}

/// <summary>An image a document asks to be used as the pointer, ahead of the <see cref="CursorShape"/> that stands in for it.</summary>
/// <param name="Uri">the address of the image, resolved against the document's base</param>
/// <param name="HotspotX">the distance of the pointer's active point from the left of the image in image pixels, or <see langword="null"/> to use the one the image file carries (the centre when it has none)</param>
/// <param name="HotspotY">the distance of the active point from the top of the image in image pixels, or <see langword="null"/></param>
public sealed record CursorImage(string Uri, double? HotspotX, double? HotspotY);
