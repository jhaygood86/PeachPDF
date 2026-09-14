using System;

namespace PeachPDF.Layout
{
    /// <summary>
    /// Builds a declarative document page by page, for <see cref="PdfGenerator.CreateDocument"/>/
    /// <see cref="PdfGenerator.AddPages"/>. This builder constructs PeachPDF's own internal box tree
    /// directly - it never generates or parses HTML/CSS text from caller content.
    /// </summary>
    public interface IDocumentBuilder
    {
        /// <summary>
        /// Appends one more paginated page-section to the document. Callable more than once per
        /// document; each call's content flows and paginates independently of the others.
        /// </summary>
        void Page(Action<IPageDescriptor> handler);
    }
}
