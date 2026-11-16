namespace PeachBrackets;

/// <summary>
/// The syntax a document's source is parsed with, which a browser decides from the MIME type the document is served as.
/// </summary>
public enum DocumentSyntax
{
    /// <summary>HTML, parsed as the HTML Standard says: tags that are left out are implied, markup that is wrong is repaired, and a DOCTYPE decides the <see cref="DocumentMode"/>.</summary>
    Html,

    /// <summary>
    /// XHTML, parsed as XML: the document must be well-formed (case matters: <c>&lt;p&gt;&lt;/P&gt;</c> is an error), an element is empty only when its markup says so, a CDATA
    /// section is text, and the named character entities of XHTML (<c>&amp;nbsp;</c>) are known. The document is never in quirks mode. A document
    /// that is not well-formed, or whose document element is not <c>html</c>, throws <c>HtmlRenderException</c>.
    /// </summary>
    Xml
}
