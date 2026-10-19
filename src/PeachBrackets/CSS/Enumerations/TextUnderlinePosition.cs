namespace PeachPDF.CSS
{
    /// <summary>
    /// The <c>from-font</c>/<c>under</c> half of <c>text-underline-position</c>'s compound grammar
    /// (<see href="https://www.w3.org/TR/css-text-decor-4/#text-underline-position-property">css-text-decor-4
    /// §2.5</see>: <c>auto | [ from-font | under ] || [ left | right ]</c>). <c>from-font</c> does not
    /// exist in css-text-decor-3 at all - that level's own grammar is only
    /// <c>auto | [ under || [ left | right ] ]</c> - so this whole compound shape, and every citation for
    /// it, is a level-4 addition (verified against the live published text of both levels). The
    /// <c>left</c>/<c>right</c> half (issue #1146) is the independent <see cref="TextUnderlineSide"/>
    /// field - see <c>TextUnderlinePositionGrammar</c> for how the two combine.
    /// </summary>
    internal enum TextUnderlinePosition
    {
        /// <summary>The initial value: the UA decides, but must place the line at or under the alphabetic baseline.</summary>
        Auto,

        /// <summary>Use the first available font's own preferred underline offset, if it has one; otherwise behaves as <c>auto</c>.</summary>
        FromFont,

        /// <summary>Positioned under the element's text so as not to cross descenders, per §2.5.</summary>
        Under
    }
}
