namespace PeachDrawing.PDF
{
    /// <summary>How a <see cref="PdfOperation"/> carries the pages of existing files into the output.</summary>
    public enum PdfCopyMode
    {
        /// <summary>
        /// Each page is drawn again into the new file (see <see cref="PdfPageSource.Paint(int, PeachDrawing.Core.Canvas)"/>), together with its
        /// links, form fields, annotations, destinations and outline. Fonts are embedded again as subsets, and whatever the importer cannot draw is
        /// left out and reported. The default: it works the same for every page, and layering and protecting the result work on any page.
        /// </summary>
        Redraw,

        /// <summary>
        /// The objects of each page are copied into the new file as they are: content streams, fonts, images, shadings, annotations and the form
        /// fields of the widgets on the page, so the page is the one the file had, with nothing drawn again. The structure tree, optional content,
        /// scripts and the document-level extras a page does not own are not carried. A layer can be drawn on a page copied this way only if it
        /// has no rotation, no crop box and a media box starting at the origin.
        /// </summary>
        ObjectCopy,
    }
}
