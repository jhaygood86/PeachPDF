namespace PeachPDF.CSS
{
    internal sealed class MaxWidthProperty : Property
    {
        private static readonly IValueConverter
            StyleConverter = Converters.OptionalLengthOrPercentOrStretchConverter.OrDefault();

        internal MaxWidthProperty()
            : base(PropertyNames.MaxWidth, PropertyFlags.Animatable)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}