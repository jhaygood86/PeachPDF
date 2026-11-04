namespace PeachBrackets;

/// <summary>
/// The kind of medium a document is laid out for. It decides which <c>@media</c> rules apply and how the document is divided: paged for
/// <see cref="Print"/>, one continuous surface for <see cref="Screen"/>.
/// </summary>
public enum MediaType
{
    /// <summary>
    /// A paged medium. The document is broken into pages of <see cref="HtmlDocumentOptions.PageWidthPoints"/> by
    /// <see cref="HtmlDocumentOptions.PageHeightPoints"/>, <c>@page</c> rules and page margin boxes apply, and <c>@media print</c> rules match.
    /// </summary>
    Print = 0,

    /// <summary>
    /// A continuous medium, as on a display. The document has no pages: it is laid out as one surface as wide as
    /// <see cref="HtmlDocumentOptions.PageWidthPoints"/> and as tall as its content (at least <see cref="HtmlDocumentOptions.ViewportHeightPoints"/>),
    /// <c>@page</c> rules and page breaks do not apply, and <c>@media screen</c> rules match.
    /// </summary>
    Screen = 1,
}
