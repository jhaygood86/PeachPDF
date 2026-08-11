namespace PeachPDF.CSS
{
    internal sealed class BookmarkStateProperty : Property
    {
        internal BookmarkStateProperty() : base(PropertyNames.BookmarkState)
        {
        }

        internal override IValueConverter Converter => Converters.BookmarkStateConverter.OrDefault();
    }
}
