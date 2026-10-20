using System.IO;

namespace PeachPDF.Layout
{
    /// <summary>
    /// Configures a PDF file whose pages are stamped over (<see cref="IDocumentBuilder.OverlayFile"/>) or under
    /// (<see cref="IDocumentBuilder.UnderlayFile"/>) the document's pages: a letterhead, a watermark, a form background.
    /// </summary>
    /// <remarks>
    /// Page numbers are one-based, and a range is a comma-separated list of pages and spans: <c>"1"</c>, <c>"1-3,5"</c>,
    /// <c>"2-z"</c>, where <c>z</c> is the last page. A span written backwards (<c>"z-1"</c>) runs backwards.
    /// </remarks>
    public interface IFileLayerDescriptor
    {
        /// <summary>Takes the layer's pages from the PDF file at <paramref name="path"/>, read when the document is built.</summary>
        IFileLayerDescriptor File(string path);

        /// <summary>Takes the layer's pages from the bytes of a PDF file.</summary>
        IFileLayerDescriptor File(byte[] pdf);

        /// <summary>Takes the layer's pages from a PDF file read from <paramref name="pdf"/>, which is read to the end when the document is built and not closed.</summary>
        IFileLayerDescriptor File(Stream pdf);

        /// <summary>
        /// The pages of the finished document that receive the layer. Unset means every page. The pages this names take the
        /// layer's pages in order: the first target page the first source page, the second the second, and so on.
        /// </summary>
        IFileLayerDescriptor TargetPages(string range);

        /// <summary>The pages of the PDF file to use, in the order they are given to the target pages. Unset means every page of the file.</summary>
        IFileLayerDescriptor SourcePages(string range);

        /// <summary>
        /// The pages of the PDF file to cycle through once <see cref="SourcePages"/> is used up. Unset means the layer stops there and
        /// the remaining target pages get none. <c>SourcePages("1")</c> with <c>RepeatSourcePages("1")</c> puts the file's first
        /// page on every target page.
        /// </summary>
        IFileLayerDescriptor RepeatSourcePages(string range);
    }
}
