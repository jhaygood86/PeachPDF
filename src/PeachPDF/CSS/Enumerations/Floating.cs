namespace PeachPDF.CSS
{
    internal enum Floating : byte
    {
        None,
        Left,
        Right,

        /// <summary>
        /// CSS Generated Content for Paged Media (css-gcpm-3) footnotes: the box is pulled out of
        /// normal flow entirely, rather than positioned beside its siblings like <see cref="Left"/>/
        /// <see cref="Right"/> — see <c>DomParser.DetachFootnoteBodies</c> and
        /// <c>Html/Core/Dom/CssBoxFootnoteCall.cs</c>. Deliberately excluded from
        /// <c>CssBox.IsFloated</c>/<c>CssLayoutEngine.FloatBox</c>, which only ever handle
        /// <see cref="Left"/>/<see cref="Right"/>.
        /// </summary>
        Footnote
    }
}