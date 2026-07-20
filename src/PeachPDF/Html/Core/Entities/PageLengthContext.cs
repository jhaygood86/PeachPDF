namespace PeachPDF.Html.Core.Entities
{
    /// <summary>
    /// The resolution context a per-page <c>@page</c> margin length needs for relative units, captured
    /// once per parse pass by <c>DomParser.CascadeApplyPageStyles</c> in true PDF points (already
    /// divided by <c>PixelsPerPoint</c>) so base-rule and per-page margins resolve against the same
    /// snapshot: <c>PdfGenerator.SetContent</c> reassigns <c>PageSize</c> after <c>SetHtml</c>, so
    /// recomputing these bases at layout/paint time would make textually identical base and
    /// <c>:first</c> percentages resolve to different values.
    /// </summary>
    /// <param name="EmPt">Points per 1em: the root element's font size.</param>
    /// <param name="RemPt">Points per 1rem: the root element's font size.</param>
    /// <param name="HundredPercentPt">The point value equivalent to 100% - the layout page width, the
    /// same basis the base rule uses for all four margins (CSS margin percentages resolve against
    /// the inline dimension).</param>
    internal readonly record struct PageLengthContext(double EmPt, double RemPt, double HundredPercentPt);
}
