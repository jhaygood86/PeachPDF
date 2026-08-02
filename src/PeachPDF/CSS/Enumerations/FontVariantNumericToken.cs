namespace PeachPDF.CSS
{
    /// <summary>
    /// One toggle keyword from the <c>font-variant-numeric</c> grammar (each of the 4 axes - figure,
    /// spacing, fraction, ordinal/slashed-zero - may appear at most once, in any order, combined with
    /// others - see <see cref="FontVariantNumericValueConverter"/>).
    /// </summary>
    internal enum FontVariantNumericToken : byte
    {
        LiningNums,
        OldstyleNums,
        ProportionalNums,
        TabularNums,
        DiagonalFractions,
        StackedFractions,
        Ordinal,
        SlashedZero
    }
}
