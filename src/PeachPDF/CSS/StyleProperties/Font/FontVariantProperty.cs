#nullable disable

namespace PeachPDF.CSS
{
    using static Converters;

    /// <summary>
    /// The real CSS Fonts <c>font-variant</c> shorthand, within PeachPDF's implemented subset: a single
    /// optional <c>font-variant-caps</c> keyword, a single optional <c>font-variant-position</c>
    /// keyword, and a single optional <c>font-variant-alternates</c> clause set, combined (in any order)
    /// with the <c>font-variant-ligatures</c>/<c>font-variant-numeric</c>/<c>font-variant-east-asian</c>
    /// token grammars, or the literal <c>none</c> (sets ligatures to <c>none</c>, everything else to
    /// <c>normal</c>), or Prince's proprietary <c>prince-opentype(...)</c> function (decomposed into the
    /// same longhands plus <c>font-feature-settings</c> - see <see cref="PrinceOpenTypeConverter"/>).
    /// Emoji presentation is not implemented and so is not covered here.
    /// </summary>
    internal sealed class FontVariantProperty : ShorthandProperty
    {
        private static readonly IValueConverter StyleConverter =
            new IdentifierValueConverter<object>(Keywords.None, null).For(PropertyNames.FontVariantLigatures)
                .Or(
                    WithAny(
                        FontVariantCapsConverter.Option().For(PropertyNames.FontVariantCaps),
                        new FontVariantLigaturesValueConverter().Option().For(PropertyNames.FontVariantLigatures),
                        new FontVariantNumericValueConverter().Option().For(PropertyNames.FontVariantNumeric),
                        new FontVariantEastAsianValueConverter().Option().For(PropertyNames.FontVariantEastAsian),
                        FontVariantPositionConverter.Option().For(PropertyNames.FontVariantPosition),
                        new FontVariantAlternatesGrammar().Option().For(PropertyNames.FontVariantAlternates)))
                .Or(new PrinceOpenTypeConverter())
                .OrGlobalValue();

        internal FontVariantProperty()
            : base(PropertyNames.FontVariant, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
