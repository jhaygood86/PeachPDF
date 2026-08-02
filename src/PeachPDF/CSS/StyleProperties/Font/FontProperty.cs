namespace PeachPDF.CSS
{
    using static Converters;

    internal sealed class FontProperty : ShorthandProperty
    {
        private static readonly IValueConverter StyleConverter = WithOrder(
            WithAny(
                FontStyleConverter.Option().For(PropertyNames.FontStyle),
                FontVariantCss2Converter.Option().For(PropertyNames.FontVariantCaps),
                FontWeightConverter.Or(WeightIntegerConverter).Option().For(PropertyNames.FontWeight),
                FontStretchConverter.Option().For(PropertyNames.FontStretch)),
            WithOrder(
                FontSizeConverter.Required().For(PropertyNames.FontSize),
                LineHeightConverter.StartsWithDelimiter().Option().For(PropertyNames.LineHeight),
                FontFamiliesConverter.Required().For(PropertyNames.FontFamily)))
            .Or(SystemFontConverter)
            .OrGlobalValue();

        internal FontProperty()
            : base(PropertyNames.Font, PropertyFlags.Inherited | PropertyFlags.Animatable)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}