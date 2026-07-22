namespace PeachPDF.CSS
{
    internal sealed class StrokeWidthProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.LengthOrPercentConverter.OrGlobalValue();

        internal StrokeWidthProperty()
            : base(PropertyNames.StrokeWidth, PropertyFlags.Animatable)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}