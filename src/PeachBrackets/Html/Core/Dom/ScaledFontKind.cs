namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// Why a <see cref="CssRect"/> is measured and painted with a scaled-down face rather than its owner
    /// box's own <see cref="DerivedStyle.ActualFont"/> - the discriminator
    /// <see cref="CssBox.ResolveWordFont"/> uses to pick which scaled face that is.
    /// </summary>
    internal enum ScaledFontKind : byte
    {
        /// <summary>The ordinary case: the box's own font, at its own size.</summary>
        None,

        /// <summary>
        /// A synthesized <c>font-variant-caps: small-caps</c>/<c>all-small-caps</c> run - an
        /// originally-lowercase (or, under all-small-caps, uppercase) run drawn upper-cased and smaller
        /// because the resolved font has no real <c>smcp</c>/<c>c2sc</c> GSUB data.
        /// </summary>
        SmallCaps,

        /// <summary>
        /// A synthesized <c>font-variant-position: sub</c>/<c>super</c> run - drawn smaller and with a
        /// shifted baseline because the resolved font has no real <c>subs</c>/<c>sups</c> GSUB data.
        /// Unlike <see cref="SmallCaps"/> this applies uniformly to the whole word, so no run splitting
        /// is involved.
        /// </summary>
        SubSuperscript
    }
}
