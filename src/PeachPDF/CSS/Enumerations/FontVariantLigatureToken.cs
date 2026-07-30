namespace PeachPDF.CSS
{
    /// <summary>
    /// One toggle keyword from the <c>font-variant-ligatures</c> grammar (each may appear at most
    /// once, in any order, combined with others - see <see cref="FontVariantLigaturesProperty"/>).
    /// Only <see cref="CommonLigatures"/>/<see cref="NoCommonLigatures"/> affect PeachPDF's GSUB
    /// shaping (via <c>liga</c>/<c>clig</c>); the discretionary/historical/contextual tokens parse
    /// but are not yet applied - see .claude/accepted-gaps/no-text-shaping.md.
    /// </summary>
    internal enum FontVariantLigatureToken : byte
    {
        CommonLigatures,
        NoCommonLigatures,
        DiscretionaryLigatures,
        NoDiscretionaryLigatures,
        HistoricalLigatures,
        NoHistoricalLigatures,
        Contextual,
        NoContextual
    }
}
