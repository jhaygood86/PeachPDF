namespace PeachPDF
{
    /// <summary>
    /// What to do when a document targeting a conformance level that forbids transparency (PDF/A-1, PDF/X-1a, PDF/X-3) uses a
    /// construct that needs it: an <c>opacity</c> below 1, a semi-transparent colour or gradient, an image with an alpha channel,
    /// a blend mode, an SVG mask or filter, and so on. See <see cref="PdfGenerateConfig.TransparencyPolicy"/>.
    /// </summary>
    public enum TransparencyPolicy
    {
        /// <summary>Reject the document with an <see cref="System.InvalidOperationException"/> naming the construct. The default.</summary>
        Reject = 0,

        /// <summary>
        /// Flatten it: the part of the page the construct touches is rendered into an opaque bitmap at
        /// <see cref="PdfGenerateConfig.RasterizationDpi"/> and embedded in its place, so the file contains no transparency at all. The text
        /// of a flattened region stays selectable. Content that is not touched stays vector. A construct that cannot be flattened (one under
        /// a CSS <c>transform</c>) is still rejected.
        /// </summary>
        Flatten = 1,
    }
}
