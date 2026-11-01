namespace PeachBrackets.Css
{
    using static Converters;

    internal sealed class BorderImageProperty : ShorthandProperty
    {
        private static readonly IValueConverter ImageConverter = WithAny(
            OptionalImageSourceConverter.Option().For(PropertyNames.BorderImageSource),
            WithOrder(
                BorderImageSliceProperty.ValueGrammar.Option().For(PropertyNames.BorderImageSlice),
                BorderImageWidthProperty.ValueGrammar.StartsWithDelimiter()
                    .Option()
                    .For(PropertyNames.BorderImageWidth),
                BorderImageOutsetProperty.ValueGrammar.StartsWithDelimiter()
                    .Option()
                    .For(PropertyNames.BorderImageOutset)),
            BorderImageRepeatProperty.ValueGrammar.Option().For(PropertyNames.BorderImageRepeat)).OrDefault();

        internal BorderImageProperty()
            : base(PropertyNames.BorderImage)
        {
        }

        internal override IValueConverter Converter => ImageConverter;
    }
}