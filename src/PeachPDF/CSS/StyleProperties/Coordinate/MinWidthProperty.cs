namespace PeachPDF.CSS
{
    internal sealed class MinWidthProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.LengthOrPercentOrStretchConverter.OrDefault(Length.Zero);

        internal MinWidthProperty()
            : base(PropertyNames.MinWidth, PropertyFlags.Animatable)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}