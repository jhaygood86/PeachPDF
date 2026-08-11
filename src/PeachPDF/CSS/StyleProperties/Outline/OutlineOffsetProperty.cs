namespace PeachPDF.CSS
{
    internal sealed class OutlineOffsetProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.LengthConverter.OrDefault(Length.Zero);

        internal OutlineOffsetProperty()
            : base(PropertyNames.OutlineOffset, PropertyFlags.Animatable)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
