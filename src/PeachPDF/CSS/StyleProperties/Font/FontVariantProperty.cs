#nullable disable

namespace PeachPDF.CSS
{
    using static Converters;

    /// <summary>
    /// The real CSS Fonts Level 3 <c>font-variant</c> shorthand, within PeachPDF's implemented subset:
    /// a single optional <c>font-variant-caps</c> keyword combined (in any order) with the existing
    /// <c>font-variant-ligatures</c>/new <c>font-variant-numeric</c>/<c>font-variant-east-asian</c>
    /// token grammars, or the literal <c>none</c> (sets ligatures to <c>none</c>, everything else to
    /// <c>normal</c>), or Prince's proprietary <c>prince-opentype(...)</c> function (decomposed into
    /// the same four longhands plus <c>font-feature-settings</c> - see
    /// <see cref="PrinceOpenTypeConverter"/>). <c>font-variant-alternates</c>/
    /// <c>font-variant-position</c>/emoji are not implemented and so are not covered here.
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
                        new FontVariantEastAsianValueConverter().Option().For(PropertyNames.FontVariantEastAsian)))
                .Or(new PrinceOpenTypeConverter())
                .OrGlobalValue();

        internal FontVariantProperty()
            : base(PropertyNames.FontVariant, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
