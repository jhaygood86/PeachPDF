using System;

namespace PeachPDF.Layout
{
    /// <summary>
    /// Builds a declarative document page by page, for <see cref="PdfGenerator.CreateDocument"/>/
    /// <see cref="PdfGenerator.AddPages"/>. This builder constructs PeachPDF's own internal box tree
    /// directly - it never generates or parses HTML/CSS text from caller content, except for an optional
    /// <see cref="Stylesheet"/> a caller attaches explicitly.
    /// </summary>
    public interface IDocumentBuilder
    {
        /// <summary>
        /// Appends one more paginated page-section to the document. Callable more than once per
        /// document; each call's content flows and paginates independently of the others.
        /// </summary>
        void Page(Action<IPageDescriptor> handler);

        /// <summary>
        /// Attaches a stylesheet to this document, so a caller-defined <c>.class</c>/<c>#id</c>/compound/
        /// descendant selector rule matches and applies against every container tagged via
        /// <see cref="IContainer.Class"/>/<see cref="IContainer.Id"/>/<see cref="IContainer.Tag"/> across
        /// every <see cref="Page"/> in this document, and any <c>@font-face</c>/<c>@property</c>/
        /// <c>@font-palette-values</c>/<c>@page</c> at-rule it declares takes effect too. Callable anywhere
        /// in the document-building callback, before, after, or interleaved with <see cref="Page"/> calls -
        /// <see cref="Page"/> itself only records its handler; every page's actual content tree (and this
        /// stylesheet's effect) is built afterward, once the whole callback has finished. Calling this more
        /// than once keeps only the last stylesheet; to combine several, merge them into one
        /// <see cref="PeachPdfCssContent"/> first via its own <see cref="PeachPdfCssContent.AddStyleSheet(string)"/>.
        /// <para>
        /// A bare type selector like <c>div</c>/<c>img</c>/<c>a</c> also matches this API's own internal box
        /// structure (every anonymous container is tagged <c>"div"</c> by default, unless renamed via
        /// <see cref="IContainer.Tag"/>) - prefer class/id selectors, or <see cref="IContainer.Tag"/>, for
        /// predictable targeting.
        /// </para>
        /// <para>
        /// Precedence: an explicit builder call (e.g. <see cref="IContainer.Padding"/>,
        /// <see cref="IPageDescriptor.Margin"/>/<see cref="IPageDescriptor.Size(PageSize)"/>) always outranks
        /// a non-<c>!important</c> rule in this stylesheet - the same precedence an inline <c>style=""</c>
        /// attribute would have over an author stylesheet - but a matching <c>!important</c> rule always
        /// wins, matching real CSS. A base <c>@page</c> rule's geometry only fills in what a page's own
        /// <see cref="IPageDescriptor.Margin"/>/<see cref="IPageDescriptor.Size(PageSize)"/> call left unset,
        /// ranking between an explicit call and the document's <see cref="PdfGenerateConfig"/> default.
        /// </para>
        /// </summary>
        void Stylesheet(PeachPdfCssContent content);
    }
}
