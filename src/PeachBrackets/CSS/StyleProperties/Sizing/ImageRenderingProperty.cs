namespace PeachPDF.CSS
{
    internal sealed class ImageRenderingProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.ImageRenderingConverter.OrDefault(ImageRenderingMode.Auto);

        internal ImageRenderingProperty()
            : base(PropertyNames.ImageRendering)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
