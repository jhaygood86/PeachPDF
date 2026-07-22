namespace PeachPDF.CSS
{
    internal sealed class FillProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.PaintConverter.OrGlobalValue();

        internal FillProperty()
            : base(PropertyNames.Fill, PropertyFlags.Animatable)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}