namespace PeachPDF.CSS
{
    internal sealed class MinHeightProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.LengthOrPercentOrStretchConverter.OrDefault(Length.Zero);

        internal MinHeightProperty()
            : base(PropertyNames.MinHeight, PropertyFlags.Animatable)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}