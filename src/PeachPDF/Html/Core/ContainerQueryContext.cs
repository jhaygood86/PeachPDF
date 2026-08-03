#nullable enable

namespace PeachPDF.Html.Core
{
    /// <summary>
    /// One eligible <c>@container</c> query container's resolved size geometry for the current layout
    /// pass - the per-container analogue of <see cref="MediaQueryContext"/>'s single, page-wide context.
    /// Built by <see cref="HtmlContainerInt"/> after each layout pass (see
    /// <see cref="ContainerQuerySizes"/>) for every box whose resolved <c>container-type</c> is
    /// <c>size</c> or <c>inline-size</c>.
    /// </summary>
    /// <param name="InlineSizePt">The container's resolved inline-axis (width, in a horizontal writing
    /// mode) size in points.</param>
    /// <param name="BlockSizePt">The container's resolved block-axis (height) size in points, or
    /// <c>null</c> when the container is <c>inline-size</c>-only - it does not track the block axis, so
    /// <c>height</c>/<c>block-size</c>/<c>aspect-ratio</c>/<c>orientation</c> conditions against it never
    /// match (CSS Containment 3).</param>
    /// <param name="ContainerName">The container's own declared <c>container-name</c> list (raw,
    /// space-separated text, or <c>"none"</c>), for informational/debugging purposes - name matching
    /// itself happens earlier, in <see cref="Dom.CssBox.FindNearestQueryContainer"/>.</param>
    internal readonly record struct ContainerQueryContext(double InlineSizePt, double? BlockSizePt, string ContainerName);
}
