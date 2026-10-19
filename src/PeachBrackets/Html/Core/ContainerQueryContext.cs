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
    /// <param name="WidthPt">The container's resolved physical width in points, for the <c>width</c>
    /// size feature and the physical <c>aspect-ratio</c>/<c>orientation</c> features (CSS Containment 3
    /// §7.3 - all three are defined against physical width/height, never the writing-mode-relative
    /// axis).</param>
    /// <param name="HeightPt">The container's resolved physical height in points, for the <c>height</c>
    /// size feature and <c>aspect-ratio</c>/<c>orientation</c>, or <c>null</c> when the container is
    /// <c>inline-size</c>-only - it does not track the block axis (which the physical height maps to
    /// under <c>horizontal-tb</c>), so these features against it never match.</param>
    /// <param name="InlineSizePt">The container's own resolved inline-axis size in points, for the
    /// <c>inline-size</c> size feature - identical to <see cref="WidthPt"/> under the container's default
    /// <c>horizontal-tb</c>, but its physical height under <c>vertical-rl</c>/<c>vertical-lr</c> (CSS
    /// Writing Modes 4 §7.1).</param>
    /// <param name="BlockSizePt">The container's own resolved block-axis size in points, for the
    /// <c>block-size</c> size feature - identical to <see cref="HeightPt"/> under <c>horizontal-tb</c>,
    /// but its physical width under a vertical writing mode. <c>null</c> when the container is
    /// <c>inline-size</c>-only - it does not track the block axis, so <c>block-size</c> against it never
    /// matches (CSS Containment 3).</param>
    /// <param name="ContainerName">The container's own declared <c>container-name</c> list (raw,
    /// space-separated text, or <c>"none"</c>), for informational/debugging purposes - name matching
    /// itself happens earlier, in <see cref="Dom.CssBox.FindNearestQueryContainer"/>.</param>
    /// <param name="PixelsPerPoint">The ambient <c>PdfGenerateConfig.PixelsPerInch / 72</c> catch-up
    /// multiplier (issue #814's convention) a length-valued feature's resolved value must be scaled by
    /// before comparing against <see cref="WidthPt"/>/<see cref="HeightPt"/>/<see cref="InlineSizePt"/>/
    /// <see cref="BlockSizePt"/> - those are PeachPDF's internal, <c>PixelsPerPoint</c>-inflated layout
    /// coordinate space, not true PDF points, whenever <c>PixelsPerInch</c> is non-default. See
    /// <see cref="MediaQueryMatcher.CompareLength"/> (issue #820), same as its <see cref="MediaQueryContext"/>
    /// analogue.</param>
    internal readonly record struct ContainerQueryContext(
        double WidthPt, double? HeightPt, double InlineSizePt, double? BlockSizePt, string ContainerName,
        double PixelsPerPoint = 1.0);
}
