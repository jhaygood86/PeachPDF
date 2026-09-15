namespace PeachPDF.Layout
{
    /// <summary>A paragraph's base text direction (CSS <c>direction</c>), for bidi/RTL content.</summary>
    public enum PdfTextDirection
    {
        /// <summary>Left-to-right (the CSS default).</summary>
        Ltr,
        /// <summary>Right-to-left.</summary>
        Rtl,
        /// <summary>
        /// Detected from the container's own text, via the HTML Standard's first-strong-character
        /// algorithm (the same detection <c>&lt;bdi&gt;</c>/<c>dir="auto"</c> use on the HTML side) -
        /// resolves to <see cref="Ltr"/> if no character with a strong direction is found. Resolution is
        /// deferred until the whole page's content is built (not applied eagerly when this is set), since
        /// a decorator like <c>DefaultTextStyle</c> is typically called before the text it should scan
        /// even exists.
        /// </summary>
        Auto
    }
}
