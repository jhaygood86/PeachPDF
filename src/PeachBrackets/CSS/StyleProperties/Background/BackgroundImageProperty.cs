namespace PeachPDF.CSS
{
    internal sealed class BackgroundImageProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.MultipleImageSourceConverter.OrDefault();

        internal BackgroundImageProperty()
            : base(PropertyNames.BackgroundImage)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}