namespace PeachPDF.CSS
{
    internal sealed class BookmarkLabelProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.BookmarkLabelConverter.OrDefault();

        internal BookmarkLabelProperty() : base(PropertyNames.BookmarkLabel)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
