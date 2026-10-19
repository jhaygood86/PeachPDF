namespace PeachPDF.CSS
{
    internal sealed class FillOpacityProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.NumberConverter.Or(Converters.PercentConverter).OrDefault(1f);

        internal FillOpacityProperty()
            : base(PropertyNames.FillOpacity, PropertyFlags.Animatable)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}