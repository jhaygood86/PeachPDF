namespace PeachPDF
{
    /// <summary>
    /// The color scheme the renderer reports to the CSS <c>prefers-color-scheme</c> media feature
    /// (which drives, for example, Tailwind's <c>dark:</c> variants). A static PDF has no user/OS
    /// preference, so this is chosen by the caller via
    /// <see cref="PdfGenerateConfig.PreferredColorScheme"/>; it defaults to <see cref="Light"/>.
    /// </summary>
    public enum PdfColorScheme
    {
        /// <summary>
        /// Report <c>prefers-color-scheme: light</c> (the default). <c>@media (prefers-color-scheme: dark)</c>
        /// blocks do not apply.
        /// </summary>
        Light,

        /// <summary>
        /// Report <c>prefers-color-scheme: dark</c>, so the document's dark-mode styles render.
        /// </summary>
        Dark
    }
}
