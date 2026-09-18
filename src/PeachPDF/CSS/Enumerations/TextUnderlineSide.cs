namespace PeachPDF.CSS
{
    /// <summary>
    /// The <c>left</c>/<c>right</c> half of <c>text-underline-position</c>'s compound grammar
    /// (<see href="https://www.w3.org/TR/css-text-decor-4/#text-underline-position-property">css-text-decor-4
    /// §2.5</see>: <c>auto | [ from-font | under ] || [ left | right ]</c>) - meaningful only under a
    /// vertical <c>writing-mode</c>, where it pins the underline to a literal physical side regardless of
    /// the writing mode's own default under/over mapping (and can make a same-line overline switch sides
    /// too - see <c>FragmentPainter.ResolveUnderlineCross</c>). Inert under <c>horizontal-tb</c>. Kept as
    /// its own <see cref="TextUnderlinePosition"/>-independent field on <c>CssBox</c> (issue #1146) rather
    /// than folded into that enum, since the two halves of the grammar combine independently
    /// (<c>||</c>) - see <c>TextUnderlinePositionGrammar</c>.
    /// </summary>
    internal enum TextUnderlineSide
    {
        /// <summary>The initial value: no forced physical side - the writing mode's own default under/over mapping applies.</summary>
        Auto,

        /// <summary>Pins the underline to the physical left edge.</summary>
        Left,

        /// <summary>Pins the underline to the physical right edge.</summary>
        Right
    }
}
