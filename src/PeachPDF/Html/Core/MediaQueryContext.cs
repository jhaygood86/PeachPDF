#nullable enable

namespace PeachPDF.Html.Core
{
    /// <summary>
    /// The resolved "device" a CSS <c>@media</c> query is evaluated against — the media type plus the
    /// page-box geometry and the fixed print characteristics PeachPDF reports. Built once per render
    /// (<see cref="FromContainer"/>) and threaded through <see cref="CssData"/>'s rule-matching so
    /// <c>@media</c> feature conditions (<c>min-width</c>, <c>orientation</c>,
    /// <c>prefers-color-scheme</c>, …) actually gate which rules apply. See
    /// <see cref="MediaQueryMatcher"/> for the evaluation itself.
    /// </summary>
    /// <param name="MediaType">The active media type (<c>"print"</c> by default, or <c>"screen"</c>/<c>"all"</c>).</param>
    /// <param name="ViewportWidthPt">The page-box width in points, or <c>null</c> when unknown (then
    /// width/height/orientation/aspect-ratio features match permissively).</param>
    /// <param name="ViewportHeightPt">The page-box height in points, or <c>null</c> when unknown.</param>
    /// <param name="ResolutionDpi">The output resolution in dots-per-inch, for <c>resolution</c> queries.</param>
    /// <param name="PreferredColorScheme">What <c>prefers-color-scheme</c> reports.</param>
    internal readonly record struct MediaQueryContext(
        string MediaType,
        double? ViewportWidthPt,
        double? ViewportHeightPt,
        double ResolutionDpi,
        PdfColorScheme PreferredColorScheme)
    {
        /// <summary>
        /// A context carrying only the media type, with no page geometry — used by callers that have no
        /// page context (standalone SVG styling, tests). Feature conditions that need geometry match
        /// permissively, preserving the pre-#235 behavior for those callers.
        /// </summary>
        internal static MediaQueryContext TypeOnly(string? media) =>
            new(string.IsNullOrEmpty(media) ? "all" : media!, null, null, 96d, PdfColorScheme.Light);

        /// <summary>
        /// Builds the context from the container's page box and configured characteristics. During the
        /// DOM/CSS tree build the container's <see cref="HtmlContainerInt.PageSize"/> holds the full
        /// physical sheet (the page box, in points) — the correct basis for the <c>width</c>/<c>height</c>
        /// media features per Media Queries 4 §6.
        /// </summary>
        internal static MediaQueryContext FromContainer(HtmlContainerInt container, string? media)
        {
            var size = container.PageSize;
            double? width = size.Width > 0 ? size.Width : null;
            double? height = size.Height > 0 ? size.Height : null;

            return new MediaQueryContext(
                string.IsNullOrEmpty(media) ? "all" : media!,
                width,
                height,
                96d,
                container.PreferredColorScheme);
        }
    }
}
