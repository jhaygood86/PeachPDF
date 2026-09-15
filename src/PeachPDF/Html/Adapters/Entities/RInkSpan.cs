namespace PeachPDF.Html.Adapters.Entities
{
    /// <summary>
    /// One horizontal range of a text run in which glyph ink crosses a band — what
    /// <see cref="RGraphics.GetInkCrossings"/> reports, and what <c>text-decoration-skip-ink</c>
    /// (<see href="https://www.w3.org/TR/css-text-decor-4/#text-decoration-skip-ink-property">CSS Text
    /// Decoration 4 §2.5</see>) subtracts from an underline or overline.
    /// </summary>
    /// <param name="Start">the range's left edge, in the same user-space units as the run's baseline origin</param>
    /// <param name="End">the range's right edge</param>
    internal readonly record struct RInkSpan(double Start, double End);
}
