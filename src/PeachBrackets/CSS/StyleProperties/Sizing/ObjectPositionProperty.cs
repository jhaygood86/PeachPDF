namespace PeachPDF.CSS
{
    internal sealed class ObjectPositionProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.PointConverter.OrDefault(Point.Center);

        internal ObjectPositionProperty()
            : base(PropertyNames.ObjectPosition, PropertyFlags.Animatable)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}