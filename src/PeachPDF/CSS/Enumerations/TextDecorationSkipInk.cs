namespace PeachPDF.CSS
{
    /// <summary>
    /// <c>text-decoration-skip-ink</c>'s <c>auto | none | all</c> grammar
    /// (<see href="https://www.w3.org/TR/css-text-decor-4/#text-decoration-skip-ink-property">CSS Text
    /// Decoration 4 §2.5</see>) — whether an underline or overline interrupts itself where it would
    /// cross a glyph's ink.
    /// </summary>
    internal enum TextDecorationSkipInk
    {
        /// <summary>The initial value: the UA may interrupt the line where it crosses ink, and PeachPDF does.</summary>
        Auto,

        /// <summary>The line is never interrupted — it is drawn straight through every descender.</summary>
        None,

        /// <summary>The line must be interrupted wherever it crosses ink.</summary>
        All
    }
}
