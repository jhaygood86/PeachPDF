// An internal mirror of PeachPDF's public option type of the same name: PeachPDF maps its PdfGenerateConfig onto these.
#nullable enable

namespace PeachDrawing.PDF.Engine
{
    /// <summary>
    /// Specifies the orientation of a page.
    /// </summary>
    internal enum PageOrientation
    {
        /// <summary>
        /// The default page orientation.
        /// </summary>
        Portrait,

        /// <summary>
        /// The width and height of the page are reversed.
        /// </summary>
        Landscape,
    }
}
