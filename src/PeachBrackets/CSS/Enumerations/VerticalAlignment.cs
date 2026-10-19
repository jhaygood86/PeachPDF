namespace PeachPDF.CSS
{
    internal enum VerticalAlignment : byte
    {
        Baseline,
        Sub,
        Super,
        TextTop,
        TextBottom,
        Middle,
        Top,
        Bottom,

        /// <summary>
        /// PeachPDF-internal sentinel for the deprecated <c>&lt;img align="middle"&gt;</c> HTML attribute
        /// (same idea as <c>-webkit-baseline-middle</c>) - not a CSS keyword, never produced by parsing
        /// authored CSS text. <see cref="PeachPDF.CSS.Keywords.PeachBaselineMiddle"/>.
        /// </summary>
        PeachBaselineMiddle
    }
}