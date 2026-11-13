namespace PeachBrackets.WinUI;

/// <summary>How an <see cref="HtmlView"/> relates to scrolling: whether it scrolls the document itself or is a block inside something that does.</summary>
public enum HtmlViewScrollMode
{
    /// <summary>
    /// The view scrolls and zooms the document itself, with the scroll bars, wheel, touch panning and pinch zoom of a viewer. This is what a view
    /// that fills a window or a pane wants.
    /// </summary>
    Auto,

    /// <summary>
    /// The view is a block in other layout, such as a row of a list: it is as tall as its document at the width it is given
    /// (<see cref="HtmlView.ContentHeight"/>), paints the document from its top-left corner with nothing around it, has no scroll bars, and leaves the
    /// wheel, touch panning and zoom to whatever scrolls it. Selection, links, the cursor, hover and accessibility work as in <see cref="Auto"/>.
    /// </summary>
    Disabled,
}
