namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// css-gcpm-3's <c>@footnote</c> area rule, resolved for one pagination slot: the note area's own box
    /// model, plus the footnote counter's step for that page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every note-area coordinate is derived here rather than summed at each call site. The two consumers
    /// - the footnote convergence loop's height math and the fragment-tree divider rect - must produce
    /// byte-identical geometry or the divider paints at a different Y than the band that was reserved, and
    /// a shared record is what makes that structural instead of a comment asking two call sites to agree.
    /// </para>
    /// <para>
    /// The bodies are the area's <em>content box</em>: <see cref="TopPadding"/> is <c>margin-top</c>,
    /// <see cref="DividerThickness"/> is <c>border-top-width</c> and <see cref="DividerToBodyGap"/> is
    /// <c>padding-top</c>, all of which sit above the content and are added to it - so a declared
    /// <c>height</c> sizes the band the bodies stack in, not the area including its own chrome.
    /// </para>
    /// </remarks>
    /// <param name="TopPadding">Space above the divider rule (<c>margin-top</c>).</param>
    /// <param name="DividerThickness">Thickness of the divider rule (<c>border-top-width</c>).</param>
    /// <param name="DividerToBodyGap">Space between the divider and the first body (<c>padding-top</c>).</param>
    /// <param name="Height">
    /// A declared fixed content height, or null for <c>auto</c> - the note area sized to its content.
    /// </param>
    /// <param name="MaxHeight">
    /// A declared <c>max-height</c>, never a clamp on the used height: purely the signal
    /// <c>footnote-policy</c> reads to decide whether a page's notes "fit".
    /// </param>
    /// <param name="Step">
    /// How much the footnote counter advances per note on this page (<c>counter-increment: footnote</c>).
    /// </param>
    /// <param name="DividerColor">
    /// The declared <c>border-top-color</c>, or null to paint the UA default.
    /// </param>
    internal readonly record struct FootnoteAreaRule(
        double TopPadding,
        double DividerThickness,
        double DividerToBodyGap,
        double? Height,
        double? MaxHeight,
        int Step,
        string? DividerColor)
    {
        /// <summary>Space (layout px, i.e. points) above a page's footnote-area divider rule.</summary>
        internal const double DefaultTopPadding = 4;

        /// <summary>Thickness of the footnote area's top divider rule.</summary>
        internal const double DefaultDividerThickness = 1;

        /// <summary>Space between the divider rule and the first footnote body.</summary>
        internal const double DefaultDividerToBodyGap = 4;

        /// <summary>How much the footnote counter advances per note when <c>@footnote</c> says nothing.</summary>
        internal const int DefaultStep = 1;

        /// <summary>
        /// PeachPDF's own UA default note area: a 1pt solid divider with 4pt above and 4pt below, sized to
        /// its content, with no <c>max-height</c> and a step of one.
        /// </summary>
        internal static FootnoteAreaRule Ua { get; } = new(
            DefaultTopPadding, DefaultDividerThickness, DefaultDividerToBodyGap,
            Height: null, MaxHeight: null, DefaultStep, DividerColor: null);

        /// <summary>Everything above the content box: margin-top + border-top + padding-top.</summary>
        internal double ChromeHeight => TopPadding + DividerThickness + DividerToBodyGap;

        /// <summary>
        /// The used content height for a stack of bodies whose own natural height is
        /// <paramref name="naturalContentHeight"/> - the declared fixed <c>height</c> when there is one,
        /// otherwise the content's own height (<c>height: auto</c>).
        /// </summary>
        internal double UsedContentHeight(double naturalContentHeight) => Height ?? naturalContentHeight;

        /// <summary>The whole note area's reserved height, chrome included.</summary>
        internal double TotalHeight(double naturalContentHeight) => ChromeHeight + UsedContentHeight(naturalContentHeight);

        /// <summary>Where the divider rule sits, given the area's own outer top edge.</summary>
        internal double DividerTopFor(double areaTop) => areaTop + TopPadding;

        /// <summary>Where the first body's top sits, given the area's own outer top edge.</summary>
        internal double BodiesTopFor(double areaTop) => areaTop + ChromeHeight;
    }
}
