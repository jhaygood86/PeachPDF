namespace PeachPDF.Layout
{
    /// <summary>A list's marker position, mapping directly onto <c>list-style-position</c>.</summary>
    public enum PdfListMarkerPosition
    {
        /// <summary>The marker sits outside the item's own content box, in its own hanging indent (the CSS default).</summary>
        Outside,
        /// <summary>The marker flows as the item's first inline content.</summary>
        Inside
    }
}
