namespace PeachPDF.Html.Core
{
    /// <summary>The colour scheme a document reports to <c>prefers-color-scheme</c> media queries.</summary>
    internal enum ColorSchemePreference
    {
        /// <summary>Report <c>prefers-color-scheme: light</c> (the default).</summary>
        Light,

        /// <summary>Report <c>prefers-color-scheme: dark</c>.</summary>
        Dark,
    }
}
