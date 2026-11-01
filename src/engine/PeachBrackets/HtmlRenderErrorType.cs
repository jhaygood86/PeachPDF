namespace PeachPDF
{
    /// <summary>
    /// The pipeline phase a <see cref="HtmlRenderException"/> was raised from. Set on <see cref="HtmlRenderException.RenderErrorType"/>.
    /// </summary>
    public enum HtmlRenderErrorType
    {
        /// <summary>
        /// An error that doesn't fit one of the more specific categories below.
        /// </summary>
        General = 0,

        /// <summary>
        /// The stylesheet (author, inline, or <c>@import</c>ed) could not be parsed or loaded.
        /// </summary>
        CssParsing = 1,

        /// <summary>
        /// The HTML document could not be parsed.
        /// </summary>
        HtmlParsing = 2,

        /// <summary>
        /// An image referenced by the document (<c>&lt;img&gt;</c>, <c>background-image</c>, <c>list-style-image</c>) could not be loaded or decoded.
        /// </summary>
        Image = 3,

        /// <summary>
        /// An error occurred while painting a box to the PDF page.
        /// </summary>
        Paint = 4,

        /// <summary>
        /// An error occurred while computing layout (position/size) for a box.
        /// </summary>
        Layout = 5,

        /// <summary>
        /// Reserved; not currently raised by PeachPDF. Inherited from this engine's interactive-viewer origins, where it covered keyboard/mouse input handling.
        /// </summary>
        KeyboardMouse = 6,

        /// <summary>
        /// Reserved; not currently raised by PeachPDF. Inherited from this engine's interactive-viewer origins, where it covered <c>&lt;iframe&gt;</c> navigation.
        /// </summary>
        Iframe = 7,

        /// <summary>
        /// Reserved; not currently raised by PeachPDF. Inherited from this engine's interactive-viewer origins, where it covered right-click context menu handling.
        /// </summary>
        ContextMenu = 8,
    }
}
