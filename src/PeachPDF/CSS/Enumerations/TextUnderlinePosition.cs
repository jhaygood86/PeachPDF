namespace PeachPDF.CSS
{
    /// <summary>
    /// <c>text-underline-position</c>'s value set, restricted to <c>auto | from-font | under</c>
    /// (<see href="https://www.w3.org/TR/css-text-decor-3/#text-underline-position-property">css-text-decor-3
    /// §2.5</see>'s full grammar is <c>auto | [ from-font | under ] || [ left | right ]</c>; <c>left</c>/
    /// <c>right</c> only matter under a vertical <c>writing-mode</c> and are not implemented — see
    /// <c>docs/html-css-support.md</c>).
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
