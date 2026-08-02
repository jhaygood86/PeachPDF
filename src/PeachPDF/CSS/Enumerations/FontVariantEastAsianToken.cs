namespace PeachPDF.CSS
{
    /// <summary>
    /// One toggle keyword from the <c>font-variant-east-asian</c> grammar (each of the 3 axes -
    /// variant forms, width, ruby - may appear at most once, in any order, combined with others -
    /// see <see cref="FontVariantEastAsianValueConverter"/>).
    /// </summary>
    internal enum FontVariantEastAsianToken : byte
    {
        Jis78Forms,
        Jis83Forms,
        Jis90Forms,
        Jis04Forms,
        Simplified,
        Traditional,
        FullWidth,
        ProportionalWidth,
        Ruby
    }
}
