namespace PeachPDF
{
    /// <summary>
    /// How an embedded file (<see cref="PdfAttachment"/>) relates to the PDF document it is embedded in -
    /// the PDF <c>/AFRelationship</c> entry (ISO 19005-3, ISO 32000-2 §14.13).
    /// </summary>
    /// <remarks>
    /// There are no technical consequences inside the PDF file from the value chosen; it tells a reader
    /// how to understand the role of the embedded data.
    /// </remarks>
    public enum PdfAttachmentRelationship
    {
        /// <summary>
        /// None of the other relationships applies, or it is unknown. Default.
        /// </summary>
        Unspecified = 0,

        /// <summary>
        /// The file is the source the visual representation was derived from - for example the XML a PDF
        /// was produced from by a transformation.
        /// </summary>
        Source,

        /// <summary>
        /// The file holds data used for the visual representation - for example the numbers behind a table
        /// or chart. It may hold less than the PDF shows.
        /// </summary>
        Data,

        /// <summary>
        /// The file is an alternative representation of the PDF's content: both carry the same information
        /// and either can stand in for the other.
        /// </summary>
        Alternative,

        /// <summary>
        /// The file is neither the source nor an alternative, but adds information - for example
        /// supporting documents.
        /// </summary>
        Supplement,
    }
}
