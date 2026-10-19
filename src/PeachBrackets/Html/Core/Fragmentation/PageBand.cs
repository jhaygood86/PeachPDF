namespace PeachPDF.Html.Core.Fragmentation
{
    /// <summary>
    /// A fragmentainer's block-axis extent: the coordinates its content may occupy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The value form of the band <see cref="FragmentainerContext"/> exposes as
    /// <c>BandTop</c>/<c>BandBottom</c>, so a question about "the fragmentainer this coordinate is in"
    /// can be asked of the page grid without a <see cref="FragmentainerContext"/> to hand — which
    /// matters because a box being laid out is not always inside the fragmentainer currently being
    /// filled (monolithic content, an engine's measurement pass, a box below a tall margin).
    /// </para>
    /// <para>
    /// Bands are contiguous: one band's <see cref="Bottom"/> is the next one's <see cref="Top"/>, on the
    /// uniform grid by construction and in the variable-geometry table by how it is built. That identity
    /// is what lets "the slot after this one's top" and "this band's bottom" be the same coordinate.
    /// </para>
    /// </remarks>
    internal readonly record struct PageBand(double Top, double Bottom)
    {
        internal double Height => Bottom - Top;
    }
}
