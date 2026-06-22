namespace PeachPDF
{
    /// <summary>
    /// Enum of possible error types that can be reported.
    /// </summary>
    public enum HtmlRenderErrorType
    {
        General = 0,
        CssParsing = 1,
        HtmlParsing = 2,
        Image = 3,
        Paint = 4,
        Layout = 5,
        KeyboardMouse = 6,
        Iframe = 7,
        ContextMenu = 8,
    }
}
