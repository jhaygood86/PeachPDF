namespace PeachPDF.CSS
{
    internal sealed class ImageOrientationProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.ImageOrientationConverter.OrDefault();

        internal ImageOrientationProperty()
            : base(PropertyNames.ImageOrientation)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
